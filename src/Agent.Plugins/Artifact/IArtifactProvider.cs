// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using BuildXL.Cache.ContentStore.Hashing;
using Microsoft.TeamFoundation.Build.WebApi;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Agent.Plugins
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1068: CancellationToken parameters must come last")]
    internal interface IArtifactProvider
    {
        Task DownloadSingleArtifactAsync(
            ArtifactDownloadParameters downloadParameters,
            BuildArtifact buildArtifact,
            CancellationToken cancellationToken,
            AgentTaskPluginExecutionContext context);

        Task DownloadMultipleArtifactsAsync(
            ArtifactDownloadParameters downloadParameters,
            IEnumerable<BuildArtifact> buildArtifacts,
            CancellationToken cancellationToken,
            AgentTaskPluginExecutionContext context);
    }
}
