// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Agent.Sdk;
using Agent.Plugins.PipelineArtifact.Telemetry;
using Microsoft.TeamFoundation.Build.WebApi;
using BuildXL.Cache.ContentStore.Hashing;
using Microsoft.VisualStudio.Services.Agent.Blob;
using Microsoft.VisualStudio.Services.BlobStore.WebApi;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.Content.Common;
using Microsoft.VisualStudio.Services.BlobStore.Common;

namespace Agent.Plugins
{
    internal class PipelineArtifactProvider : IArtifactProvider
    {
        private readonly IAppTraceSource tracer;
        private readonly AgentTaskPluginExecutionContext context;
        private readonly VssConnection connection;

        public PipelineArtifactProvider(AgentTaskPluginExecutionContext context, VssConnection connection, IAppTraceSource tracer)
        {
            this.tracer = tracer;
            this.context = context;
            this.connection = connection;
        }

        public async Task DownloadSingleArtifactAsync(
            ArtifactDownloadParameters downloadParameters,
            BuildArtifact buildArtifact,
            CancellationToken cancellationToken,
            AgentTaskPluginExecutionContext context)
        {
            var (dedupManifestClient, clientTelemetry) = await DedupManifestArtifactClientFactory.Instance.CreateDedupManifestClientAsync(
                this.context.IsSystemDebugTrue(),
                (str) => this.context.Output(str),
                this.connection,
                DedupManifestArtifactClientFactory.Instance.GetDedupStoreClientMaxParallelism(context),
                WellKnownDomainIds.DefaultDomainId,
                Microsoft.VisualStudio.Services.BlobStore.WebApi.Contracts.Client.PipelineArtifact,
                hashType,
                cancellationToken);

            using (clientTelemetry)
            {
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

        public async Task DownloadMultipleArtifactsAsync(
            ArtifactDownloadParameters downloadParameters,
            IEnumerable<BuildArtifact> buildArtifacts,
            CancellationToken cancellationToken,
            AgentTaskPluginExecutionContext context)
        {
            var (dedupManifestClient, clientTelemetry) = await DedupManifestArtifactClientFactory.Instance.CreateDedupManifestClientAsync(
                this.context.IsSystemDebugTrue(),
                (str) => this.context.Output(str),
                this.connection,
                DedupManifestArtifactClientFactory.Instance.GetDedupStoreClientMaxParallelism(context),
                WellKnownDomainIds.DefaultDomainId,
                Microsoft.VisualStudio.Services.BlobStore.WebApi.Contracts.Client.PipelineArtifact,
                hashType,
                cancellationToken);

            using (clientTelemetry)
            {
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
