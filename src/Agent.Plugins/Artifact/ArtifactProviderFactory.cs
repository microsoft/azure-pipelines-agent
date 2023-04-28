// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Agent.Sdk;
using Agent.Plugins.PipelineArtifact;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;
using Microsoft.VisualStudio.Services.Agent.Blob;

namespace Agent.Plugins
{
    internal class ArtifactProviderFactory
    {
        private AgentTaskPluginExecutionContext _context;
        private VssConnection _connection;
        private IAppTraceSource _tracer;

        private FileContainerProvider fileContainerProvider;
        private PipelineArtifactProvider pipelineArtifactProvider;
        private FileShareProvider fileShareProvider;

        public ArtifactProviderFactory(AgentTaskPluginExecutionContext context, VssConnection connection, IAppTraceSource tracer)
        {
            this._connection = connection;
            this._context = context;
            this._tracer = tracer;
        }

        public IArtifactProvider GetProvider(BuildArtifact buildArtifact)
        {
            string artifactType = buildArtifact.Resource.Type;
            if (PipelineArtifactConstants.PipelineArtifact.Equals(artifactType, StringComparison.CurrentCultureIgnoreCase))
            {
                return pipelineArtifactProvider ??= new PipelineArtifactProvider(this._context, this._connection, this._tracer);
            }
            else if (PipelineArtifactConstants.Container.Equals(artifactType, StringComparison.CurrentCultureIgnoreCase))
            {
                return fileContainerProvider ??= new FileContainerProvider(this._connection, this._tracer);
            }
            else if (PipelineArtifactConstants.FileShareArtifact.Equals(artifactType, StringComparison.CurrentCultureIgnoreCase))
            {
                if (fileShareProvider is null)
                {
                    fileShareProvider = new FileShareProvider(this._context, this._connection, this._tracer, DedupManifestArtifactClientFactory.Instance);
                }
                return fileShareProvider;
            }
            else
            {
                throw new InvalidOperationException($"{buildArtifact} is not of type PipelineArtifact, FileShare or BuildArtifact");
            }
        }
    }
}
