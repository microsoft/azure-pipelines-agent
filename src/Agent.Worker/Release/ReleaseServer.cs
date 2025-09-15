// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.ReleaseManagement.WebApi.Clients;
using Microsoft.VisualStudio.Services.ReleaseManagement.WebApi.Contracts;
using Microsoft.VisualStudio.Services.WebApi;
using RMContracts = Microsoft.VisualStudio.Services.ReleaseManagement.WebApi;
using Agent.Sdk.Util;

namespace Microsoft.VisualStudio.Services.Agent.Worker.Release
{
    [ServiceLocator(Default = typeof(ReleaseServer))]
    public interface IReleaseServer : IAgentService
    {
        Task ConnectAsync(VssConnection jobConnection);
        IEnumerable<AgentArtifactDefinition> GetReleaseArtifactsFromService(
            int releaseId,
            Guid projectId,
            CancellationToken cancellationToken = default(CancellationToken));
        Task<RMContracts.Release> UpdateReleaseName(
            string releaseId,
            Guid projectId,
            string releaseName,
            CancellationToken cancellationToken = default(CancellationToken));
    }
    public class ReleaseServer : AgentService, IReleaseServer
    {
        private VssConnection _connection;

        private ReleaseHttpClient _releaseHttpClient;

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
                catch (SocketException ex)
                {
                    ExceptionsUtil.HandleSocketException(ex, _connection.Uri.ToString(), (message) => Trace.Error(message));
                }
                catch (Exception ex) when (attemptCount > 0)
                {
                    Trace.Info($"Catch exception during connect. {attemptCount} attemp left.");
                    Trace.Error(ex);
                }

                await Task.Delay(100);
            }

            _releaseHttpClient = _connection.GetClient<ReleaseHttpClient>();
        }

        public IEnumerable<AgentArtifactDefinition> GetReleaseArtifactsFromService(
            int releaseId,
            Guid projectId,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var artifacts = _releaseHttpClient.GetAgentArtifactDefinitionsAsync(projectId, releaseId, cancellationToken: cancellationToken).Result;
            return artifacts;
        }

        public async Task<RMContracts.Release> UpdateReleaseName(
            string releaseId,
            Guid projectId,
            string releaseName,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            RMContracts.ReleaseUpdateMetadata updateMetadata = new RMContracts.ReleaseUpdateMetadata()
            {
                Name = releaseName,
                Comment = StringUtil.Loc("RMUpdateReleaseNameForReleaseComment", releaseName)
            };

            return await _releaseHttpClient.UpdateReleaseResourceAsync(updateMetadata, projectId, int.Parse(releaseId), cancellationToken: cancellationToken);
        }
    }
}