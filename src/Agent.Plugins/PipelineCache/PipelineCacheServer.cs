using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System;
using Agent.Plugins.PipelineArtifact;
using Agent.Plugins.PipelineArtifact.Telemetry;
using Agent.Plugins.PipelineCache.Telemetry;
using Agent.Sdk;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using Microsoft.VisualStudio.Services.BlobStore.Common.Telemetry;
using Microsoft.VisualStudio.Services.BlobStore.WebApi;
using Microsoft.VisualStudio.Services.Content.Common;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;
using Microsoft.VisualStudio.Services.PipelineCache.WebApi;
using Microsoft.VisualStudio.Services.WebApi;
using Newtonsoft.Json;

namespace Agent.Plugins.PipelineCache
{
    public class PipelineCacheServer
    {
        internal async Task UploadAsync(
            AgentTaskPluginExecutionContext context,
            IEnumerable<string> key,
            string path,
            string salt,
            CancellationToken cancellationToken)
        {
            VssConnection connection = context.VssConnection;
            BlobStoreClientTelemetry clientTelemetry;
            DedupManifestArtifactClient dedupManifestClient = DedupManifestArtifactClientFactory.CreateDedupManifestClient(context, connection, out clientTelemetry);
            PipelineCacheClient pipelineCacheClient = this.CreateClient(clientTelemetry, context, connection);

            using (clientTelemetry)
            {
                // Check if the key exists.
                GetPipelineCacheArtifactOptions getOptions = new GetPipelineCacheArtifactOptions
                {
                    Key = key,
                    Salt = salt,
                };
                PipelineCacheActionRecord cacheRecordGet = clientTelemetry.CreateRecord<PipelineCacheActionRecord>((level, uri, type) =>
                        new PipelineCacheActionRecord(level, uri, type, PipelineArtifactConstants.RestoreCache, context));
                PipelineCacheArtifact getResult = await pipelineCacheClient.GetPipelineCacheArtifactAsync(getOptions, cancellationToken, cacheRecordGet);
                // Send results to CustomerIntelligence
                context.PublishTelemetry(area: PipelineArtifactConstants.AzurePipelinesAgent, feature: PipelineArtifactConstants.PipelineCache, record: cacheRecordGet);
                //If cache exists, return.
                if (getResult != null)
                {
                    context.Output($"Cache with fingerprint {getResult.Fingerprint} already exists.");
                    return;
                }

                //Upload the pipeline artifact.
                PipelineCacheActionRecord uploadRecord = clientTelemetry.CreateRecord<PipelineCacheActionRecord>((level, uri, type) =>
                    new PipelineCacheActionRecord(level, uri, type, nameof(dedupManifestClient.PublishAsync), context));
                PublishResult result = await clientTelemetry.MeasureActionAsync(
                    record: uploadRecord,
                    actionAsync: async () =>
                    {
                        return await dedupManifestClient.PublishAsync(path, cancellationToken);
                    });

                CreatePipelineCacheArtifactOptions options = new CreatePipelineCacheArtifactOptions
                {
                    Key = key,
                    RootId = result.RootId,
                    ManifestId = result.ManifestId,
                    ProofNodes = result.ProofNodes.ToArray(),
                    Salt = salt
                };

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
            IEnumerable<string> key,
            string path,
            string salt,
            string variableToSetOnHit,
            CancellationToken cancellationToken)
        {
            VssConnection connection = context.VssConnection;
            BlobStoreClientTelemetry clientTelemetry;
            DedupManifestArtifactClient dedupManifestClient = DedupManifestArtifactClientFactory.CreateDedupManifestClient(context, connection, out clientTelemetry);
            PipelineCacheClient pipelineCacheClient = this.CreateClient(clientTelemetry, context, connection);
            GetPipelineCacheArtifactOptions options = new GetPipelineCacheArtifactOptions
            {
                Key = key,
                Salt = salt,
            };
            
            using (clientTelemetry)
            {
                PipelineCacheActionRecord cacheRecord = clientTelemetry.CreateRecord<PipelineCacheActionRecord>((level, uri, type) =>
                        new PipelineCacheActionRecord(level, uri, type, PipelineArtifactConstants.RestoreCache, context));
                PipelineCacheArtifact result = await pipelineCacheClient.GetPipelineCacheArtifactAsync(options, cancellationToken, cacheRecord);

                // Send results to CustomerIntelligence
                context.PublishTelemetry(area: PipelineArtifactConstants.AzurePipelinesAgent, feature: PipelineArtifactConstants.PipelineCache, record: cacheRecord);

                if (result == null)
                {
                    return;
                }
                else
                {
                    context.Output($"Manifest ID is: {result.ManifestId.ValueString}");
                    PipelineCacheActionRecord downloadRecord = clientTelemetry.CreateRecord<PipelineCacheActionRecord>((level, uri, type) =>
                        new PipelineCacheActionRecord(level, uri, type, nameof(DownloadAsync), context));
                    await clientTelemetry.MeasureActionAsync(
                        record: downloadRecord,
                        actionAsync: async () =>
                        {
                            await this.DownloadPipelineCacheAsync(dedupManifestClient, result.ManifestId, path, cancellationToken);
                        });

                    // Send results to CustomerIntelligence
                    context.PublishTelemetry(area: PipelineArtifactConstants.AzurePipelinesAgent, feature: PipelineArtifactConstants.PipelineCache, record: downloadRecord);
                    
                    if (!string.IsNullOrEmpty(variableToSetOnHit))
                    {
                        context.SetVariable($"{PipelineArtifactConstants.PipelineCache}.{variableToSetOnHit}", "True");
                    }
                    Console.WriteLine("Cache restored.");
                }
            }
        }

        private PipelineCacheClient CreateClient(
            BlobStoreClientTelemetry blobStoreClientTelemetry,
            AgentTaskPluginExecutionContext context,
            VssConnection connection)
        {
            var tracer = new CallbackAppTraceSource(str => context.Output(str), System.Diagnostics.SourceLevels.Information);
            IClock clock = UtcClock.Instance;
            var pipelineCacheHttpClient = connection.GetClient<PipelineCacheHttpClient>();
            var pipelineCacheClient = new PipelineCacheClient(blobStoreClientTelemetry, pipelineCacheHttpClient, clock, tracer);

            return pipelineCacheClient;
        }

        private Task DownloadPipelineCacheAsync(
            DedupManifestArtifactClient dedupManifestClient,
            DedupIdentifier manifestId,
            string targetDirectory,
            CancellationToken cancellationToken)
        {
            DownloadDedupManifestArtifactOptions options = DownloadDedupManifestArtifactOptions.CreateWithManifestId(
                manifestId,
                targetDirectory,
                proxyUri: null,
                minimatchPatterns: null);
            return dedupManifestClient.DownloadAsync(options, cancellationToken);
        }
    }
}