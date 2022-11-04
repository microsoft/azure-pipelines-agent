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
using Microsoft.VisualStudio.Services.Agent.Blob;
using BuildXL.Cache.ContentStore.Hashing;
using Microsoft.VisualStudio.Services.BlobStore.Common.Telemetry;
using Microsoft.VisualStudio.Services.BlobStore.WebApi;
using Microsoft.VisualStudio.Services.Content.Common;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;
using Microsoft.VisualStudio.Services.PipelineCache.WebApi;
using Microsoft.VisualStudio.Services.WebApi;
using JsonSerializer = Microsoft.VisualStudio.Services.Content.Common.JsonSerializer;
using Microsoft.VisualStudio.Services.BlobStore.Common;

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
            Fingerprint fingerprint,
            string path,
            CancellationToken cancellationToken,
            ContentFormat contentFormat)
        {
            VssConnection connection = context.VssConnection;
            var (dedupManifestClient, clientTelemetry) = await DedupManifestArtifactClientFactory.Instance
                .CreateDedupManifestClientAsync(
                    context.IsSystemDebugTrue(),
                    (str) => context.Output(str),
                    connection,
                    DedupManifestArtifactClientFactory.Instance.GetDedupStoreClientMaxParallelism(context),
                    WellKnownDomainIds.DefaultDomainId,
                    cancellationToken);

            PipelineCacheClient pipelineCacheClient = await this.CreateClientWithRetryAsync(clientTelemetry, context, connection, cancellationToken);

            using (clientTelemetry)
            {
                // Check if the key exists.
                PipelineCacheActionRecord cacheRecordGet = clientTelemetry.CreateRecord<PipelineCacheActionRecord>((level, uri, type) =>
                        new PipelineCacheActionRecord(level, uri, type, PipelineArtifactConstants.RestoreCache, context));
                PipelineCacheArtifact getResult = await pipelineCacheClient.GetPipelineCacheArtifactAsync(new [] {fingerprint}, cancellationToken, cacheRecordGet);
                // Send results to CustomerIntelligence
                context.PublishTelemetry(area: PipelineArtifactConstants.AzurePipelinesAgent, feature: PipelineArtifactConstants.PipelineCache, record: cacheRecordGet);
                //If cache exists, return.
                if (getResult != null)
                {
                    context.Output($"Cache with fingerprint `{getResult.Fingerprint}` already exists.");
                    return;
                }

                string uploadPath = await this.GetUploadPathAsync(contentFormat, context, path, cancellationToken);
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
                    Fingerprint = fingerprint,
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

                // Try to cache the artifact
                PipelineCacheActionRecord cacheRecord = clientTelemetry.CreateRecord<PipelineCacheActionRecord>(
                    (level, uri, type) => new PipelineCacheActionRecord(
                        level,
                        uri,
                        type,
                        PipelineArtifactConstants.SaveCache,
                        context));

                try
                {
                    _ = await pipelineCacheClient.CreatePipelineCacheArtifactAsync(
                        options,
                        cancellationToken,
                        cacheRecord);
                }
                catch
                {
                    context.Output($"Failed to cache item.");
                }

                // Send results to CustomerIntelligence
                context.PublishTelemetry(
                    area: PipelineArtifactConstants.AzurePipelinesAgent,
                    feature: PipelineArtifactConstants.PipelineCache,
                    record: uploadRecord);

                context.PublishTelemetry(
                    area: PipelineArtifactConstants.AzurePipelinesAgent,
                    feature: PipelineArtifactConstants.PipelineCache,
                    record: cacheRecord);

                context.Output("Saved item.");
            }
        }

        internal async Task DownloadAsync(
            AgentTaskPluginExecutionContext context,
            Fingerprint[] fingerprints,
            string path,
            string cacheHitVariable,
            CancellationToken cancellationToken)
        {
            VssConnection connection = context.VssConnection;
            var (dedupManifestClient, clientTelemetry) = await DedupManifestArtifactClientFactory.Instance
                .CreateDedupManifestClientAsync(
                    context.IsSystemDebugTrue(),
                    (str) => context.Output(str),
                    connection,
                    DedupManifestArtifactClientFactory.Instance.GetDedupStoreClientMaxParallelism(context),
                    WellKnownDomainIds.DefaultDomainId,
                    cancellationToken);

            PipelineCacheClient pipelineCacheClient = await this.CreateClientWithRetryAsync(clientTelemetry, context, connection, cancellationToken);

            using (clientTelemetry)
            {
                PipelineCacheActionRecord cacheRecord = clientTelemetry.CreateRecord<PipelineCacheActionRecord>((level, uri, type) =>
                        new PipelineCacheActionRecord(level, uri, type, PipelineArtifactConstants.RestoreCache, context));

                PipelineCacheArtifact result = null;
                try
                {
                    result = await pipelineCacheClient.GetPipelineCacheArtifactAsync(
                        fingerprints,
                        cancellationToken,
                        cacheRecord);
                }
                catch
                {
                    context.Output($"Failed to get cached item.");
                }

                // Send results to CustomerIntelligence
                context.PublishTelemetry(
                    area: PipelineArtifactConstants.AzurePipelinesAgent,
                    feature: PipelineArtifactConstants.PipelineCache,
                    record: cacheRecord);

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
                            await this.DownloadPipelineCacheAsync(context, dedupManifestClient, result.ManifestId, path, Enum.Parse<ContentFormat>(result.ContentFormat), cancellationToken);
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
                        foreach(var fingerprint in fingerprints)
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


        private Task<PipelineCacheClient> CreateClientWithRetryAsync(
            BlobStoreClientTelemetry blobStoreClientTelemetry,
            AgentTaskPluginExecutionContext context,
            VssConnection connection,
            CancellationToken cancellationToken)
        {
            // this uses location service so needs http retries.
            return AsyncHttpRetryHelper.InvokeAsync(
                async () => await this.CreateClientAsync(blobStoreClientTelemetry, context, connection),
                maxRetries: 3,
                tracer: tracer,
                canRetryDelegate: e => true, // this isn't great, but failing on upload stinks, so just try a couple of times
                cancellationToken: cancellationToken,
                continueOnCapturedContext: false);
        }

        private async Task<PipelineCacheClient> CreateClientAsync(
            BlobStoreClientTelemetry blobStoreClientTelemetry,
            AgentTaskPluginExecutionContext context,
            VssConnection connection)
        {

            var tracer = context.CreateArtifactsTracer();
            IClock clock = UtcClock.Instance;
            var pipelineCacheHttpClient = await connection.GetClientAsync<PipelineCacheHttpClient>();
            var pipelineCacheClient = new PipelineCacheClient(blobStoreClientTelemetry, pipelineCacheHttpClient, clock, tracer);

            return pipelineCacheClient;
        }

        private async Task<string> GetUploadPathAsync(ContentFormat contentFormat, AgentTaskPluginExecutionContext context, string path, CancellationToken cancellationToken)
        {
            string uploadPath = path;
            if(contentFormat == ContentFormat.SingleTar)
            {
                uploadPath = await TarUtils.ArchiveFilesToTarAsync(context, path, cancellationToken);
            }
            return uploadPath;
        }

        private async Task DownloadPipelineCacheAsync(
            AgentTaskPluginExecutionContext context,
            DedupManifestArtifactClient dedupManifestClient,
            DedupIdentifier manifestId,
            string targetDirectory,
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
                await TarUtils.DownloadAndExtractTarAsync (context, manifest, dedupManifestClient, targetDirectory, cancellationToken);
                try
                {
                    if(File.Exists(manifestPath))
                    {
                        File.Delete(manifestPath);
                    }
                }
                catch {}
            }
            else
            {
                DownloadDedupManifestArtifactOptions options = DownloadDedupManifestArtifactOptions.CreateWithManifestId(
                    manifestId,
                    targetDirectory,
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
    }
}
