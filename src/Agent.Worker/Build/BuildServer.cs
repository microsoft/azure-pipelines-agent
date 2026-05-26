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
using Agent.Sdk.Knob;

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
            IKnobValueContext knobContext,
            CancellationToken cancellationToken = default(CancellationToken));
    }

    public class BuildServer : AgentService, IBuildServer
    {
        private VssConnection _connection;
        // Exposed as internal so unit tests in the Test assembly can inject a mocked
        // BuildHttpClient via [assembly: InternalsVisibleTo("Test")].
        internal Build2.BuildHttpClient _buildHttpClient;

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
            IKnobValueContext knobContext,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            ArgUtil.NotNull(knobContext, nameof(knobContext));

            // Prefer the body-based AddBuildTagsAsync overload, which preserves reserved URL
            // characters such as ';'. The legacy AddBuildTagAsync overload encodes the tag into
            // the URL path and mangles such characters, causing the post-call verification in
            // BuildAddBuildTagCommand to fail. See azure-pipelines-task-lib#1072.
            // The UseBuildTagsBodyApi knob is a kill-switch for on-prem servers that don't
            // support the body endpoint.
            bool useBodyApi = AgentKnobs.UseBuildTagsBodyApi
                .GetValue(knobContext)
                .AsBoolean();

            if (useBodyApi)
            {
                Trace.Info("Adding build tag using body-based API.");
                return await _buildHttpClient.AddBuildTagsAsync(new[] { buildTag }, projectId, buildId, cancellationToken: cancellationToken);
            }

            Trace.Info("Adding build tag using legacy URL-path API.");
            return await _buildHttpClient.AddBuildTagAsync(projectId, buildId, buildTag, cancellationToken: cancellationToken);
        }
    }
}
