// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Agent.Sdk;
using Agent.Plugins.PipelineArtifact.Telemetry;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using Microsoft.VisualStudio.Services.BlobStore.Common.Telemetry;
using Microsoft.VisualStudio.Services.BlobStore.WebApi;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.Content.Common;

namespace Agent.Plugins.PipelineArtifact
{
    internal class PipelineArtifactProvider : IArtifactProvider
    {
        // Old default for hosted agents was 16*2 cores = 32. 
        // In my tests of a node_modules folder, this 32x parallelism was consistently around 47 seconds.
        // At 192x it was around 16 seconds and 256x was no faster.
        private const int DefaultDedupStoreClientMaxParallelism = 192;

        internal static int GetDedupStoreClientMaxParallelism(AgentTaskPluginExecutionContext context) {
            int parallelism = DefaultDedupStoreClientMaxParallelism;
            if(context.Variables.TryGetValue("AZURE_PIPELINES_DEDUP_PARALLELISM", out VariableValue v)) {
                if (!int.TryParse(v.Value, out parallelism)) {
                    context.Info($"Could not parse the value of AZURE_PIPELINES_DEDUP_PARALLELISM, '{v.Value}', as an integer. Defaulting to {DefaultDedupStoreClientMaxParallelism}");
                    parallelism = DefaultDedupStoreClientMaxParallelism;
                }
            }
            context.Info(string.Format("Dedup parallelism: {0}", parallelism));
            return parallelism;
        } 

        private readonly IAppTraceSource tracer;
        private readonly AgentTaskPluginExecutionContext context;
        private readonly VssConnection connection;

        public PipelineArtifactProvider(AgentTaskPluginExecutionContext context, VssConnection connection, IAppTraceSource tracer)
        {
            var dedupStoreHttpClient = connection.GetClient<DedupStoreHttpClient>();
            this.tracer = tracer;
            this.context = context;
            this.connection = connection;
            dedupStoreHttpClient.SetTracer(tracer);
            int parallelism = GetDedupStoreClientMaxParallelism(context);
            var client = new DedupStoreClientWithDataport(dedupStoreHttpClient, parallelism);
        }

        public async Task DownloadSingleArtifactAsync(PipelineArtifactDownloadParameters downloadParameters, BuildArtifact buildArtifact, CancellationToken cancellationToken, AgentTaskPluginExecutionContext context)
        {
            DedupManifestArtifactClient dedupManifestClient = DedupManifestArtifactClientFactory.Instance.CreateDedupManifestClient(
                this.context, this.connection, cancellationToken, out BlobStoreClientTelemetry clientTelemetry);

            using(clientTelemetry) {
                var manifestId = DedupIdentifier.Create(buildArtifact.Resource.Data);
                var options = DownloadDedupManifestArtifactOptions.CreateWithManifestId(
                    manifestId,
                    downloadParameters.TargetDirectory,
                    proxyUri: null,
                    minimatchPatterns: downloadParameters.MinimatchFilters);

                PipelineArtifactActionRecord downloadRecord = clientTelemetry.CreateRecord<PipelineArtifactActionRecord>((level, uri, type) =>
                    new PipelineArtifactActionRecord(level, uri, type, nameof(DownloadMultipleArtifactsAsync), this.context));
                await clientTelemetry.MeasureActionAsync(
                    record: downloadRecord,
                    actionAsync: async () =>
                    {
                        await AsyncHttpRetryHelper.InvokeVoidAsync(
                            async () =>
                            {
                                await dedupManifestClient.DownloadAsync(options, cancellationToken);
                            },
                            maxRetries: 3,
                            tracer: tracer,
                            canRetryDelegate: e => true,
                            context: nameof(DownloadSingleArtifactAsync),
                            cancellationToken: cancellationToken,
                            continueOnCapturedContext: false);
                    });
                // Send results to CustomerIntelligence
                this.context.PublishTelemetry(area: PipelineArtifactConstants.AzurePipelinesAgent, feature: PipelineArtifactConstants.PipelineArtifact, record: downloadRecord);
            }
        }

        public async Task DownloadMultipleArtifactsAsync(PipelineArtifactDownloadParameters downloadParameters, IEnumerable<BuildArtifact> buildArtifacts, CancellationToken cancellationToken, AgentTaskPluginExecutionContext context)
        {
            DedupManifestArtifactClient dedupManifestClient = DedupManifestArtifactClientFactory.Instance.CreateDedupManifestClient(
                this.context, this.connection, cancellationToken, out BlobStoreClientTelemetry clientTelemetry);

            using(clientTelemetry) {
                var artifactNameAndManifestIds = buildArtifacts.ToDictionary(
                    keySelector: (a) => a.Name, // keys should be unique, if not something is really wrong
                    elementSelector: (a) => DedupIdentifier.Create(a.Resource.Data));
                // 2) download to the target path
                var options = DownloadDedupManifestArtifactOptions.CreateWithMultiManifestIds(
                    artifactNameAndManifestIds,
                    downloadParameters.TargetDirectory,
                    proxyUri: null,
                    minimatchPatterns: downloadParameters.MinimatchFilters,
                    minimatchFilterWithArtifactName: downloadParameters.MinimatchFilterWithArtifactName);

                PipelineArtifactActionRecord downloadRecord = clientTelemetry.CreateRecord<PipelineArtifactActionRecord>((level, uri, type) =>
                    new PipelineArtifactActionRecord(level, uri, type, nameof(DownloadMultipleArtifactsAsync), this.context));
                await clientTelemetry.MeasureActionAsync(
                    record: downloadRecord,
                    actionAsync: async () =>
                    {
                        await AsyncHttpRetryHelper.InvokeVoidAsync(
                            async () =>
                            {
                                await dedupManifestClient.DownloadAsync(options, cancellationToken);
                            },
                            maxRetries: 3,
                            tracer: tracer,
                            canRetryDelegate: e => true,
                            context: nameof(DownloadMultipleArtifactsAsync),
                            cancellationToken: cancellationToken,
                            continueOnCapturedContext: false);
                    });
                // Send results to CustomerIntelligence
                this.context.PublishTelemetry(area: PipelineArtifactConstants.AzurePipelinesAgent, feature: PipelineArtifactConstants.PipelineArtifact, record: downloadRecord);
            }
        }
    }
}
