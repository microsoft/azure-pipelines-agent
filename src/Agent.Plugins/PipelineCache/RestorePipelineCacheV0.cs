using System.Threading;
using System.Threading.Tasks;
using Agent.Sdk;
using Microsoft.VisualStudio.Services.PipelineCache.WebApi;

namespace Agent.Plugins.PipelineCache
{    
    public class RestorePipelineCacheV0 : PipelineCacheTaskPluginBase
    {
        public override string Stage => "main";

        protected override async Task ProcessCommandInternalAsync(
            AgentTaskPluginExecutionContext context,
            Fingerprint[] fingerprints,
            string path,
            CancellationToken token)
        {
            var server = new PipelineCacheServer();
            await server.DownloadAsync(
                context, 
                fingerprints,
                path,
                context.GetInput(PipelineCacheTaskPluginConstants.CacheHitVariable, required: false),
                token);
        }
    }
}