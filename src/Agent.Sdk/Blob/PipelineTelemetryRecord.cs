// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Agent.Sdk.Knob;
using Microsoft.VisualStudio.Services.Content.Common.Telemetry;
using Microsoft.VisualStudio.Services.BlobStore.Common.Telemetry;
using Microsoft.TeamFoundation.DistributedTask.WebApi;

namespace Agent.Sdk.Blob
{
    /// <summary>
    /// Generic telemetry record for use with Pipeline events.
    /// </summary>
    public abstract class PipelineTelemetryRecord : BlobStoreTelemetryRecord
    {
        public Guid PlanId { get; private set; }
        public Guid JobId { get; private set; }
        public Guid TaskInstanceId { get; private set; }

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
    }
}