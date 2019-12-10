// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.Build.WebApi;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Agent.Plugins.PipelineArtifact
{
    internal interface IArtifactProvider
    {
        Task DownloadSingleArtifactAsync(PipelineArtifactDownloadParameters downloadParameters, BuildArtifact buildArtifact, CancellationToken cancellationToken);
        Task DownloadMultipleArtifactsAsync(PipelineArtifactDownloadParameters downloadParameters, IEnumerable<BuildArtifact> buildArtifacts, CancellationToken cancellationToken);
    }
}
