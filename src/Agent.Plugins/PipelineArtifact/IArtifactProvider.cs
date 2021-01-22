// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using Microsoft.TeamFoundation.Build.WebApi;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Agent.Plugins.PipelineArtifact
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1068: CancellationToken parameters must come last")]
    internal interface IArtifactProvider
    {
        Task DownloadSingleArtifactAsync(PipelineArtifactDownloadParameters downloadParameters, BuildArtifact buildArtifact, CancellationToken cancellationToken, AgentTaskPluginExecutionContext context);
        Task DownloadMultipleArtifactsAsync(PipelineArtifactDownloadParameters downloadParameters, IEnumerable<BuildArtifact> buildArtifacts, CancellationToken cancellationToken, AgentTaskPluginExecutionContext context);
    }
}
