using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Build2 = Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.WebApi;

namespace Microsoft.VisualStudio.Services.Agent.Worker.Build
{
    [ServiceLocator(Default = typeof(BuildServer))]
    public interface IBuildServer : IAgentService
    {
        Task ConnectAsync(VssConnection connection);
        Task<Build2.BuildArtifact> AssociateArtifact(Guid projectId, int buildId, string name, string type, string data, Dictionary<string, string> propertiesDictionary, CancellationToken cancellationToken);
        Task<Build2.Build> UpdateBuildNumber(Guid projectId, int buildId, string buildNumber, CancellationToken cancellationToken);
        Task<IEnumerable<string>> AddBuildTag(Guid projectId, int buildId, string buildTag, CancellationToken cancellationToken);
    }

    public sealed class BuildServer : AgentService, IBuildServer
    {
        private bool _hasConnection;
        private VssConnection _connection;
        private Build2.BuildHttpClient _buildHttpClient;

        public async Task ConnectAsync(VssConnection connection)
        {
            _connection = connection;
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
                    Trace.Info($"Catch exception during connect. {attemptCount} attempt left.");
                    Trace.Error(ex);
                }

                await Task.Delay(100);
            }

            _buildHttpClient = _connection.GetClient<Build2.BuildHttpClient>();
            _hasConnection = true;
        }

        private void CheckConnection()
        {
            if (!_hasConnection)
            {
                throw new InvalidOperationException("SetConnection");
            }
        }

        public async Task<Build2.BuildArtifact> AssociateArtifact(
            Guid projectId,
            int buildId,
            string name,
            string type,
            string data,
            Dictionary<string, string> propertiesDictionary,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            CheckConnection();
            Build2.BuildArtifact artifact = new Build2.BuildArtifact()
            {
                Name = name,
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
            Guid projectId,
            int buildId,
            string buildNumber,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            CheckConnection();
            Build2.Build build = new Build2.Build()
            {
                Id = buildId,
                BuildNumber = buildNumber,
                Project = new TeamProjectReference()
                {
                    Id = projectId,
                },
            };

            return await _buildHttpClient.UpdateBuildAsync(build, projectId, buildId, cancellationToken: cancellationToken);
        }

        public async Task<IEnumerable<string>> AddBuildTag(
            Guid projectId,
            int buildId,
            string buildTag,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            CheckConnection();
            return await _buildHttpClient.AddBuildTagAsync(projectId, buildId, buildTag, cancellationToken: cancellationToken);
        }
    }
}
