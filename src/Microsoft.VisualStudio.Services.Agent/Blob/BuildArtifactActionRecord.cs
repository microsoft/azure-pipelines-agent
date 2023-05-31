// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Agent.Sdk.Knob;
using Microsoft.VisualStudio.Services.Content.Common.Telemetry;

namespace Microsoft.VisualStudio.Services.Agent.Blob
{
    /// <summary>
    /// Telemetry record for use with Build Artifact events.
    /// </summary>
    public class BuildArtifactActionRecord : PipelineTelemetryRecord
    {
        public BuildArtifactActionRecord(
            TelemetryInformationLevel level,
            Uri baseAddress,
            string eventNamePrefix,
            string eventNameSuffix,
            IKnobValueContext context,
            uint attemptNumber = 1)
            : base(level, baseAddress, eventNamePrefix, eventNameSuffix, context, attemptNumber)
        {
        }
    }
}