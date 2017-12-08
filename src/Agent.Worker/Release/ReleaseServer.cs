using System;
using System.Collections.Generic;
using System.Threading;

using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.ReleaseManagement.WebApi.Clients;
using Microsoft.VisualStudio.Services.ReleaseManagement.WebApi.Contracts;
using Microsoft.VisualStudio.Services.WebApi;

namespace Agent.Worker.Release
{
    public class ReleaseServer
    {
        private Uri _projectCollectionUrl;
        private VssConnection _connection;
        private Guid _projectId;

        private ReleaseHttpClient _releaseHttpClient { get; }

        public ReleaseServer(Uri projectCollection, VssConnection connection, Guid projectId)
        {
            ArgUtil.NotNull(projectCollection, nameof(projectCollection));
            ArgUtil.NotNull(connection, nameof(connection));

            _projectCollectionUrl = projectCollection;
            _connection = connection;
            _projectId = projectId;

            _releaseHttpClient = _connection.GetClient<ReleaseHttpClient>();
        }

        public IEnumerable<AgentArtifactDefinition> GetReleaseArtifactsFromService(int releaseId, CancellationToken cancellationToken = default(CancellationToken))
        {
            var artifacts = _releaseHttpClient.GetAgentArtifactDefinitionsAsync(_projectId, releaseId, cancellationToken: cancellationToken).Result;
            return artifacts;
        }
    }
}