// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.ReleaseManagement.WebApi.Contracts;
using Microsoft.VisualStudio.Services.Agent.Worker.Release;
using RMContracts = Microsoft.VisualStudio.Services.ReleaseManagement.WebApi;

namespace Microsoft.VisualStudio.Services.Agent.Tests.L1.Worker
{
    public class FakeReleaseServer : AgentService, IReleaseServer
    {
        public string ReleaseName { get; internal set; }

        public Task ConnectAsync(VssConnection jobConnection)
        {
            return Task.CompletedTask;
        }

        public IEnumerable<AgentArtifactDefinition> GetReleaseArtifactsFromService(
            int releaseId,
            Guid projectId,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return new List<AgentArtifactDefinition>();
        }
        public Task<RMContracts.Release> UpdateReleaseName(
            string releaseId,
            Guid projectId,
            string releaseName,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            ReleaseName = releaseName;
            return Task.FromResult(new RMContracts.Release
            {
                Name = releaseName
            });
        }
    }
}