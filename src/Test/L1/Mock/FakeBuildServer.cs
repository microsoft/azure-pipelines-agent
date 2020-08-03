// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.Agent.Worker.Build;
using Build2 = Microsoft.TeamFoundation.Build.WebApi;

namespace Microsoft.VisualStudio.Services.Agent.Tests.L1.Worker
{
    public class FakeBuildServer : AgentService, IBuildServer
    {
        public List<string> AssosciatedArtifacts { get; }
        public List<string> BuildTags { get; }
        public string BuildNumber { get; internal set; }

        public FakeBuildServer()
        {
            AssosciatedArtifacts = new List<string>();
            BuildTags = new List<string>();
        }

        public Task ConnectAsync(VssConnection jobConnection)
        {
            return Task.CompletedTask;
        }

        public Task<Build2.BuildArtifact> AssociateArtifactAsync(
            int buildId,
            Guid projectId,
            string name,
            string jobId,
            string type,
            string data,
            Dictionary<string, string> propertiesDictionary,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            AssosciatedArtifacts.Add(data);
            return Task.FromResult(new Build2.BuildArtifact
            {
                Name = data
            });
        }

        public Task<Build2.Build> UpdateBuildNumber(
            int buildId,
            Guid projectId,
            string buildNumber,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            BuildNumber = buildNumber;
            return Task.FromResult(new Build2.Build
            {
                BuildNumber = buildNumber
            });
        }

        public Task<IEnumerable<string>> AddBuildTag(
            int buildId,
            Guid projectId,
            string buildTag,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            BuildTags.Add(buildTag);
            return Task.FromResult<IEnumerable<string>>(BuildTags);
        }
    }
}