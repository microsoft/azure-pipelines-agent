// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Agent.Sdk.Knob;
using Microsoft.VisualStudio.Services.Content.Common.Telemetry;
using Microsoft.VisualStudio.Services.BlobStore.Common.Telemetry;
using Microsoft.TeamFoundation.DistributedTask.WebApi;

namespace Microsoft.VisualStudio.Services.Agent.Blob
{
    /// <summary>
    /// Generic telemetry record for use with timeline record events.
    /// </summary>
    public class TimelineRecordAttachmentTelemetryRecord : BlobStoreTelemetryRecord
    {
        public TimelineRecordAttachmentTelemetryRecord(
            TelemetryInformationLevel level, 
            Uri baseAddress, 
            string eventNamePrefix, 
            string eventNameSuffix,
            uint attemptNumber = 1)
            : base(level, baseAddress, eventNamePrefix, eventNameSuffix, attemptNumber)
        {
        }
    }
}