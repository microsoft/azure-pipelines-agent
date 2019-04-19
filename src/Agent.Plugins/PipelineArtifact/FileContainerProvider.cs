using Microsoft.TeamFoundation.Build.WebApi;
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
        private FileContainerHttpClient containerClient;
        private CallbackAppTraceSource tracer;
        // https://github.com/Microsoft/azure-pipelines-task-lib/blob/master/node/docs/findingfiles.md#matchoptions
        private static readonly Options minimatchOptions = new Options
        {
            Dot = true,
            NoBrace = true,
            NoCase = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? true : false
        };

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

        public async Task DownloadFileContainersAsync(Guid projectId, IEnumerable<BuildArtifact> buildArtifacts, string targetDirectory, string[] minimatchFilters, CancellationToken cancellationToken)
        {
            if (buildArtifacts.Count() == 0)
            {
                return;
            }
            foreach (var buildArtifact in buildArtifacts)
            {
                var specificPath = Path.Combine(targetDirectory, buildArtifact.Name);
                Directory.CreateDirectory(specificPath);
                await DownloadFileContainerAsync(projectId, buildArtifact, specificPath, minimatchFilters, cancellationToken);
            }
        }

        private Tuple<long, string> ParseContainerId(string resourceData)
        {
            // Example of resourceData: "#/7029766/artifacttool-alpine-x64-Debug"
            var segments = resourceData.Split('/');

            long containerId;
            if(segments.Length < 3)
            {
                throw new ArgumentException($"Resource data value '{resourceData}' invalid");
            }
            if (segments.Length >= 3 && segments[0] == "#" && long.TryParse(segments[1], out containerId))
            {
                int index = resourceData.IndexOf('/', resourceData.IndexOf('/') + 1);
                return new Tuple<long, string>(
                    containerId,
                    resourceData.Substring(index+1)
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

            IEnumerable<Func<string, bool>> minimatcherFuncs = this.GetMinimatchFuncs(minimatchPatterns);
            if(minimatcherFuncs !=null && minimatcherFuncs.Count() !=0)
            {
                items = this.GetFilteredItems(items, minimatcherFuncs);
            }

            // Group items by type because we want to create the folder structure first.
            var groupedItems = from i in items
                               group i by i.ItemType into g
                               select g;

            // Now create the folders.
            var folderItems = groupedItems.SingleOrDefault(i => i.Key == ContainerItemType.Folder);
            if (folderItems != null)
            {
                Parallel.ForEach(folderItems, (folder) =>
                {
                    var targetPath = ResolveTargetPath(rootPath, folder, artifact.Name);
                    Directory.CreateDirectory(targetPath);
                });
            }

            var fileItems = groupedItems.SingleOrDefault(i => i.Key == ContainerItemType.File);

            var batchItemsBlock = new BatchBlock<FileContainerItem>(
                batchSize: 1000,
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
                        Stream stream = await this.DownloadFileFromContainerAsync(containerIdAndRoot, projectId, containerClient, item, targetPath, cancellationToken);
                        collection.Add((stream, targetPath));
                    }
                    return collection;
                },
                new ExecutionDataflowBlockOptions()
                {
                    BoundedCapacity = 1000,
                    MaxDegreeOfParallelism = 250,
                    CancellationToken = cancellationToken,
                });

            var downloadBlock = NonSwallowingActionBlock.Create<(Stream stream, string targetPath)>(
                item =>
                {
                    using (item.stream)
                    {
                        using (var fileStream = new FileStream(item.targetPath, FileMode.Create))
                        {
                            item.stream.CopyTo(fileStream);
                        }
                    }
                    
                },
                new ExecutionDataflowBlockOptions()
                {
                    BoundedCapacity = 1000,
                    MaxDegreeOfParallelism = 250,
                    CancellationToken = cancellationToken,
                });

            batchItemsBlock.LinkTo(fetchStream, new DataflowLinkOptions() { PropagateCompletion = true });
            fetchStream.LinkTo(downloadBlock, new DataflowLinkOptions() { PropagateCompletion = true });

            await batchItemsBlock.SendAllAndCompleteAsync(fileItems, downloadBlock, cancellationToken).ConfigureAwait(false);
        }

        private async Task<Stream> DownloadFileFromContainerAsync(Tuple<long, string> containerIdAndRoot, Guid scopeIdentifier, FileContainerHttpClient containerClient, FileContainerItem item, string targetPath, CancellationToken cancellationToken)
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
            var index = artifactName.Length;
            var itemPathWithoutDirectoryPrefix = (index != -1 && index < item.Path.Length) ? item.Path.Substring(index + 1) : string.Empty;
            var targetPath = Path.Combine(rootPath, itemPathWithoutDirectoryPrefix);
            return targetPath;
        }

        private IEnumerable<Func<string, bool>> GetMinimatchFuncs(IEnumerable<string> minimatchPatterns)
        {
            IEnumerable<Func<string, bool>> minimatcherFuncs;
            if (minimatchPatterns != null && minimatchPatterns.Count() != 0)
            {
                string minimatchPatternMsg = $"Minimatch patterns: [{ string.Join(",", minimatchPatterns) }]";
                minimatcherFuncs = minimatchPatterns
                    .Where(pattern => !string.IsNullOrEmpty(pattern)) // get rid of empty strings to avoid filtering out whole item list.
                    .Select(pattern => Minimatcher.CreateFilter(pattern, minimatchOptions));
            }
            else
            {
                minimatcherFuncs = null;
            }

            return minimatcherFuncs;
        }

        private List<FileContainerItem> GetFilteredItems(List<FileContainerItem> items, IEnumerable<Func<string, bool>> minimatchFuncs)
        {
            List<FileContainerItem> filteredItems = new List<FileContainerItem>();
            foreach(FileContainerItem item in items)
            {
                int index = item.Path.IndexOf('/');
                // trim the leading slash from the item paths
                if (minimatchFuncs.Any(match => match(item.Path.Substring(index+1))))
                {
                    filteredItems.Add(item);
                }
            }

            return filteredItems;
        }
    }
}
