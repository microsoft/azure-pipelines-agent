// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Agent.Sdk;
using Microsoft.VisualStudio.Services.BlobStore.WebApi;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.Content.Common;
using Microsoft.VisualStudio.Services.BlobStore.Common.Telemetry;

namespace Agent.Plugins.PipelineArtifact
{
    public interface IDedupManifestArtifactClientFactory
    {
        Task<(DedupManifestArtifactClient client, BlobStoreClientTelemetry telemetry)> CreateDedupManifestClientAsync(AgentTaskPluginExecutionContext context, VssConnection connection, CancellationToken cancellationToken);
    }

    public class DedupManifestArtifactClientFactory : IDedupManifestArtifactClientFactory
    {
        public static readonly DedupManifestArtifactClientFactory Instance = new DedupManifestArtifactClientFactory();

        private DedupManifestArtifactClientFactory() 
        {
        }
        
        public async Task<(DedupManifestArtifactClient client, BlobStoreClientTelemetry telemetry)> CreateDedupManifestClientAsync(AgentTaskPluginExecutionContext context, VssConnection connection, CancellationToken cancellationToken)
        {
            const int maxRetries = 5;
            var tracer = context.CreateArtifactsTracer();
            var dedupStoreHttpClient = await AsyncHttpRetryHelper.InvokeAsync(
                () =>
                {
                    ArtifactHttpClientFactory factory = new ArtifactHttpClientFactory(
                        connection.Credentials,
                        TimeSpan.FromSeconds(50),
                        tracer,
                        cancellationToken);
                
                    // this is actually a hidden network call to the location service:
                     return Task.FromResult(factory.CreateVssHttpClient<IDedupStoreHttpClient, DedupStoreHttpClient>(connection.GetClient<DedupStoreHttpClient>().BaseAddress));
                
                },
                maxRetries: maxRetries,
                tracer: tracer,
                canRetryDelegate: e => true,
                context: nameof(CreateDedupManifestClientAsync),
                cancellationToken: cancellationToken,
                continueOnCapturedContext: false);

            var telemetry = new BlobStoreClientTelemetry(tracer, dedupStoreHttpClient.BaseAddress);
            var client = new DedupStoreClientWithDataport(dedupStoreHttpClient, PipelineArtifactProvider.GetDedupStoreClientMaxParallelism(context));
            return (new DedupManifestArtifactClient(telemetry, client, tracer), telemetry);
        }
    }
}