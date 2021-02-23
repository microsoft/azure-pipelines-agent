// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Agent.Sdk.Blob;
using Microsoft.VisualStudio.Services.Content.Common.Telemetry;

namespace Microsoft.VisualStudio.Services.Agent.Worker.Build
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
            IAsyncCommandContext context, 
            uint attemptNumber = 1)
            : base(level, baseAddress, eventNamePrefix, eventNameSuffix, context, attemptNumber)
        {
        }
    }
}