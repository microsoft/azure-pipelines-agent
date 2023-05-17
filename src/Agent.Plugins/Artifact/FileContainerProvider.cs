// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using Agent.Sdk.Knob;
using Agent.Sdk.Util;
using BuildXL.Cache.ContentStore.Hashing;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Blob;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using Microsoft.VisualStudio.Services.BlobStore.WebApi;
using Microsoft.VisualStudio.Services.Content.Common;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;
using Microsoft.VisualStudio.Services.FileContainer;
using Microsoft.VisualStudio.Services.FileContainer.Client;
using Microsoft.VisualStudio.Services.WebApi;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Minimatch;

namespace Agent.Plugins
{
    internal class FileContainerProvider : IArtifactProvider
    {
        private readonly VssConnection connection;
        private readonly FileContainerHttpClient containerClient;
        private readonly IAppTraceSource tracer;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA2000:Dispose objects before losing scope", MessageId = "connection2")]
        public FileContainerProvider(VssConnection connection, IAppTraceSource tracer)
        {
            if (connection != null)
            {
                BuildHttpClient buildHttpClient = connection.GetClient<BuildHttpClient>();
                VssConnection connection2 = new VssConnection(buildHttpClient.BaseAddress, connection.Credentials);
                containerClient = connection2.GetClient<FileContainerHttpClient>();
            }

            this.tracer = tracer;
            this.connection = connection;
        }

        public async Task DownloadSingleArtifactAsync(ArtifactDownloadParameters downloadParameters, BuildArtifact buildArtifact, CancellationToken cancellationToken, AgentTaskPluginExecutionContext context)
        {
            IEnumerable<FileContainerItem> items = await GetArtifactItems(downloadParameters, buildArtifact);
            await this.DownloadFileContainerAsync(items, downloadParameters, buildArtifact, downloadParameters.TargetDirectory, context, cancellationToken);

            IEnumerable<string> fileArtifactPaths = items
                .Where((item) => item.ItemType == ContainerItemType.File)
                .Select((fileItem) => Path.Combine(downloadParameters.TargetDirectory, fileItem.Path));

            if (downloadParameters.ExtractTars)
            {
                ExtractTarsIfPresent(context, fileArtifactPaths, downloadParameters.TargetDirectory, downloadParameters.ExtractedTarsTempPath);
            }
        }

        public async Task DownloadMultipleArtifactsAsync(ArtifactDownloadParameters downloadParameters, IEnumerable<BuildArtifact> buildArtifacts, CancellationToken cancellationToken, AgentTaskPluginExecutionContext context)
        {
            var allFileArtifactPaths = new List<string>();

            foreach (var buildArtifact in buildArtifacts)
            {
                var dirPath = downloadParameters.AppendArtifactNameToTargetPath
                    ? Path.Combine(downloadParameters.TargetDirectory, buildArtifact.Name)
                    : downloadParameters.TargetDirectory;

                IEnumerable<FileContainerItem> items = await GetArtifactItems(downloadParameters, buildArtifact);
                IEnumerable<string> fileArtifactPaths = items
                    .Where((item) => item.ItemType == ContainerItemType.File)
                    .Select((fileItem) => Path.Combine(dirPath, fileItem.Path));
                allFileArtifactPaths.AddRange(fileArtifactPaths);

                await DownloadFileContainerAsync(items, downloadParameters, buildArtifact, dirPath, context, cancellationToken, isSingleArtifactDownload: false);
            }

            if (downloadParameters.ExtractTars)
            {
                ExtractTarsIfPresent(context, allFileArtifactPaths, downloadParameters.TargetDirectory, downloadParameters.ExtractedTarsTempPath);
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

        private async Task DownloadFileContainerAsync(IEnumerable<FileContainerItem> items, ArtifactDownloadParameters downloadParameters, BuildArtifact artifact, string rootPath, AgentTaskPluginExecutionContext context, CancellationToken cancellationToken, bool isSingleArtifactDownload = true)
        {
            var containerIdAndRoot = ParseContainerId(artifact.Resource.Data);
            var projectId = downloadParameters.ProjectId;

            tracer.Info($"Start downloading FCS artifact- {artifact.Name}");

            if (!isSingleArtifactDownload && items.Any())
            {
                Directory.CreateDirectory(rootPath);
            }

            var folderItems = items.Where(i => i.ItemType == ContainerItemType.Folder);
            Parallel.ForEach(folderItems, (folder) =>
            {
                var targetPath = ResolveTargetPath(rootPath, folder, containerIdAndRoot.Item2, downloadParameters.IncludeArtifactNameInPath);
                Directory.CreateDirectory(targetPath);
            });

            var fileItems = items.Where(i => i.ItemType == ContainerItemType.File);

            // Only initialize these clients if we know we need to download from Blobstore
            // If a client cannot connect to Blobstore, we shouldn't stop them from downloading from FCS
            var downloadFromBlob = !AgentKnobs.DisableBuildArtifactsToBlob.GetValue(context).AsBoolean();
            DedupStoreClient dedupClient = null;
            BlobStoreClientTelemetryTfs clientTelemetry = null;
            if (downloadFromBlob && fileItems.Any(x => x.BlobMetadata != null))
            {
                try
                {
                    (dedupClient, clientTelemetry) = await DedupManifestArtifactClientFactory.Instance.CreateDedupClientAsync(
                        false, (str) => this.tracer.Info(str), this.connection, DedupManifestArtifactClientFactory.Instance.GetDedupStoreClientMaxParallelism(context), cancellationToken);
                }
                catch (SocketException e)
                {
                    ExceptionsUtil.HandleSocketException(e, connection.Uri.ToString(), context.Warning);
                }
                catch
                {
                    var blobStoreHost = dedupClient.Client.BaseAddress.Host;
                    var allowListLink = BlobStoreWarningInfoProvider.GetAllowListLinkForCurrentPlatform();
                    var warningMessage = StringUtil.Loc("BlobStoreDownloadWarning", blobStoreHost, allowListLink);

                    // Fall back to streaming through TFS if we cannot reach blobstore
                    downloadFromBlob = false;
                    tracer.Warn(warningMessage);
                }
            }

            var downloadBlock = NonSwallowingActionBlock.Create<FileContainerItem>(
                async item =>
                {
                    var targetPath = ResolveTargetPath(rootPath, item, containerIdAndRoot.Item2, downloadParameters.IncludeArtifactNameInPath);
                    var directory = Path.GetDirectoryName(targetPath);
                    Directory.CreateDirectory(directory);
                    await AsyncHttpRetryHelper.InvokeVoidAsync(
                        async () =>
                        {
                            tracer.Info($"Downloading: {targetPath}");
                            if (item.BlobMetadata != null && downloadFromBlob)
                            {
                                await this.DownloadFileFromBlobAsync(context, containerIdAndRoot, targetPath, projectId, item, dedupClient, clientTelemetry, cancellationToken);
                            }
                            else
                            {
                                using (var sourceStream = await this.DownloadFileAsync(containerIdAndRoot, projectId, containerClient, item, cancellationToken))
                                using (var targetStream = new FileStream(targetPath, FileMode.Create))
                                {
                                    await sourceStream.CopyToAsync(targetStream);
                                }
                            }
                        },
                        maxRetries: downloadParameters.RetryDownloadCount,
                        cancellationToken: cancellationToken,
                        tracer: tracer,
                        continueOnCapturedContext: false,
                        canRetryDelegate: exception => exception is IOException,
                        context: null
                        );
                },
                new ExecutionDataflowBlockOptions()
                {
                    BoundedCapacity = 5000,
                    MaxDegreeOfParallelism = downloadParameters.ParallelizationLimit,
                    CancellationToken = cancellationToken,
                });

            await downloadBlock.SendAllAndCompleteSingleBlockNetworkAsync(fileItems, cancellationToken);

            // Send results to CustomerIntelligence
            if (clientTelemetry != null)
            {
                var planId = new Guid(context.Variables.GetValueOrDefault(WellKnownDistributedTaskVariables.PlanId)?.Value ?? Guid.Empty.ToString());
                var jobId = new Guid(context.Variables.GetValueOrDefault(WellKnownDistributedTaskVariables.JobId)?.Value ?? Guid.Empty.ToString());
                context.PublishTelemetry(area: PipelineArtifactConstants.AzurePipelinesAgent, feature: PipelineArtifactConstants.BuildArtifactDownload,
                    properties: clientTelemetry.GetArtifactDownloadTelemetry(planId, jobId));
            }

            // check files (will throw an exception if a file is corrupt)
            if (downloadParameters.CheckDownloadedFiles)
            {
                CheckDownloads(items, rootPath, containerIdAndRoot.Item2, downloadParameters.IncludeArtifactNameInPath);
            }
        }

        // Returns list of filtered artifact items. Uses minimatch filters specified in downloadParameters.
        private async Task<IEnumerable<FileContainerItem>> GetArtifactItems(ArtifactDownloadParameters downloadParameters, BuildArtifact buildArtifact)
        {
            (long, string) containerIdAndRoot = ParseContainerId(buildArtifact.Resource.Data);
            Guid projectId = downloadParameters.ProjectId;
            string[] minimatchPatterns = downloadParameters.MinimatchFilters;

            List<FileContainerItem> items = await containerClient.QueryContainerItemsAsync(
                containerIdAndRoot.Item1,
                projectId,
                isShallow: false,
                includeBlobMetadata: true,
                $"{containerIdAndRoot.Item2}{Path.DirectorySeparatorChar}"
            );

            Options customMinimatchOptions;
            if (downloadParameters.CustomMinimatchOptions != null)
            {
                customMinimatchOptions = downloadParameters.CustomMinimatchOptions;
            }
            else
            {
                customMinimatchOptions = new Options()
                {
                    Dot = true,
                    NoBrace = true,
                    AllowWindowsPaths = PlatformUtil.RunningOnWindows
                };
            }

            // Getting list of item paths. It is useful to handle list of paths instead of items.
            // Also it allows to use the same methods for FileContainerProvider and FileShareProvider.
            List<string> paths = new List<string>();
            foreach (FileContainerItem item in items)
            {
                paths.Add(item.Path);
            }

            ArtifactItemFilters filters = new ArtifactItemFilters(connection, tracer);
            Hashtable map = filters.GetMapToFilterItems(paths, minimatchPatterns, customMinimatchOptions);

            // Returns filtered list of artifact items. Uses minimatch filters specified in downloadParameters.
            List<FileContainerItem> resultItems = filters.ApplyPatternsMapToContainerItems(items, map);

            tracer.Info($"{resultItems.Count} final results");

            IEnumerable<FileContainerItem> excludedItems = items.Except(resultItems);
            foreach (FileContainerItem item in excludedItems)
            {
                tracer.Info($"Item excluded: {item.Path}");
            }

            return resultItems;
        }

        private void CheckDownloads(IEnumerable<FileContainerItem> items, string rootPath, string artifactName, bool includeArtifactName)
        {
            tracer.Info(StringUtil.Loc("BeginArtifactItemsIntegrityCheck"));
            var corruptedItems = new List<FileContainerItem>();
            foreach (var item in items.Where(x => x.ItemType == ContainerItemType.File))
            {
                var targetPath = ResolveTargetPath(rootPath, item, artifactName, includeArtifactName);
                var fileInfo = new FileInfo(targetPath);
                if (fileInfo.Length != item.FileLength)
                {
                    corruptedItems.Add(item);
                }
            }

            if (corruptedItems.Count > 0)
            {
                tracer.Warn(StringUtil.Loc("CorruptedArtifactItemsList"));
                corruptedItems.ForEach(item => tracer.Warn(item.ItemLocation));

                throw new Exception(StringUtil.Loc("IntegrityCheckNotPassed"));
            }
            tracer.Info(StringUtil.Loc("IntegrityCheckPassed"));
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
                    Stream internalResponseStream = await containerClient.DownloadFileAsync(containerIdAndRoot.Item1, item.Path, cancellationToken, scopeIdentifier);
                    return internalResponseStream;
                },
                maxRetries: 5,
                cancellationToken: cancellationToken,
                tracer: this.tracer,
                continueOnCapturedContext: false
                );

            return responseStream;
        }

        private async Task DownloadFileFromBlobAsync(
            AgentTaskPluginExecutionContext context,
            (long, string) containerIdAndRoot,
            string destinationPath,
            Guid scopeIdentifier,
            FileContainerItem item,
            DedupStoreClient dedupClient,
            BlobStoreClientTelemetryTfs clientTelemetry,
            CancellationToken cancellationToken)
        {
            var dedupIdentifier = DedupIdentifier.Deserialize(item.BlobMetadata.ArtifactHash);

            var downloadRecord = clientTelemetry.CreateRecord<BuildArtifactActionRecord>((level, uri, type) =>
                new BuildArtifactActionRecord(level, uri, type, nameof(DownloadFileContainerAsync), context));
            await clientTelemetry.MeasureActionAsync(
                record: downloadRecord,
                actionAsync: async () =>
                {
                    return await AsyncHttpRetryHelper.InvokeAsync(
                        async () =>
                        {
                            if (item.BlobMetadata.CompressionType == BlobCompressionType.GZip)
                            {
                                using (var targetFileStream = new FileStream(destinationPath, FileMode.Create))
                                using (var uncompressStream = new GZipStream(targetFileStream, CompressionMode.Decompress))
                                {
                                    await dedupClient.DownloadToStreamAsync(dedupIdentifier, uncompressStream, null, EdgeCache.Allowed, (size) => { }, (size) => { }, cancellationToken);
                                }
                            }
                            else
                            {
                                await dedupClient.DownloadToFileAsync(dedupIdentifier, destinationPath, null, null, EdgeCache.Allowed, cancellationToken);
                            }
                            return dedupClient.DownloadStatistics;
                        },
                        maxRetries: 3,
                        tracer: tracer,
                        canRetryDelegate: e => true,
                        context: nameof(DownloadFileFromBlobAsync),
                        cancellationToken: cancellationToken,
                        continueOnCapturedContext: false);
                });
        }

        private string ResolveTargetPath(string rootPath, FileContainerItem item, string artifactName, bool includeArtifactName)
        {
            if (includeArtifactName)
            {
                return Path.Combine(rootPath, item.Path);
            }
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

        // Checks all specified artifact paths, searches for files ending with '.tar'.
        // If any files were found, extracts them to extractedTarsTempPath and moves to rootPath/extracted_tars.
        private void ExtractTarsIfPresent(AgentTaskPluginExecutionContext context, IEnumerable<string> fileArtifactPaths, string rootPath, string extractedTarsTempPath)
        {
            tracer.Info(StringUtil.Loc("TarSearchStart"));

            int tarsFoundCount = 0;

            foreach (var fileArtifactPath in fileArtifactPaths)
            {
                if (fileArtifactPath.EndsWith(".tar"))
                {
                    tarsFoundCount += 1;

                    // fileArtifactPath is a combination of rootPath and the relative artifact path
                    string relativeFileArtifactPath = fileArtifactPath.Substring(rootPath.Length);
                    string relativeFileArtifactDirPath = Path.GetDirectoryName(relativeFileArtifactPath).TrimStart('/');
                    string extractedFilesDir = Path.Combine(extractedTarsTempPath, relativeFileArtifactDirPath);

                    ExtractTar(fileArtifactPath, extractedFilesDir);

                    File.Delete(fileArtifactPath);
                }
            }

            if (tarsFoundCount == 0)
            {
                context.Warning(StringUtil.Loc("TarsNotFound"));
            }
            else
            {
                tracer.Info(StringUtil.Loc("TarsFound", tarsFoundCount));

                string targetDirectory = Path.Combine(rootPath, "extracted_tars");
                Directory.CreateDirectory(targetDirectory);
                MoveDirectory(extractedTarsTempPath, targetDirectory);
            }
        }

        // Extracts tar archive at tarArchivePath to extractedFilesDir.
        // Uses 'tar' utility like this: tar xf `tarArchivePath` --directory `extractedFilesDir`.
        // Throws if any errors are encountered.
        private void ExtractTar(string tarArchivePath, string extractedFilesDir)
        {
            tracer.Info(StringUtil.Loc("TarExtraction", tarArchivePath));

            Directory.CreateDirectory(extractedFilesDir);
            var extractionProcessInfo = new ProcessStartInfo("tar")
            {
                Arguments = $"xf {tarArchivePath} --directory {extractedFilesDir}",
                UseShellExecute = false,
                RedirectStandardError = true
            };
            Process extractionProcess = Process.Start(extractionProcessInfo);
            extractionProcess.WaitForExit();

            var extractionStderr = extractionProcess.StandardError.ReadToEnd();
            if (extractionStderr.Length != 0 || extractionProcess.ExitCode != 0)
            {
                throw new Exception(StringUtil.Loc("TarExtractionError", tarArchivePath, extractionStderr));
            }
        }

        // Recursively moves sourcePath directory to targetPath
        private void MoveDirectory(string sourcePath, string targetPath)
        {
            var sourceDirectoryInfo = new DirectoryInfo(sourcePath);
            foreach (FileInfo file in sourceDirectoryInfo.GetFiles("*", SearchOption.TopDirectoryOnly))
            {
                file.MoveTo(Path.Combine(targetPath, file.Name), true);
            }
            foreach (DirectoryInfo subdirectory in sourceDirectoryInfo.GetDirectories("*", SearchOption.TopDirectoryOnly))
            {
                string subdirectoryDestinationPath = Path.Combine(targetPath, subdirectory.Name);
                var subdirectoryDestination = new DirectoryInfo(subdirectoryDestinationPath);

                if (subdirectoryDestination.Exists)
                {
                    MoveDirectory(
                        Path.Combine(sourcePath, subdirectory.Name),
                        Path.Combine(targetPath, subdirectory.Name)
                    );
                }
                else
                {
                    subdirectory.MoveTo(Path.Combine(targetPath, subdirectory.Name));
                }
            }
        }
    }
}
