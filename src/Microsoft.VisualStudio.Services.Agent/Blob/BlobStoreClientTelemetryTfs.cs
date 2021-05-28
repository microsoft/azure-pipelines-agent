// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
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

        public async Task CommitTelemetry(Guid planId, Guid jobId)
        {
            var ciSender = this.senders.OfType<CustomerIntelligenceTelemetrySender>().FirstOrDefault();
            await (ciSender?.CommitTelemetry(planId, jobId) ?? Task.CompletedTask);
        }
    }
}