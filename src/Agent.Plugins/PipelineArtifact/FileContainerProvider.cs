using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using Microsoft.VisualStudio.Services.Content.Common;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;
using Microsoft.VisualStudio.Services.FileContainer;
using Microsoft.VisualStudio.Services.FileContainer.Client;
using Microsoft.VisualStudio.Services.WebApi;
using Minimatch;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Agent.Plugins.PipelineArtifact
{
    class FileContainerProvider : IArtifactProvider
    {
        private readonly FileContainerHttpClient containerClient;
        private readonly CallbackAppTraceSource tracer;

        public FileContainerProvider(VssConnection connection, CallbackAppTraceSource tracer)
        {
            containerClient = connection.GetClient<FileContainerHttpClient>();
            this.tracer = tracer;
        }
        public async Task DownloadSingleArtifactAsync(PipelineArtifactDownloadParameters downloadParameters, BuildArtifact buildArtifact, CancellationToken cancellationToken)
        {
            await this.DownloadFileContainerAsync(downloadParameters.ProjectId, buildArtifact, downloadParameters.TargetDirectory, downloadParameters.MinimatchFilters, cancellationToken);
        }

        public async Task DownloadMultipleArtifactsAsync(PipelineArtifactDownloadParameters downloadParameters, IEnumerable<BuildArtifact> buildArtifacts, CancellationToken cancellationToken)
        {
            await this.DownloadFileContainersAsync(downloadParameters.ProjectId, buildArtifacts, downloadParameters.TargetDirectory, downloadParameters.MinimatchFilters, cancellationToken);
        }

        public async Task DownloadFileContainersAsync(Guid projectId, IEnumerable<BuildArtifact> buildArtifacts, string targetDirectory, IEnumerable<string> minimatchFilters, CancellationToken cancellationToken)
        {
            foreach (var buildArtifact in buildArtifacts)
            {
                var dirPath = Path.Combine(targetDirectory, buildArtifact.Name);
                Directory.CreateDirectory(dirPath);
                await DownloadFileContainerAsync(projectId, buildArtifact, dirPath, minimatchFilters, cancellationToken);
            }
        }

        private (long, string) ParseContainerId(string resourceData)
        {
            // Example of resourceData: "#/7029766/artifacttool-alpine-x64-Debug"
            var segments = resourceData.Split('/');
            long containerId;
            if (segments.Length < 3)
            {
                throw new ArgumentException($"Resource data value '{resourceData}' invalid");
            }
            if (segments.Length >= 3 && segments[0] == "#" && long.TryParse(segments[1], out containerId))
            {
                var artifactName = String.Join('/', segments[2]);
                return(
                        containerId,
                        artifactName
                    );
            }
            else
            {
                var message = $"Resource data value '{resourceData}' was not expected.";
                throw new ArgumentException(message, "resourceData");
            }
        }

        private async Task DownloadFileContainerAsync(Guid projectId, BuildArtifact artifact, string rootPath, IEnumerable<string> minimatchPatterns, CancellationToken cancellationToken)
        {
            var containerIdAndRoot = ParseContainerId(artifact.Resource.Data);

            var items = await containerClient.QueryContainerItemsAsync(
                containerIdAndRoot.Item1,
                projectId,
                containerIdAndRoot.Item2
                );

            tracer.Info($"Start downloading FCS artifact- {artifact.Name}");
            IEnumerable<Func<string, bool>> minimatcherFuncs = MinimatchHelper.GetMinimatchFuncs(minimatchPatterns, tracer);
            if (minimatcherFuncs !=null && minimatcherFuncs.Count() !=0)
            {
                items = this.GetFilteredItems(items, minimatcherFuncs, artifact.Name);
            }

            var folderItems = items.Where(i => i.ItemType == ContainerItemType.Folder);
            Parallel.ForEach(folderItems, (folder) =>
            {
                var targetPath = ResolveTargetPath(rootPath, folder, artifact.Name);
                Directory.CreateDirectory(targetPath);
            });

            var fileItems = items.Where(i => i.ItemType == ContainerItemType.File);

            var batchItemsBlock = new BatchBlock<FileContainerItem>(
                batchSize: 8,
                new GroupingDataflowBlockOptions()
                {
                    CancellationToken = cancellationToken,
                });

            var fetchStream = NonSwallowingTransformManyBlock.Create<IEnumerable<FileContainerItem>, (Stream, string)>(
                async itemsStream =>
                {
                    List<(Stream, string)> collection= new List<(Stream, string)>();
                    foreach (var item in itemsStream)
                    {
                        var targetPath = ResolveTargetPath(rootPath, item, artifact.Name);
                        Stream stream = await this.DownloadFileFromContainerAsync(containerIdAndRoot, projectId, containerClient, item, cancellationToken);
                        collection.Add((stream, targetPath));
                    }
                    return collection;
                },
                new ExecutionDataflowBlockOptions()
                {
                    BoundedCapacity = 8,
                    MaxDegreeOfParallelism = Environment.ProcessorCount * 8,
                    CancellationToken = cancellationToken,
                });

            var downloadBlock = NonSwallowingActionBlock.Create<(Stream stream, string targetPath)>(
                item =>
                {
                    using (item.stream)
                    {
                        int retryCount = 0;
                        try
                        {
                            tracer.Info($"Downloading: {item.targetPath}");
                            using (var fileStream = new FileStream(item.targetPath, FileMode.Create))
                            {
                                item.stream.CopyTo(fileStream);
                            }
                        }
                        catch (IOException exception) when (retryCount < 3)
                        {
                            tracer.Warn($"Exception caught: {exception.Message}, on retry count {retryCount}, Retrying");
                            retryCount++;
                        }
                    }               
                },
                new ExecutionDataflowBlockOptions()
                {
                    BoundedCapacity = 8,
                    MaxDegreeOfParallelism = Environment.ProcessorCount * 8,
                    CancellationToken = cancellationToken,
                });

            batchItemsBlock.LinkTo(fetchStream, new DataflowLinkOptions() { PropagateCompletion = true });
            fetchStream.LinkTo(downloadBlock, new DataflowLinkOptions() { PropagateCompletion = true });

            await batchItemsBlock.SendAllAndCompleteAsync(fileItems, downloadBlock, cancellationToken).ConfigureAwait(false);
        }

        private async Task<Stream> DownloadFileFromContainerAsync(
            (long, string) containerIdAndRoot,
            Guid scopeIdentifier,
            FileContainerHttpClient containerClient,
            FileContainerItem item,
            CancellationToken cancellationToken)
        {
            Stream responseStream = await AsyncHttpRetryHelper.InvokeAsync(
                async () =>
                {
                    Stream internalResponseStream = await containerClient.DownloadFileAsync(containerIdAndRoot.Item1, item.Path, cancellationToken, scopeIdentifier).ConfigureAwait(false);
                    return internalResponseStream;
                },
                maxRetries: 5,
                cancellationToken: cancellationToken,
                tracer: this.tracer,
                continueOnCapturedContext: false
                );

            return responseStream;
        }

        private string ResolveTargetPath(string rootPath, FileContainerItem item, string artifactName)
        {
            if(item.Path.Length > artifactName.Length)
            {
                artifactName += "/";
            }
            var itemPathWithoutDirectoryPrefix = item.Path.Replace(artifactName, "");
            var absolutePath = Path.Combine(rootPath, itemPathWithoutDirectoryPrefix);
            return absolutePath;
        }

        private List<FileContainerItem> GetFilteredItems(List<FileContainerItem> items, IEnumerable<Func<string, bool>> minimatchFuncs, string artifactName)
        {
            List<FileContainerItem> filteredItems = new List<FileContainerItem>();
            int index = artifactName.Length;
            foreach (FileContainerItem item in items)
            {
                var itemPathWithoutDirectoryPrefix = (index != -1 && index < item.Path.Length) ? item.Path.Substring(index + 1) : string.Empty;
                if (minimatchFuncs.Any(match => match(itemPathWithoutDirectoryPrefix)))
                {
                    filteredItems.Add(item);
                }
            }
            var excludedItems = items.Except(filteredItems).ToList();
            foreach(FileContainerItem item in excludedItems)
            {
                tracer.Info($"Item excluded: {item.Path}");
            }
            return filteredItems;
        }
    }
}
