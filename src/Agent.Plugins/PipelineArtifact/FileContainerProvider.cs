// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using Microsoft.VisualStudio.Services.Common;
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
    internal class FileContainerProvider : IArtifactProvider
    {
        private readonly FileContainerHttpClient containerClient;
        private readonly IAppTraceSource tracer;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA2000:Dispose objects before losing scope", MessageId = "connection2")]
        public FileContainerProvider(VssConnection connection, IAppTraceSource tracer)
        {
            BuildHttpClient buildHttpClient = connection.GetClient<BuildHttpClient>();
            var connection2 = new VssConnection(buildHttpClient.BaseAddress, connection.Credentials);
            containerClient = connection2.GetClient<FileContainerHttpClient>();
            this.tracer = tracer;

        }

        public async Task DownloadSingleArtifactAsync(PipelineArtifactDownloadParameters downloadParameters, BuildArtifact buildArtifact, CancellationToken cancellationToken, AgentTaskPluginExecutionContext context)
        {
            context.Warning(StringUtil.Loc("DownloadArtifactWarning", "Build Artifact"));
            await this.DownloadFileContainerAsync(downloadParameters.ProjectId, buildArtifact, downloadParameters.TargetDirectory, downloadParameters.MinimatchFilters, cancellationToken);
        }

        public async Task DownloadMultipleArtifactsAsync(PipelineArtifactDownloadParameters downloadParameters, IEnumerable<BuildArtifact> buildArtifacts, CancellationToken cancellationToken, AgentTaskPluginExecutionContext context)
        {
            context.Warning(StringUtil.Loc("DownloadArtifactWarning", "Build Artifact"));
            await this.DownloadFileContainersAsync(downloadParameters.ProjectId, buildArtifacts, downloadParameters.TargetDirectory, downloadParameters.MinimatchFilters, cancellationToken);
        }

        public async Task DownloadFileContainersAsync(Guid projectId, IEnumerable<BuildArtifact> buildArtifacts, string targetDirectory, IEnumerable<string> minimatchFilters, CancellationToken cancellationToken)
        {
            foreach (var buildArtifact in buildArtifacts)
            {
                var dirPath = Path.Combine(targetDirectory, buildArtifact.Name);
                await DownloadFileContainerAsync(projectId, buildArtifact, dirPath, minimatchFilters, cancellationToken, isSingleArtifactDownload: false);
            }
        }

        private (long, string) ParseContainerId(string resourceData)
        {
            // Example of resourceData: "#/7029766/artifacttool-alpine-x64-Debug"
            string[] segments = resourceData.Split('/');
            long containerId;

            if (segments.Length < 3)
            {
                throw new ArgumentException($"Resource data value '{resourceData}' is invalid.");
            }

            if (segments.Length >= 3 && segments[0] == "#" && long.TryParse(segments[1], out containerId))
            {
                var artifactName = String.Join('/', segments, 2, segments.Length - 2);
                return (
                        containerId,
                        artifactName
                        );
            }
            else
            {
                var message = $"Resource data value '{resourceData}' is invalid.";
                throw new ArgumentException(message, nameof(resourceData));
            }
        }

        private async Task DownloadFileContainerAsync(Guid projectId, BuildArtifact artifact, string rootPath, IEnumerable<string> minimatchPatterns, CancellationToken cancellationToken, bool isSingleArtifactDownload = true)
        {
            var containerIdAndRoot = ParseContainerId(artifact.Resource.Data);

            var items = await containerClient.QueryContainerItemsAsync(containerIdAndRoot.Item1, projectId, containerIdAndRoot.Item2);

            tracer.Info($"Start downloading FCS artifact- {artifact.Name}");
            IEnumerable<Func<string, bool>> minimatcherFuncs = MinimatchHelper.GetMinimatchFuncs(minimatchPatterns, tracer);

            if (minimatcherFuncs != null && minimatcherFuncs.Count() != 0)
            {
                items = this.GetFilteredItems(items, minimatcherFuncs);
            }

            if (!isSingleArtifactDownload && items.Any())
            {
                Directory.CreateDirectory(rootPath);
            }

            var folderItems = items.Where(i => i.ItemType == ContainerItemType.Folder);
            Parallel.ForEach(folderItems, (folder) =>
            {
                var targetPath = ResolveTargetPath(rootPath, folder, containerIdAndRoot.Item2);
                Directory.CreateDirectory(targetPath);
            });

            var fileItems = items.Where(i => i.ItemType == ContainerItemType.File);

            var downloadBlock = NonSwallowingActionBlock.Create<FileContainerItem>(
                async item =>
                {
                    var targetPath = ResolveTargetPath(rootPath, item, containerIdAndRoot.Item2);
                    var directory = Path.GetDirectoryName(targetPath);
                    Directory.CreateDirectory(directory);
                    tracer.Info($"Downloading: {targetPath}");

                    await AsyncHttpRetryHelper.InvokeVoidAsync(
                        async () =>
                        {
                            using (var sourceStream = await this.DownloadFileAsync(containerIdAndRoot, projectId, containerClient, item, cancellationToken))
                                // this is already wrapped with WrapWithCancellationEnforcement
                            using (var targetStream = FileStreamUtils.OpenFileStreamForAsync(targetPath, FileMode.Create, FileAccess.Write, FileShare.None)
                                                                     .WrapWithCancellationEnforcement(targetPath))
                            {
                                const int bufferSize = 64*1024; // smaller than LOH
                                var buffer = new byte[bufferSize];
                                int bytesRead;
                                do
                                {
                                    using (var bufferReadWriteTimout = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
                                    using (var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, bufferReadWriteTimout.Token))
                                    {
                                        bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length, linkedSource.Token).ConfigureAwait(false);
                                        await targetStream.WriteAsync(buffer, 0, bytesRead, linkedSource.Token).ConfigureAwait(false);
                                    }
                                }
                                while (bytesRead != 0);
                            }
                        },
                        maxRetries: 5,
                        cancellationToken: cancellationToken,
                        tracer: tracer,
                        continueOnCapturedContext: false,
                        canRetryDelegate: exception => exception is IOException,
                        context: targetPath
                        );
                },
                new ExecutionDataflowBlockOptions()
                {
                    BoundedCapacity = 5000,
                    MaxDegreeOfParallelism = 8,
                    CancellationToken = cancellationToken,
                    EnsureOrdered = false,
                });

            await downloadBlock.SendAllAndCompleteSingleBlockNetworkAsync(fileItems, cancellationToken);
        }

        private async Task<Stream> DownloadFileAsync(
            (long, string) containerIdAndRoot,
            Guid scopeIdentifier,
            FileContainerHttpClient containerClient,
            FileContainerItem item,
            CancellationToken cancellationToken)
        {
            Stream responseStream = await AsyncHttpRetryHelper.InvokeAsync(
                async () =>
                {
                    using (var getHeadersTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
                    using (var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, getHeadersTimeout.Token))
                    {
                        var task = containerClient.DownloadFileAsync(containerIdAndRoot.Item1, item.Path, linkedSource.Token, scopeIdentifier);
                        Stream stream = await task.EnforceCancellation(linkedSource.Token).ConfigureAwait(false);
                        return stream.WrapWithCancellationEnforcement(item.Path);
                    }
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
            //Example of item.Path&artifactName: item.Path = "drop3", "drop3/HelloWorld.exe"; artifactName = "drop3"
            string tempArtifactName;
            if (item.Path.Length == artifactName.Length)
            {
                tempArtifactName = artifactName;
            }
            else if (item.Path.Length > artifactName.Length)
            {
                tempArtifactName = artifactName + "/";
            }
            else
            {
                throw new ArgumentException($"Item path {item.Path} cannot be smaller than artifact {artifactName}");
            }

            var itemPathWithoutDirectoryPrefix = item.Path.Replace(tempArtifactName, String.Empty);
            var absolutePath = Path.Combine(rootPath, itemPathWithoutDirectoryPrefix);
            return absolutePath;
        }

        private List<FileContainerItem> GetFilteredItems(List<FileContainerItem> items, IEnumerable<Func<string, bool>> minimatchFuncs)
        {
            List<FileContainerItem> filteredItems = new List<FileContainerItem>();
            foreach (FileContainerItem item in items)
            {
                if (minimatchFuncs.Any(match => match(item.Path)))
                {
                    filteredItems.Add(item);
                }
            }
            var excludedItems = items.Except(filteredItems);
            foreach (FileContainerItem item in excludedItems)
            {
                tracer.Info($"Item excluded: {item.Path}");
            }
            return filteredItems;
        }
    }
}
