// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;
using Microsoft.VisualStudio.Services.Content.Common.Telemetry;
using Microsoft.VisualStudio.Services.BlobStore.Common.Telemetry;

namespace Microsoft.VisualStudio.Services.Agent.Blob
{
    public class BlobStoreClientTelemetryTfs : BlobStoreClientTelemetry
    {
        public BlobStoreClientTelemetryTfs(IAppTraceSource tracer, Uri baseAddress, VssConnection connection) 
            : base(tracer, baseAddress, new CustomerIntelligenceTelemetrySender(connection))
        {
        }

        // for testing
        public BlobStoreClientTelemetryTfs(IAppTraceSource tracer, Uri baseAddress, VssConnection connection, ITelemetrySender sender)
            : base(tracer, baseAddress, sender)
        {
        }

        public async Task CommitTelemetryUpload(Guid planId, Guid jobId)
        {
            var ciSender = this.senders.OfType<CustomerIntelligenceTelemetrySender>().FirstOrDefault();
            await (ciSender?.CommitTelemetryUpload(planId, jobId) ?? Task.CompletedTask);
        }

        public Dictionary<string, object> GetArtifactDownloadTelemetry(Guid planId, Guid jobId)
        {
            var ciSender = this.senders.OfType<CustomerIntelligenceTelemetrySender>().FirstOrDefault();
            return ciSender?.GetArtifactDownloadTelemetry(planId, jobId);
        }
    }
}