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
    /// Generic telemetry record for use with Pipeline events.
    /// </summary>
    public abstract class PipelineTelemetryRecord : BlobStoreTelemetryRecord, IDedupRecord
    {
        public Guid PlanId { get; private set; }
        public Guid JobId { get; private set; }
        public Guid TaskInstanceId { get; private set; }
        public DedupUploadStatistics UploadStatistics { get; private set; }
        public DedupDownloadStatistics DownloadStatistics { get; private set; }

        public PipelineTelemetryRecord(
            TelemetryInformationLevel level, 
            Uri baseAddress, 
            string eventNamePrefix, 
            string eventNameSuffix, 
            IKnobValueContext context, 
            uint attemptNumber = 1)
            : base(level, baseAddress, eventNamePrefix, eventNameSuffix, attemptNumber)
        {
            PlanId = new Guid(context.GetVariableValueOrDefault(WellKnownDistributedTaskVariables.PlanId) ?? Guid.Empty.ToString());
            JobId = new Guid(context.GetVariableValueOrDefault(WellKnownDistributedTaskVariables.JobId) ?? Guid.Empty.ToString());
            TaskInstanceId = new Guid(context.GetVariableValueOrDefault(WellKnownDistributedTaskVariables.TaskInstanceId) ?? Guid.Empty.ToString());
        }

        public PipelineTelemetryRecord(
            TelemetryInformationLevel level, 
            Uri baseAddress, 
            string eventNamePrefix, 
            string eventNameSuffix, 
            Guid planId,
            Guid jobId,
            Guid taskInstanceId,
            uint attemptNumber = 1)
            : base(level, baseAddress, eventNamePrefix, eventNameSuffix, attemptNumber)
        {
            PlanId = planId;
            JobId = jobId;
            TaskInstanceId = taskInstanceId;
        }

        protected override void SetMeasuredActionResult<T>(T value)
        {
            if (value is DedupUploadStatistics upStats)
            {
                UploadStatistics = upStats;
            }
            if (value is DedupDownloadStatistics downStats)
            {
                DownloadStatistics = downStats;
            }
        }
    }
}