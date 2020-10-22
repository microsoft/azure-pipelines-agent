// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agent.Plugins.PipelineArtifact;
using Agent.Plugins.PipelineCache.Telemetry;
using Agent.Sdk;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using Microsoft.VisualStudio.Services.BlobStore.Common.Telemetry;
using Microsoft.VisualStudio.Services.BlobStore.WebApi;
using Microsoft.VisualStudio.Services.Content.Common;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;
using Microsoft.VisualStudio.Services.PipelineCache.WebApi;
using Microsoft.VisualStudio.Services.WebApi;
using JsonSerializer = Microsoft.VisualStudio.Services.Content.Common.JsonSerializer;

namespace Agent.Plugins.PipelineCache
{
    public class PipelineCacheServer
    {
        private readonly IAppTraceSource tracer;

        public PipelineCacheServer(AgentTaskPluginExecutionContext context)
        {
            this.tracer = context.CreateArtifactsTracer();
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1068: CancellationToken parameters must come last")]
        internal async Task UploadAsync(
            AgentTaskPluginExecutionContext context,
            Fingerprint keyFingerprint,
            string[] pathSegments,
            string workspaceRoot,
            CancellationToken cancellationToken,
            ContentFormat contentFormat)
        {
            VssConnection connection = context.VssConnection;
            BlobStoreClientTelemetry clientTelemetry;
            DedupManifestArtifactClient dedupManifestClient = DedupManifestArtifactClientFactory.Instance.CreateDedupManifestClient(context, connection, cancellationToken, out clientTelemetry);
            PipelineCacheClient pipelineCacheClient = this.CreateClient(clientTelemetry, context, connection);

            using (clientTelemetry)
            {
                // Check if the key exists.
                PipelineCacheActionRecord cacheRecordGet = clientTelemetry.CreateRecord<PipelineCacheActionRecord>((level, uri, type) =>
                        new PipelineCacheActionRecord(level, uri, type, PipelineArtifactConstants.RestoreCache, context));
                PipelineCacheArtifact getResult = await pipelineCacheClient.GetPipelineCacheArtifactAsync(new[] { keyFingerprint }, cancellationToken, cacheRecordGet);
                // Send results to CustomerIntelligence
                context.PublishTelemetry(area: PipelineArtifactConstants.AzurePipelinesAgent, feature: PipelineArtifactConstants.PipelineCache, record: cacheRecordGet);
                //If cache exists, return.
                if (getResult != null)
                {
                    context.Output($"Cache with fingerprint `{getResult.Fingerprint}` already exists.");
                    return;
                }

                context.Output("Resolving path:");
                Fingerprint pathFp = FingerprintCreator.EvaluateToFingerprint(context, workspaceRoot, pathSegments, FingerprintType.Path);
                context.Output($"Resolved to: {pathFp}");

                string uploadPath = await this.GetUploadPathAsync(contentFormat, context, pathFp, pathSegments, workspaceRoot, cancellationToken);

                //Upload the pipeline artifact.
                PipelineCacheActionRecord uploadRecord = clientTelemetry.CreateRecord<PipelineCacheActionRecord>((level, uri, type) =>
                    new PipelineCacheActionRecord(level, uri, type, nameof(dedupManifestClient.PublishAsync), context));

                PublishResult result = await clientTelemetry.MeasureActionAsync(
                    record: uploadRecord,
                    actionAsync: async () => 
                        await AsyncHttpRetryHelper.InvokeAsync(
                            async () => 
                            {
                                return await dedupManifestClient.PublishAsync(uploadPath, cancellationToken);
                            },
                            maxRetries: 3,
                            tracer: tracer,
                            canRetryDelegate: e => true, // this isn't great, but failing on upload stinks, so just try a couple of times
                            cancellationToken: cancellationToken,
                            continueOnCapturedContext: false)
                );

                CreatePipelineCacheArtifactContract options = new CreatePipelineCacheArtifactContract
                {
                    Fingerprint = keyFingerprint,
                    RootId = result.RootId,
                    ManifestId = result.ManifestId,
                    ProofNodes = result.ProofNodes.ToArray(),
                    ContentFormat = contentFormat.ToString(),
                };

                // delete archive file if it's tar.
                if (contentFormat == ContentFormat.SingleTar)
                {
                    try
                    {
                        if (File.Exists(uploadPath))
                        {
                            File.Delete(uploadPath);
                        }
                    }
                    catch { }
                }

                // Cache the artifact
                PipelineCacheActionRecord cacheRecord = clientTelemetry.CreateRecord<PipelineCacheActionRecord>((level, uri, type) =>
                    new PipelineCacheActionRecord(level, uri, type, PipelineArtifactConstants.SaveCache, context));
                CreateStatus status = await pipelineCacheClient.CreatePipelineCacheArtifactAsync(options, cancellationToken, cacheRecord);

                // Send results to CustomerIntelligence
                context.PublishTelemetry(area: PipelineArtifactConstants.AzurePipelinesAgent, feature: PipelineArtifactConstants.PipelineCache, record: uploadRecord);
                context.PublishTelemetry(area: PipelineArtifactConstants.AzurePipelinesAgent, feature: PipelineArtifactConstants.PipelineCache, record: cacheRecord);
                context.Output("Saved item.");
            }
        }

        internal async Task DownloadAsync(
            AgentTaskPluginExecutionContext context,
            Fingerprint[] fingerprints,
            string[] pathSegments,
            string cacheHitVariable,
            string workspaceRoot,
            CancellationToken cancellationToken)
        {
            VssConnection connection = context.VssConnection;
            BlobStoreClientTelemetry clientTelemetry;
            DedupManifestArtifactClient dedupManifestClient = DedupManifestArtifactClientFactory.Instance.CreateDedupManifestClient(context, connection, cancellationToken, out clientTelemetry);
            PipelineCacheClient pipelineCacheClient = this.CreateClient(clientTelemetry, context, connection);

            using (clientTelemetry)
            {
                PipelineCacheActionRecord cacheRecord = clientTelemetry.CreateRecord<PipelineCacheActionRecord>((level, uri, type) =>
                        new PipelineCacheActionRecord(level, uri, type, PipelineArtifactConstants.RestoreCache, context));
                PipelineCacheArtifact result = await pipelineCacheClient.GetPipelineCacheArtifactAsync(fingerprints, cancellationToken, cacheRecord);

                // Send results to CustomerIntelligence
                context.PublishTelemetry(area: PipelineArtifactConstants.AzurePipelinesAgent, feature: PipelineArtifactConstants.PipelineCache, record: cacheRecord);

                if (result != null)
                {
                    context.Output($"Entry found at fingerprint: `{result.Fingerprint.ToString()}`");
                    context.Verbose($"Manifest ID is: {result.ManifestId.ValueString}");
                    PipelineCacheActionRecord downloadRecord = clientTelemetry.CreateRecord<PipelineCacheActionRecord>((level, uri, type) =>
                        new PipelineCacheActionRecord(level, uri, type, nameof(DownloadAsync), context));
                    await clientTelemetry.MeasureActionAsync(
                        record: downloadRecord,
                        actionAsync: async () =>
                        {
                            await this.DownloadPipelineCacheAsync(context, dedupManifestClient, result.ManifestId, pathSegments, workspaceRoot, Enum.Parse<ContentFormat>(result.ContentFormat), cancellationToken);
                        });

                    // Send results to CustomerIntelligence
                    context.PublishTelemetry(area: PipelineArtifactConstants.AzurePipelinesAgent, feature: PipelineArtifactConstants.PipelineCache, record: downloadRecord);

                    context.Output("Cache restored.");
                }

                if (!string.IsNullOrEmpty(cacheHitVariable))
                {
                    if (result == null)
                    {
                        context.SetVariable(cacheHitVariable, "false");
                    }
                    else
                    {
                        context.Verbose($"Exact fingerprint: `{result.Fingerprint.ToString()}`");

                        bool foundExact = false;
                        foreach (var fingerprint in fingerprints)
                        {
                            context.Verbose($"This fingerprint: `{fingerprint.ToString()}`");

                            if (fingerprint == result.Fingerprint
                                || result.Fingerprint.Segments.Length == 1 && result.Fingerprint.Segments.Single() == fingerprint.SummarizeForV1())
                            {
                                foundExact = true;
                                break;
                            }
                        }

                        context.SetVariable(cacheHitVariable, foundExact ? "true" : "inexact");
                    }
                }
            }
        }

        private PipelineCacheClient CreateClient(
            BlobStoreClientTelemetry blobStoreClientTelemetry,
            AgentTaskPluginExecutionContext context,
            VssConnection connection)
        {
            var tracer = context.CreateArtifactsTracer();
            IClock clock = UtcClock.Instance;
            var pipelineCacheHttpClient = connection.GetClient<PipelineCacheHttpClient>();
            var pipelineCacheClient = new PipelineCacheClient(blobStoreClientTelemetry, pipelineCacheHttpClient, clock, tracer);

            return pipelineCacheClient;
        }

        private Task<string> GetUploadPathAsync(ContentFormat contentFormat, AgentTaskPluginExecutionContext context, Fingerprint pathFingerprint, string[] pathSegments, string workspaceRoot, CancellationToken cancellationToken)
        {
            if (contentFormat == ContentFormat.SingleTar)
            {
                var (tarWorkingDirectory, isWorkspaceContained) = GetTarWorkingDirectory(pathSegments, workspaceRoot);

                return TarUtils.ArchiveFilesToTarAsync(
                    context, 
                    pathFingerprint, 
                    tarWorkingDirectory, 
                    isWorkspaceContained, 
                    cancellationToken
                );
            }

            return Task.FromResult(pathFingerprint.Segments[0]);
        }

        private async Task DownloadPipelineCacheAsync(
            AgentTaskPluginExecutionContext context,
            DedupManifestArtifactClient dedupManifestClient,
            DedupIdentifier manifestId,
            string[] pathSegments,
            string workspaceRoot,
            ContentFormat contentFormat,
            CancellationToken cancellationToken)
        {
            if (contentFormat == ContentFormat.SingleTar)
            {
                string manifestPath = Path.Combine(Path.GetTempPath(), $"{nameof(DedupManifestArtifactClient)}.{Path.GetRandomFileName()}.manifest");

                await AsyncHttpRetryHelper.InvokeVoidAsync(
                    async () =>
                    {
                        await dedupManifestClient.DownloadFileToPathAsync(manifestId, manifestPath, proxyUri: null, cancellationToken: cancellationToken);
                    },
                    maxRetries: 3,
                    tracer: tracer,
                    canRetryDelegate: e => true,
                    context: nameof(DownloadPipelineCacheAsync),
                    cancellationToken: cancellationToken,
                    continueOnCapturedContext: false);

                Manifest manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath));
                var (tarWorkingDirectory, _) = GetTarWorkingDirectory(pathSegments, workspaceRoot);
                await TarUtils.DownloadAndExtractTarAsync(context, manifest, dedupManifestClient, tarWorkingDirectory, cancellationToken);
                try
                {
                    if (File.Exists(manifestPath))
                    {
                        File.Delete(manifestPath);
                    }
                }
                catch { }
            }
            else
            {
                DownloadDedupManifestArtifactOptions options = DownloadDedupManifestArtifactOptions.CreateWithManifestId(
                    manifestId,
                    pathSegments[0],
                    proxyUri: null,
                    minimatchPatterns: null);

                await AsyncHttpRetryHelper.InvokeVoidAsync(
                    async () =>
                    {
                        await dedupManifestClient.DownloadAsync(options, cancellationToken);
                    },
                    maxRetries: 3,
                    tracer: tracer,
                    canRetryDelegate: e => true,
                    context: nameof(DownloadPipelineCacheAsync),
                    cancellationToken: cancellationToken,
                    continueOnCapturedContext: false);
            }
        }

        private (string workingDirectory, bool isWorkspaceContained) GetTarWorkingDirectory(string[] segments, string workspaceRoot)
        {
            // If path segment is single directory outside of Pipeline.Workspace extract tarball directly to this path
            if (segments.Count() == 1) 
            {
                var workingDirectory = segments[0];
                if (FingerprintCreator.IsPathySegment(workingDirectory) && !workingDirectory.StartsWith(workspaceRoot))
                {
                    return (workingDirectory, false);
                }
            }

            // All other scenarios means that paths must within and relative to Pipeline.Workspace
            return (workspaceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), true);
        }
    }
}
