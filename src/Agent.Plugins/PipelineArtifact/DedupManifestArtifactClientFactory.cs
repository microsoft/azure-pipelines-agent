using System;
using System.Diagnostics;
using System.Threading;
using Agent.Sdk;
using Microsoft.VisualStudio.Services.BlobStore.Common.Telemetry;
using Microsoft.VisualStudio.Services.BlobStore.WebApi;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.Content.Common;
using Microsoft.VisualStudio.Services.Common;

namespace Agent.Plugins.PipelineArtifact
{    
    public static class DedupManifestArtifactClientFactory
    {
        public static DedupManifestArtifactClient CreateDedupManifestClient(AgentTaskPluginExecutionContext context, VssConnection connection, CancellationToken cancellationToken, out BlobStoreClientTelemetry telemetry)
        {
            var tracer = new CallbackAppTraceSource(str => context.Output(str), SourceLevels.Information);

            ArtifactHttpClientFactory factory = new ArtifactHttpClientFactory(
                connection.Credentials,
                TimeSpan.FromSeconds(50),
                tracer,
                cancellationToken);

            var dedupStoreHttpClient = factory.CreateVssHttpClient<IDedupStoreHttpClient, DedupStoreHttpClient>(connection.GetClient<DedupStoreHttpClient>().BaseAddress);
            
            var client = new DedupStoreClientWithDataport(dedupStoreHttpClient, PipelineArtifactProvider.GetDedupStoreClientMaxParallelism(context));
            return new DedupManifestArtifactClient(telemetry = new BlobStoreClientTelemetry(tracer, dedupStoreHttpClient.BaseAddress), client, tracer);
        }
    }
}