// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.Core.WebApi;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Build2 = Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Microsoft.VisualStudio.Services.Agent.Worker.Build
{
    [ServiceLocator(Default = typeof(BuildServer))]
    public interface IBuildServer : IAgentService
    {
        Task ConnectAsync(VssConnection jobConnection);
        Task<Build2.BuildArtifact> AssociateArtifactAsync(
            int buildId,
            Guid projectId,
            string name,
            string jobId,
            string type,
            string data,
            Dictionary<string, string> propertiesDictionary,
            CancellationToken cancellationToken = default(CancellationToken));
        Task<Build2.Build> UpdateBuildNumber(
            int buildId,
            Guid projectId,
            string buildNumber,
            CancellationToken cancellationToken = default(CancellationToken));
        Task<IEnumerable<string>> AddBuildTag(
            int buildId,
            Guid projectId,
            string buildTag,
            CancellationToken cancellationToken = default(CancellationToken));
    }

    public class BuildServer : AgentService, IBuildServer
    {
        private VssConnection _connection;
        private Build2.BuildHttpClient _buildHttpClient;

        public async Task ConnectAsync(VssConnection jobConnection)
        {
            ArgUtil.NotNull(jobConnection, nameof(jobConnection));

            _connection = jobConnection;
            int attemptCount = 5;
            while (!_connection.HasAuthenticated && attemptCount-- > 0)
            {
                try
                {
                    await _connection.ConnectAsync();
                    break;
                }
                catch (Exception ex) when (attemptCount > 0)
                {
                    Trace.Info($"Catch exception during connect. {attemptCount} attempt(s) left.");
                    Trace.Error(ex);
                }

                await Task.Delay(100);
            }

            _buildHttpClient = _connection.GetClient<Build2.BuildHttpClient>();
        }

        public async Task<Build2.BuildArtifact> AssociateArtifactAsync(
            int buildId,
            Guid projectId,
            string name,
            string jobId,
            string type,
            string data,
            Dictionary<string, string> propertiesDictionary,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            Build2.BuildArtifact artifact = new Build2.BuildArtifact()
            {
                Name = name,
                Source = jobId,
                Resource = new Build2.ArtifactResource()
                {
                    Data = data,
                    Type = type,
                    Properties = propertiesDictionary
                }
            };

            return await _buildHttpClient.CreateArtifactAsync(artifact, projectId, buildId, cancellationToken: cancellationToken);
        }

        public async Task<Build2.Build> UpdateBuildNumber(
            int buildId,
            Guid projectId,
            string buildNumber,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            Build2.Build build = new Build2.Build()
            {
                Id = buildId,
                BuildNumber = buildNumber,
                Project = new TeamProjectReference()
                {
                    Id = projectId,
                },
            };

            return await _buildHttpClient.UpdateBuildAsync(build, cancellationToken: cancellationToken);
        }

        public async Task<IEnumerable<string>> AddBuildTag(
            int buildId,
            Guid projectId,
            string buildTag,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return await _buildHttpClient.AddBuildTagAsync(projectId, buildId, buildTag, cancellationToken: cancellationToken);
        }
    }
}
