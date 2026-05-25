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
            CancellationToken cancellationToken = default(CancellationToken));
    }

    public class BuildServer : AgentService, IBuildServer
    {
        private VssConnection _connection;
        // Exposed as internal (not private) so unit tests in the Test assembly can substitute a mocked
        // BuildHttpClient via [assembly: InternalsVisibleTo("Test")] declared in AssemblyInfo.cs.
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
            CancellationToken cancellationToken = default(CancellationToken))
        {
            // Prefer the bulk AddBuildTagsAsync overload (body transport), which preserves reserved
            // URL characters such as ';'. The legacy single-tag AddBuildTagAsync overload encodes
            // the tag into the URL path and mangles such characters, causing the post-call
            // verification in BuildAddBuildTagCommand to fail. See:
            //   https://github.com/microsoft/azure-pipelines-task-lib/issues/1072
            //
            // The kill-switch knob UseBuildTagsBodyApi (set the agent-host environment variable
            // AGENT_USE_BUILD_TAGS_BODY_API=false and restart the agent) lets operators fall back
            // to the legacy URL-path API if the body-based endpoint is unsupported on their
            // on-prem Azure DevOps Server version. Do not "simplify" this branch away.
            bool useBodyApi = AgentKnobs.UseBuildTagsBodyApi
                .GetValue(UtilKnobValueContext.Instance())
                .AsBoolean();

            if (useBodyApi)
            {
                return await _buildHttpClient.AddBuildTagsAsync(new[] { buildTag }, projectId, buildId, cancellationToken: cancellationToken);
            }

            return await _buildHttpClient.AddBuildTagAsync(projectId, buildId, buildTag, cancellationToken: cancellationToken);
        }
    }
}
