using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.ReleaseManagement.WebApi;
using Microsoft.VisualStudio.Services.ReleaseManagement.WebApi.Clients;
using Microsoft.VisualStudio.Services.ReleaseManagement.WebApi.Contracts;
using Microsoft.VisualStudio.Services.Pipeline.WebApi;
using Microsoft.VisualStudio.Services.Pipeline.WebApi.Clients;
using Microsoft.VisualStudio.Services.Pipeline.WebApi.Contracts;

namespace Agent.Worker.Release
{
    public class ReleaseServer
    {
        private Uri _projectCollectionUrl;
        private VssCredentials _credential;
        private Guid _projectId;

        private ReleaseHttpClient _releaseHttpClient { get; }
        private PipelineHttpClient _pipelineHttpClient { get; }

        public ReleaseServer(Uri projectCollection, VssCredentials credentials, Guid projectId)
        {
            ArgUtil.NotNull(projectCollection, nameof(projectCollection));
            ArgUtil.NotNull(credentials, nameof(credentials));

            _projectCollectionUrl = projectCollection;
            _credential = credentials;
            _projectId = projectId;

            _releaseHttpClient = new ReleaseHttpClient(projectCollection, credentials, new VssHttpRetryMessageHandler(3));
            _pipelineHttpClient = new PipelineHttpClient(projectCollection, credentials);
        }

        public IEnumerable<AgentArtifactDefinition> GetReleaseArtifactsFromService(int releaseId, CancellationToken cancellationToken = default(CancellationToken))
        {
            var artifacts = _releaseHttpClient.GetAgentArtifactDefinitionsAsync(_projectId, releaseId, cancellationToken: cancellationToken).Result;
            return artifacts;
        }

        public async Task<Microsoft.VisualStudio.Services.Pipeline.WebApi.Contracts.PipelineArtifact> AssociateArtifact(
        int releaseId,
        int releaseEnvironmentId,
        int deploymentId,
        string name,
        string type,
        string data,
        Dictionary<string, string> propertiesDictionary,
        CancellationToken cancellationToken = default(CancellationToken))
        {
            Microsoft.VisualStudio.Services.Pipeline.WebApi.Contracts.PipelineArtifact artifact = new Microsoft.VisualStudio.Services.Pipeline.WebApi.Contracts.PipelineArtifact()
            {
                Name = name,
                Resource = new Microsoft.VisualStudio.Services.Pipeline.WebApi.Contracts.ArtifactResource()
                {
                    Data = data,
                    Type = type,
                    Properties = propertiesDictionary
                }
            };

            return await _pipelineHttpClient.AddArtifactAsync(artifact, _projectId, releaseId, releaseEnvironmentId, deploymentId, cancellationToken);
        }
    }
}