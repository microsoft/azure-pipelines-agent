using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.WebApi;
using System;
using System.Collections.Generic;
using System.Text;
using Agent.Sdk;
using System.Threading;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;

namespace Agent.Plugins.PipelineArtifact
{
    public class ArtifactProviderFactory
    {
        private readonly FileContainerProvider fileContainerProvider;
        private readonly PipelineArtifactProvider pipelineArtifactProvider;

        public ArtifactProviderFactory(AgentTaskPluginExecutionContext context, VssConnection connection, CallbackAppTraceSource tracer )
        {
            pipelineArtifactProvider = new PipelineArtifactProvider(context, connection, tracer);
            fileContainerProvider = new FileContainerProvider(connection, tracer);
        }

        public IArtifactProvider GetProvider(BuildArtifact buildArtifact)
        {
            IArtifactProvider provider;
            if (buildArtifact.Resource.Type == PipelineArtifactServer.PipelineArtifactTypeName)
            {
                provider = pipelineArtifactProvider;
            }
            else if (buildArtifact.Resource.Type == PipelineArtifactServer.BuildArtifactTypeName)
            {
                provider = fileContainerProvider;
            }
            else
            {
                throw new InvalidOperationException($"{buildArtifact} is neither of type PipelineArtifact nor BuildArtifact");
            }
            return provider;
        }

    }
}
