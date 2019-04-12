using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.WebApi;
using System;
using System.Collections.Generic;
using System.Text;
using Agent.Sdk;
using System.Threading;

namespace Agent.Plugins.PipelineArtifact
{
    public class ArtifactProviderFactory
    {
        private readonly FileContainerProvider fileContainerProvider;
        private readonly PipelineArtifactProvider pipelineArtifactProvider;

        public ArtifactProviderFactory(AgentTaskPluginExecutionContext context, VssConnection connection)
        {
            pipelineArtifactProvider = new PipelineArtifactProvider(context, connection);
            fileContainerProvider = new FileContainerProvider(connection);
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
                provider = null;
                Console.WriteLine("BAD EXCEPTION!!!!");
            }
            return provider;
        }

    }
}
