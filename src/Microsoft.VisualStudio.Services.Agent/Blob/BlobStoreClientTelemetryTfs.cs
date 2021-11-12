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
        private CustomerIntelligenceTelemetrySender sender;

        public BlobStoreClientTelemetryTfs(IAppTraceSource tracer, Uri baseAddress, VssConnection connection) 
            : this(tracer, baseAddress, new CustomerIntelligenceTelemetrySender(connection))
        {
        }

        // for testing
        public BlobStoreClientTelemetryTfs(IAppTraceSource tracer, Uri baseAddress, VssConnection connection, ITelemetrySender sender)
            : base(tracer, baseAddress, sender)
        {
            this.sender = sender as CustomerIntelligenceTelemetrySender;
        }

        private BlobStoreClientTelemetryTfs(IAppTraceSource tracer, Uri baseAddress, CustomerIntelligenceTelemetrySender sender) 
            : base(tracer, baseAddress, sender)
        {
            this.sender = sender;
        }

        public async Task CommitTelemetryUpload(Guid planId, Guid jobId)
        {
            await (this.sender?.CommitTelemetryUpload(planId, jobId) ?? Task.CompletedTask);
        }

        public Dictionary<string, object> GetArtifactDownloadTelemetry(Guid planId, Guid jobId)
        {
            return this.sender?.GetArtifactDownloadTelemetry(planId, jobId);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.sender = null;
            }

            base.Dispose(disposing);
        }
    }
}