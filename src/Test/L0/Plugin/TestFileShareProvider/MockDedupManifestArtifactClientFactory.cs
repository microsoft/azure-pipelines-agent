// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using Agent.Sdk;
using Microsoft.VisualStudio.Services.BlobStore.WebApi;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.BlobStore.Common.Telemetry;
using Agent.Plugins.PipelineArtifact;

namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    public class MockDedupManifestArtifactClientFactory : IDedupManifestArtifactClientFactory
    {
        private TestTelemetrySender telemetrySender;
        private readonly Uri baseAddress = new Uri("http://testBaseAddress");
        public DedupManifestArtifactClient CreateDedupManifestClient(AgentTaskPluginExecutionContext context, VssConnection connection, CancellationToken cancellationToken, out BlobStoreClientTelemetry telemetry)
        {
            telemetrySender = new TestTelemetrySender();
            telemetry = new BlobStoreClientTelemetry(
                NoopAppTraceSource.Instance,
                baseAddress,
                telemetrySender);

            return null;
        }
    }
}