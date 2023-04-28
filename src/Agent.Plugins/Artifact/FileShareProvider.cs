// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Agent.Plugins.PipelineArtifact.Telemetry;
using Agent.Sdk;
using BuildXL.Cache.ContentStore.Hashing;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.Agent.Blob;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using Microsoft.VisualStudio.Services.BlobStore.Common.Telemetry;
using Microsoft.VisualStudio.Services.BlobStore.WebApi;
using Microsoft.VisualStudio.Services.Content.Common;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;
using Microsoft.VisualStudio.Services.WebApi;
using Minimatch;

namespace Agent.Plugins
{
    internal class FileShareProvider : IArtifactProvider
    {
        private readonly AgentTaskPluginExecutionContext context;
        private readonly IAppTraceSource tracer;
        private const int defaultParallelCount = 1;
        private readonly VssConnection connection;
        private readonly IDedupManifestArtifactClientFactory factory;

        // Default stream buffer size set in the existing file share implementation https://github.com/microsoft/azure-pipelines-agent/blob/ffb3a9b3e2eb5a1f34a0f45d0f2b8639740d37d3/src/Agent.Worker/Release/Artifacts/FileShareArtifact.cs#L154
        private const int DefaultStreamBufferSize = 8192;

        public FileShareProvider(AgentTaskPluginExecutionContext context, VssConnection connection, IAppTraceSource tracer, IDedupManifestArtifactClientFactory factory)
        {
            this.factory = factory;
            this.context = context;
            this.tracer = tracer;
            this.connection = connection;
        }

        public async Task DownloadSingleArtifactAsync(
            ArtifactDownloadParameters downloadParameters,
            BuildArtifact buildArtifact,
            CancellationToken cancellationToken,
            AgentTaskPluginExecutionContext context)
        {
            await DownloadMultipleArtifactsAsync(downloadParameters, new List<BuildArtifact> { buildArtifact }, cancellationToken, context);
        }

        public async Task DownloadMultipleArtifactsAsync(
            ArtifactDownloadParameters downloadParameters,
            IEnumerable<BuildArtifact> buildArtifacts,
            CancellationToken cancellationToken,
            AgentTaskPluginExecutionContext context)
        {
            context.Warning(StringUtil.Loc("DownloadArtifactWarning", "UNC"));
            var (dedupManifestClient, clientTelemetry) = await this.factory.CreateDedupManifestClientAsync(
                context.IsSystemDebugTrue(),
                (str) => context.Output(str),
                connection,
                this.factory.GetDedupStoreClientMaxParallelism(context),
                WellKnownDomainIds.DefaultDomainId,
                Microsoft.VisualStudio.Services.BlobStore.WebApi.Contracts.Client.FileShare,
                cancellationToken);

            using (clientTelemetry)
            {
                FileShareActionRecord downloadRecord = clientTelemetry.CreateRecord<FileShareActionRecord>((level, uri, type) =>
                    new FileShareActionRecord(level, uri, type, nameof(DownloadArtifactsAsync), context));

                await clientTelemetry.MeasureActionAsync(
                    record: downloadRecord,
                    actionAsync: async () =>
                    {
                        return await DownloadArtifactsAsync(downloadParameters, buildArtifacts, cancellationToken);
                    }
                );

                // Send results to CustomerIntelligence
                context.PublishTelemetry(area: PipelineArtifactConstants.AzurePipelinesAgent, feature: PipelineArtifactConstants.PipelineArtifact, record: downloadRecord);
            }
        }

        private async Task<FileShareDownloadResult> DownloadArtifactsAsync(ArtifactDownloadParameters downloadParameters, IEnumerable<BuildArtifact> buildArtifacts, CancellationToken cancellationToken)
        {
            var records = new List<ArtifactRecord>();
            long totalContentSize = 0;
            int totalFileCount = 0;
            foreach (var buildArtifact in buildArtifacts)
            {
                var downloadRootPath = Path.Combine(buildArtifact.Resource.Data, buildArtifact.Name);
                var minimatchPatterns = downloadParameters.MinimatchFilters.Select(pattern => Path.Combine(buildArtifact.Resource.Data, pattern));
                var customMinimatchOptions = downloadParameters.CustomMinimatchOptions;
                var record = await this.DownloadFileShareArtifactAsync(downloadRootPath, Path.Combine(downloadParameters.TargetDirectory, buildArtifact.Name), defaultParallelCount, downloadParameters, cancellationToken, minimatchPatterns);
                totalContentSize += record.ContentSize;
                totalFileCount += record.FileCount;
                records.Add(record);
            }

            return new FileShareDownloadResult(records, totalFileCount, totalContentSize);
        }

        public async Task PublishArtifactAsync(
            string sourcePath,
            string destPath,
            int parallelCount,
            CancellationToken cancellationToken)
        {
            var (dedupManifestClient, clientTelemetry) = await this.factory.CreateDedupManifestClientAsync(
                context.IsSystemDebugTrue(),
                (str) => context.Output(str),
                connection,
                this.factory.GetDedupStoreClientMaxParallelism(context),
                WellKnownDomainIds.DefaultDomainId,
                Microsoft.VisualStudio.Services.BlobStore.WebApi.Contracts.Client.FileShare,
                cancellationToken);

            using (clientTelemetry)
            {
                FileShareActionRecord publishRecord = clientTelemetry.CreateRecord<FileShareActionRecord>((level, uri, type) =>
                    new FileShareActionRecord(level, uri, type, nameof(PublishArtifactAsync), context));

                await clientTelemetry.MeasureActionAsync(
                    record: publishRecord,
                    actionAsync: async () =>
                    {
                        return await PublishArtifactUsingRobocopyAsync(this.context, sourcePath, destPath, parallelCount, cancellationToken);
                    }
                );

                // Send results to CustomerIntelligence
                context.PublishTelemetry(area: PipelineArtifactConstants.AzurePipelinesAgent, feature: PipelineArtifactConstants.PipelineArtifact, record: publishRecord);
            }
        }

        private async Task<FileSharePublishResult> PublishArtifactUsingRobocopyAsync(
            AgentTaskPluginExecutionContext executionContext,
            string dropLocation,
            string downloadFolderPath,
            int parallelCount,
            CancellationToken cancellationToken)
        {
            executionContext.Output(StringUtil.Loc("PublishingArtifactUsingRobocopy"));
            using (var processInvoker = new ProcessInvoker(this.context))
            {
                // Save STDOUT from worker, worker will use STDOUT report unhandle exception.
                processInvoker.OutputDataReceived += delegate (object sender, ProcessDataReceivedEventArgs stdout)
                {
                    if (!string.IsNullOrEmpty(stdout.Data))
                    {
                        executionContext.Output(stdout.Data);
                    }
                };

                // Save STDERR from worker, worker will use STDERR on crash.
                processInvoker.ErrorDataReceived += delegate (object sender, ProcessDataReceivedEventArgs stderr)
                {
                    if (!string.IsNullOrEmpty(stderr.Data))
                    {
                        executionContext.Error(stderr.Data);
                    }
                };

                var trimChars = new[] { '\\', '/' };

                dropLocation = Path.Combine(dropLocation.TrimEnd(trimChars));
                downloadFolderPath = downloadFolderPath.TrimEnd(trimChars);

                string robocopyArguments = "\"" + dropLocation + "\" \"" + downloadFolderPath + "\" * /E /COPY:DA /NP /R:3";

                robocopyArguments += " /MT:" + parallelCount;

                int exitCode = await processInvoker.ExecuteAsync(
                        workingDirectory: "",
                        fileName: "robocopy",
                        arguments: robocopyArguments,
                        environment: null,
                        requireExitCodeZero: false,
                        outputEncoding: null,
                        killProcessOnCancel: true,
                        cancellationToken: cancellationToken);

                executionContext.Output(StringUtil.Loc("RobocopyBasedPublishArtifactTaskExitCode", exitCode));
                // Exit code returned from robocopy. For more info https://blogs.technet.microsoft.com/deploymentguys/2008/06/16/robocopy-exit-codes/
                if (exitCode >= 8)
                {
                    throw new Exception(StringUtil.Loc("RobocopyBasedPublishArtifactTaskFailed", exitCode));
                }

                return new FileSharePublishResult(exitCode);
            }
        }

        private async Task<ArtifactRecord> DownloadFileShareArtifactAsync(
            string sourcePath,
            string destPath,
            int parallelCount,
            ArtifactDownloadParameters downloadParameters,
            CancellationToken cancellationToken,
            IEnumerable<string> minimatchPatterns = null)
        {
            Stopwatch watch = Stopwatch.StartNew();

            IEnumerable<Func<string, bool>> minimatchFuncs = MinimatchHelper.GetMinimatchFuncs(minimatchPatterns, this.tracer);

            var trimChars = new[] { '\\', '/' };

            sourcePath = sourcePath.TrimEnd(trimChars);

            var artifactName = new DirectoryInfo(sourcePath).Name;

            List<FileInfo> files =
                new DirectoryInfo(sourcePath).EnumerateFiles("*", SearchOption.AllDirectories).ToList<FileInfo>();

            ArtifactItemFilters filters = new ArtifactItemFilters(connection, tracer);

            // Getting list of file paths. It is useful to handle list of paths instead of files.
            // Also it allows to use the same methods for FileContainerProvider and FileShareProvider.
            List<string> paths = new List<string>();
            foreach (FileInfo file in files)
            {
                string pathInArtifact = filters.RemoveSourceDirFromPath(file, sourcePath);
                paths.Add(Path.Combine(artifactName, pathInArtifact));
            }

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

            Hashtable map = filters.GetMapToFilterItems(paths, downloadParameters.MinimatchFilters, customMinimatchOptions);

            // Returns filtered list of artifact items. Uses minimatch filters specified in downloadParameters.
            IEnumerable<FileInfo> filteredFiles = filters.ApplyPatternsMapToFileShareItems(files, map, sourcePath);

            tracer.Info($"{filteredFiles.ToList().Count} final results");

            IEnumerable<FileInfo> excludedItems = files.Except(filteredFiles);
            foreach (FileInfo item in excludedItems)
            {
                tracer.Info($"File excluded: {item.FullName}");
            }

            var parallelism = new ExecutionDataflowBlockOptions()
            {
                MaxDegreeOfParallelism = parallelCount,
                BoundedCapacity = 2 * parallelCount,
                CancellationToken = cancellationToken
            };

            var contentSize = 0;
            var fileCount = 0;

            var actionBlock = NonSwallowingActionBlock.Create<FileInfo>(
               action: async file =>
                {
                    if (minimatchFuncs == null || minimatchFuncs.Any(match => match(file.FullName)))
                    {
                        string tempPath = Path.Combine(destPath, Path.GetRelativePath(sourcePath, file.FullName));
                        context.Output(StringUtil.Loc("CopyFileToDestination", file, tempPath));
                        FileInfo tempFile = new System.IO.FileInfo(tempPath);
                        using (StreamReader fileReader = GetFileReader(file.FullName))
                        {
                            await WriteStreamToFile(
                                fileReader.BaseStream,
                                tempFile.FullName,
                                DefaultStreamBufferSize,
                                cancellationToken);
                        }
                        Interlocked.Add(ref contentSize, tempPath.Length);
                        Interlocked.Increment(ref fileCount);
                    }
                },
                dataflowBlockOptions: parallelism);

            await actionBlock.SendAllAndCompleteAsync(filteredFiles, actionBlock, cancellationToken);

            watch.Stop();

            return new ArtifactRecord(artifactName,
                                      fileCount,
                                      contentSize,
                                      watch.ElapsedMilliseconds);
        }

        private async Task WriteStreamToFile(Stream stream, string filePath, int bufferSize, CancellationToken cancellationToken)
        {
            ArgUtil.NotNull(stream, nameof(stream));
            ArgUtil.NotNullOrEmpty(filePath, nameof(filePath));

            EnsureDirectoryExists(Path.GetDirectoryName(filePath));
            using (var targetStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, useAsync: true))
            {
                await stream.CopyToAsync(targetStream, bufferSize, cancellationToken);
            }
        }

        private StreamReader GetFileReader(string filePath)
        {
            string path = Path.Combine(ValidatePath(filePath));
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(StringUtil.Loc("FileNotFound", path));
            }

            return new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, DefaultStreamBufferSize, true));
        }

        private void EnsureDirectoryExists(string directoryPath)
        {
            string path = ValidatePath(directoryPath);
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        private string ValidatePath(string path)
        {
            ArgUtil.NotNullOrEmpty(path, nameof(path));
            return Path.GetFullPath(path);
        }
    }
}