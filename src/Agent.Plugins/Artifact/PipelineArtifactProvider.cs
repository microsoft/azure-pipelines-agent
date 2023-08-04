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
using Microsoft.VisualStudio.Services.BlobStore.Common.Telemetry;

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
            // if  properties doesn't have it, use the default domain for backward compatibility
            IDomainId domainId = WellKnownDomainIds.DefaultDomainId;
            if(buildArtifact.Resource.Properties.TryGetValue(PipelineArtifactConstants.DomainId, out string domainIdString))
            {
                domainId = DomainIdFactory.Create(domainIdString);
            }

            var (dedupManifestClient, clientTelemetry) = await DedupManifestArtifactClientFactory.Instance.CreateDedupManifestClientAsync(
                this.context.IsSystemDebugTrue(),
                (str) => this.context.Output(str),
                this.connection,
                DedupManifestArtifactClientFactory.Instance.GetDedupStoreClientMaxParallelism(context),
                domainId,
                Microsoft.VisualStudio.Services.BlobStore.WebApi.Contracts.Client.PipelineArtifact,
                context,
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
            // create clients and group artifacts for each domain:
            Dictionary<IDomainId, (DedupManifestArtifactClient Client, BlobStoreClientTelemetry Telemetry, Dictionary<string, DedupIdentifier> ArtifactDictionary)> dedupManifestClients = 
                new Dictionary<IDomainId, (DedupManifestArtifactClient Client, BlobStoreClientTelemetry Telemetry, Dictionary<string, DedupIdentifier> ArtifactDictionary)>();

            foreach(var buildArtifact in buildArtifacts)
            {                
                // if  properties doesn't have it, use the default domain for backward compatibility
                IDomainId domainId = WellKnownDomainIds.DefaultDomainId;
                if(buildArtifact.Resource.Properties.TryGetValue(PipelineArtifactConstants.DomainId, out string domainIdString))
                {
                    domainId = DomainIdFactory.Create(domainIdString);
                }
                // Have we already created the clients for this domain?
                if(dedupManifestClients.ContainsKey(domainId)) {
                    // Clients already created for this domain, Just add the artifact to the list:
                    dedupManifestClients[domainId].ArtifactDictionary.Add(buildArtifact.Name, DedupIdentifier.Create(buildArtifact.Resource.Data));
                }
                else 
                {
                    // create the clients:
                    var (dedupManifestClient, clientTelemetry) = await DedupManifestArtifactClientFactory.Instance.CreateDedupManifestClientAsync(
                        this.context.IsSystemDebugTrue(),
                        (str) => this.context.Output(str),
                        this.connection,
                        DedupManifestArtifactClientFactory.Instance.GetDedupStoreClientMaxParallelism(context),
                        WellKnownDomainIds.DefaultDomainId,
                        Microsoft.VisualStudio.Services.BlobStore.WebApi.Contracts.Client.PipelineArtifact,
                        context,
                        cancellationToken);
                    
                    // and create the artifact dictionary with the current artifact
                    var artifactDictionary = new Dictionary<string, DedupIdentifier>();
                    artifactDictionary.Add(buildArtifact.Name, DedupIdentifier.Create(buildArtifact.Resource.Data));

                    dedupManifestClients.Add(domainId, (dedupManifestClient, clientTelemetry, artifactDictionary));
                }
            }
            foreach(var clientInfo in dedupManifestClients.Values)
            {
                using (clientInfo.Telemetry)
                {
                    // 2) download to the target path
                    var options = DownloadDedupManifestArtifactOptions.CreateWithMultiManifestIds(
                        clientInfo.ArtifactDictionary,
                        downloadParameters.TargetDirectory,
                        proxyUri: null,
                        minimatchPatterns: downloadParameters.MinimatchFilters,
                        minimatchFilterWithArtifactName: downloadParameters.MinimatchFilterWithArtifactName);

                    PipelineArtifactActionRecord downloadRecord = clientInfo.Telemetry.CreateRecord<PipelineArtifactActionRecord>((level, uri, type) =>
                        new PipelineArtifactActionRecord(level, uri, type, nameof(DownloadMultipleArtifactsAsync), this.context));

                    await clientInfo.Telemetry.MeasureActionAsync(
                        record: downloadRecord,
                        actionAsync: async () =>
                        {
                            await AsyncHttpRetryHelper.InvokeVoidAsync(
                                async () =>
                                {
                                    await clientInfo.Client.DownloadAsync(options, cancellationToken);
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
}
