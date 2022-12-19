// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using Agent.Sdk;
using Microsoft.VisualStudio.Services.Agent.Blob;
using Microsoft.VisualStudio.Services.BlobStore.WebApi;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.BlobStore.Common.Telemetry;
using Agent.Plugins.PipelineArtifact;
using Microsoft.VisualStudio.Services.BlobStore.Common;

namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    public class MockDedupManifestArtifactClientFactory : IDedupManifestArtifactClientFactory
    {
        private TestTelemetrySender telemetrySender;
        private readonly Uri baseAddress = new Uri("http://testBaseAddress");
        public Task<(DedupManifestArtifactClient client, BlobStoreClientTelemetry telemetry)> CreateDedupManifestClientAsync(
            bool verbose,
            Action<string> traceOutput,
            VssConnection connection,
            int maxParallelism,
            IDomainId domainId,
            CancellationToken cancellationToken)
        {
            telemetrySender = new TestTelemetrySender();
            return Task.FromResult((client: (DedupManifestArtifactClient)null, telemetry: new BlobStoreClientTelemetry(
                NoopAppTraceSource.Instance,
                baseAddress,
                telemetrySender)));

        }

        public Task<(DedupStoreClient client, BlobStoreClientTelemetryTfs telemetry)> CreateDedupClientAsync(bool verbose, Action<string> traceOutput, VssConnection connection, int maxParallelism, CancellationToken cancellationToken)
        {
            telemetrySender = new TestTelemetrySender();
            return Task.FromResult((client: (DedupStoreClient)null, telemetry: new BlobStoreClientTelemetryTfs(
                NoopAppTraceSource.Instance,
                baseAddress,
                connection,
                telemetrySender)));

        }

        public int GetDedupStoreClientMaxParallelism(AgentTaskPluginExecutionContext context)
        {
            return 4;
        }
    }
}