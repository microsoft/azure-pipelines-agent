using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Agent.Sdk;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Content.Common;
using Microsoft.VisualStudio.Services.BlobStore.Common.Telemetry;
using Agent.Plugins.PipelineArtifact.Telemetry;
using Microsoft.VisualStudio.Services.BlobStore.WebApi;
using System.Diagnostics;
using Microsoft.VisualStudio.Services.WebApi;

namespace Agent.Plugins.PipelineArtifact
{
    internal class FileShareProvider: IArtifactProvider
    {
        private readonly AgentTaskPluginExecutionContext context;
        private readonly CallbackAppTraceSource tracer;
        private const int defaultParallelCount = 1;
        private readonly VssConnection connection;

        // Default stream buffer size set in the existing file share implementation https://github.com/microsoft/azure-pipelines-agent/blob/ffb3a9b3e2eb5a1f34a0f45d0f2b8639740d37d3/src/Agent.Worker/Release/Artifacts/FileShareArtifact.cs#L154
        private const int DefaultStreamBufferSize = 8192;

        public FileShareProvider(AgentTaskPluginExecutionContext context, VssConnection connection, CallbackAppTraceSource tracer)
        {
            this.context = context;
            this.tracer = tracer;
            this.connection = connection;
        }

        public async Task DownloadSingleArtifactAsync(PipelineArtifactDownloadParameters downloadParameters, BuildArtifact buildArtifact, CancellationToken cancellationToken) 
        {
            BlobStoreClientTelemetry clientTelemetry;
            DedupManifestArtifactClient dedupManifestClient = DedupManifestArtifactClientFactory.CreateDedupManifestClient(context, connection, cancellationToken, out clientTelemetry);
            using (clientTelemetry)
            {
                FileShareActionRecord downloadRecord = clientTelemetry.CreateRecord<FileShareActionRecord>((level, uri, type) =>
                    new FileShareActionRecord(level, uri, type, nameof(CopyFileShareAsync), context));

                await clientTelemetry.MeasureActionAsync(
                    record: downloadRecord,
                    actionAsync: async () =>
                    {
                        return await DownloadArtifactsAsync(downloadParameters, new List<BuildArtifact> { buildArtifact }, cancellationToken);
                    }
                );

                // Send results to CustomerIntelligence
                context.PublishTelemetry(area: PipelineArtifactConstants.AzurePipelinesAgent, feature: PipelineArtifactConstants.PipelineArtifact, record: downloadRecord);
            }
        }

        public async Task DownloadMultipleArtifactsAsync(PipelineArtifactDownloadParameters downloadParameters, IEnumerable<BuildArtifact> buildArtifacts, CancellationToken cancellationToken) 
        {
            BlobStoreClientTelemetry clientTelemetry;
            DedupManifestArtifactClient dedupManifestClient = DedupManifestArtifactClientFactory.CreateDedupManifestClient(context, connection, cancellationToken, out clientTelemetry);
            using (clientTelemetry)
            {
                FileShareActionRecord downloadRecord = clientTelemetry.CreateRecord<FileShareActionRecord>((level, uri, type) =>
                    new FileShareActionRecord(level, uri, type, nameof(CopyFileShareAsync), context));

                await clientTelemetry.MeasureActionAsync(
                    record: downloadRecord,
                    actionAsync: async () =>
                    {
                        await DownloadArtifactsAsync(downloadParameters, buildArtifacts, cancellationToken);
                    }
                );

                // Send results to CustomerIntelligence
                context.PublishTelemetry(area: PipelineArtifactConstants.AzurePipelinesAgent, feature: PipelineArtifactConstants.PipelineArtifact, record: downloadRecord);
            }
        }

        public async Task<FileShareDownloadResult> DownloadArtifactsAsync(PipelineArtifactDownloadParameters downloadParameters, IEnumerable<BuildArtifact> buildArtifacts, CancellationToken cancellationToken)
        {
            var fileShareDownloadResult = new FileShareDownloadResult(0, 0, 0);

            foreach (var buildArtifact in buildArtifacts)
            {
                var downloadRootPath = Path.Combine(buildArtifact.Resource.Data, buildArtifact.Name);
                var minimatchPatterns = downloadParameters.MinimatchFilters.Select(pattern => Path.Combine(buildArtifact.Resource.Data, pattern));
                await this.CopyFileShareAsync(downloadRootPath, Path.Combine(downloadParameters.TargetDirectory, buildArtifact.Name), minimatchPatterns, cancellationToken, fileShareDownloadResult);
            }
            
            return fileShareDownloadResult;
        }

        public async Task PublishSingleArtifactAsync(
            string sourcePath,
            string destPath,
            int parallelCount,
            CancellationToken cancellationToken) 
        {
            BlobStoreClientTelemetry clientTelemetry;
            DedupManifestArtifactClient dedupManifestClient = DedupManifestArtifactClientFactory.CreateDedupManifestClient(context, connection, cancellationToken, out clientTelemetry);
            using (clientTelemetry)
            {
                FileShareActionRecord downloadRecord = clientTelemetry.CreateRecord<FileShareActionRecord>((level, uri, type) =>
                    new FileShareActionRecord(level, uri, type, nameof(PublishArtifactAsync), context));

                await clientTelemetry.MeasureActionAsync(
                    record: downloadRecord,
                    actionAsync: async () =>
                    {
                        return await PublishArtifactAsync(sourcePath, destPath, parallelCount, cancellationToken);
                    }
                );

                // Send results to CustomerIntelligence
                context.PublishTelemetry(area: PipelineArtifactConstants.AzurePipelinesAgent, feature: PipelineArtifactConstants.PipelineArtifact, record: downloadRecord);
            }
        }

        public async Task<FileSharePublishResult> PublishArtifactAsync(string sourcePath, string destPath, int parallelCount, CancellationToken cancellationToken)
        {
            return await PublishArtifactUsingRobocopyAsync(this.context, sourcePath, destPath, parallelCount, cancellationToken);
        }

        private async Task CopyFileShareAsync(
            string downloadRootPath,
            string destPath,
            IEnumerable<string> minimatchPatterns,
            CancellationToken cancellationToken,
            FileShareDownloadResult result)
        {
            IEnumerable<Func<string, bool>> minimatcherFuncs = MinimatchHelper.GetMinimatchFuncs(minimatchPatterns, this.tracer);
            await DownloadFileShareArtifactAsync(downloadRootPath, destPath, defaultParallelCount, cancellationToken, minimatcherFuncs, result);
        }
        
        private async Task<FileSharePublishResult> PublishArtifactUsingRobocopyAsync(
            AgentTaskPluginExecutionContext executionContext,
            string dropLocation,
            string downloadFolderPath,
            int parallelCount,
            CancellationToken cancellationToken)
        {
            Stopwatch watch = Stopwatch.StartNew();
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

                watch.Stop();        
                return new FileSharePublishResult (watch.ElapsedMilliseconds, robocopyArguments, exitCode);
            }

        }

        private async Task DownloadFileShareArtifactAsync(
            string sourcePath,
            string destPath,
            int parallelCount,
            CancellationToken cancellationToken,
            IEnumerable<Func<string, bool>> minimatchFuncs = null,
            FileShareDownloadResult result = null)
        {
            var trimChars = new[] { '\\', '/' };

            sourcePath = sourcePath.TrimEnd(trimChars);

            IEnumerable<FileInfo> files =
                new DirectoryInfo(sourcePath).EnumerateFiles("*", SearchOption.AllDirectories);

            var parallelism = new ExecutionDataflowBlockOptions()
            {
                MaxDegreeOfParallelism = parallelCount,
                BoundedCapacity = 2 * parallelCount,
                CancellationToken = cancellationToken
            };

            result = result == null? new FileShareDownloadResult(0,0,0) : result;

            Stopwatch watch = Stopwatch.StartNew();

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
                        result.ContentSize += tempPath.Length;
                        result.FileCount += 1;
                    }
                },
                dataflowBlockOptions: parallelism);
                
                await actionBlock.SendAllAndCompleteAsync(files, actionBlock, cancellationToken);

            watch.Stop();
            result.TimeLapse += watch.ElapsedMilliseconds;
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