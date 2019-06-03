using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using Microsoft.VisualStudio.Services.BlobStore.WebApi;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;
using Microsoft.VisualStudio.Services.WebApi;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Agent.Sdk;
using System.Threading;
using System.Linq;
using Microsoft.TeamFoundation.DistributedTask.WebApi;

namespace Agent.Plugins.PipelineArtifact
{
    internal class PipelineArtifactProvider : IArtifactProvider
    {
        private const int DefaultDedupStoreClientMaxParallelism = 192;

        internal static int GetDedupStoreClientMaxParallelism(AgentTaskPluginExecutionContext context) {
            int parallelism = DefaultDedupStoreClientMaxParallelism;
            if(context.Variables.TryGetValue("AZURE_PIPELINES_DEDUP_PARALLELISM", out VariableValue v)) {
                parallelism = int.Parse(v.Value);
            }
            context.Info(string.Format("Dedup parallelism: {0}", parallelism));
            return parallelism;
        } 

        private readonly BuildDropManager buildDropManager;
        private readonly CallbackAppTraceSource tracer;

        public PipelineArtifactProvider(AgentTaskPluginExecutionContext context, VssConnection connection, CallbackAppTraceSource tracer)
        {
            var dedupStoreHttpClient = connection.GetClient<DedupStoreHttpClient>();
            this.tracer = tracer;
            dedupStoreHttpClient.SetTracer(tracer);
            int parallelism = GetDedupStoreClientMaxParallelism(context);
            tracer.Info("PipelineArtifactProvider using parallelism {0}.", parallelism);
            var client = new DedupStoreClientWithDataport(dedupStoreHttpClient, parallelism);
            buildDropManager = new BuildDropManager(client, this.tracer);
        }

        public async Task DownloadSingleArtifactAsync(PipelineArtifactDownloadParameters downloadParameters, BuildArtifact buildArtifact, CancellationToken cancellationToken)
        {
            var manifestId = DedupIdentifier.Create(buildArtifact.Resource.Data);
            var options = DownloadDedupManifestArtifactOptions.CreateWithManifestId(
                manifestId,
                downloadParameters.TargetDirectory,
                proxyUri: null,
                minimatchPatterns: downloadParameters.MinimatchFilters);
            await buildDropManager.DownloadAsync(options, cancellationToken);
        }

        public async Task DownloadMultipleArtifactsAsync(PipelineArtifactDownloadParameters downloadParameters, IEnumerable<BuildArtifact> buildArtifacts, CancellationToken cancellationToken)
        {
            var artifactNameAndManifestIds = buildArtifacts.ToDictionary(
                keySelector: (a) => a.Name, // keys should be unique, if not something is really wrong
                elementSelector: (a) => DedupIdentifier.Create(a.Resource.Data));
            // 2) download to the target path
            var options = DownloadDedupManifestArtifactOptions.CreateWithMultiManifestIds(
                artifactNameAndManifestIds,
                downloadParameters.TargetDirectory,
                proxyUri: null,
                minimatchPatterns: downloadParameters.MinimatchFilters,
                minimatchFilterWithArtifactName: downloadParameters.MinimatchFilterWithArtifactName);
            await buildDropManager.DownloadAsync(options, cancellationToken);
        }
    }
}
