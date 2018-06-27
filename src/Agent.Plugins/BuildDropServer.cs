using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;
using Microsoft.VisualStudio.Services.BlobStore.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agent.Sdk;
using Newtonsoft.Json;

namespace Agent.Plugins.Drop
{
    public class DebugHelper
    {
        public static void Output(ITraceWriter writer, string msg)
        {
            Console.WriteLine(msg);
            if (writer != null) {
                writer.Info(msg);
            }
        }
        public static void WaitForDebugger(ITraceWriter writer = null)
        {
            int remaining = 120;
            while (remaining > 0) {
                if (remaining % 5 == 0 ) 
                {
                    Output(writer, $"Waiting for attaching... ({remaining})");
                }
                Thread.Sleep(TimeSpan.FromSeconds(1));
                remaining--;
            }

            Output(writer, $"Done waiting. Continue...");
        }
    }
    
    // A wrapper of BuildDropManager, providing basic functionalities such as uploading and downloading drop artifacts.
    public class BuildDropServer
    {
        // Command plugin model
        // Upload drop artifact to VSTS BlobStore service through BuildDropManager
        internal async Task UploadDropArtifactAsync(
            AgentCommandPluginExecutionContext context,
            VssConnection connection,
            Guid projectId,
            int buildId,
            string name,
            Dictionary<string, string> propertiesDictionary,
            string source,
            CancellationToken cancellationToken)
        {
            var httpclient = connection.GetClient<DedupStoreHttpClient>();
            var tracer = new CallbackAppTraceSource(str => context.Output(str), System.Diagnostics.SourceLevels.Information);
            httpclient.SetTracer(tracer);
            var client = new DedupStoreClientWithDataport(httpclient, 16 * Environment.ProcessorCount);

            var buildDropManager = new BuildDropManager(client, tracer);
            var result = await buildDropManager.PublishAsync(source, cancellationToken);
            context.Output(StringUtil.Loc("UploadToDrop", source));

            BuildServer buildHelper = new BuildServer(connection);
            propertiesDictionary.Add("RootId", result.RootId.ValueString);
            propertiesDictionary.Add("ProofNodes", JsonConvert.SerializeObject(result.ProofNodes.ToArray()));
            var artifact = await buildHelper.AssociateArtifact(projectId, buildId, name, ArtifactResourceTypes.Drop, result.ManifestId.ValueString, propertiesDictionary, cancellationToken);
            context.Output(StringUtil.Loc("AssociateArtifactWithBuild", artifact.Id, buildId));
        }

        // Download drop artifact from VSTS BlobStore service through BuildDropManager to a target path
        internal async Task DownloadDropArtifactAsync(
            AgentCommandPluginExecutionContext context,
            VssConnection connection,
            Guid projectId,
            string manifestId,
            string targetDir,
            CancellationToken cancellationToken)
        {
            var httpclient = connection.GetClient<DedupStoreHttpClient>();
            var tracer = new CallbackAppTraceSource(str => context.Output(str), System.Diagnostics.SourceLevels.Information);
            httpclient.SetTracer(tracer);
            var client = new DedupStoreClientWithDataport(httpclient, 16 * Environment.ProcessorCount);

            var buildDropManager = new BuildDropManager(client, tracer);
            await buildDropManager.DownloadAsync(DedupIdentifier.Create(manifestId), targetDir, cancellationToken);
            context.Output(StringUtil.Loc("DownloadFromDrop", targetDir));
        }

        // Task plugin model
        // Upload drop artifact to VSTS BlobStore service through BuildDropManager
        internal async Task UploadDropArtifactAsync(
            AgentTaskPluginExecutionContext context,
            VssConnection connection,
            Guid projectId,
            int buildId,
            string name,
            Dictionary<string, string> propertiesDictionary,
            string source,
            CancellationToken cancellationToken)
        {
            DebugHelper.WaitForDebugger(context);

            var httpclient = connection.GetClient<DedupStoreHttpClient>();
            var tracer = new CallbackAppTraceSource(str => context.Output(str), System.Diagnostics.SourceLevels.Information);
            httpclient.SetTracer(tracer);
            var client = new DedupStoreClientWithDataport(httpclient, 16 * Environment.ProcessorCount);

            var buildDropManager = new BuildDropManager(client, tracer);
            var result = await buildDropManager.PublishAsync(source, cancellationToken);
            context.Output(StringUtil.Loc("UploadToDrop", source));

            BuildServer buildHelper = new BuildServer(connection);
            propertiesDictionary.Add("RootId", result.RootId.ValueString);
            propertiesDictionary.Add("ProofNodes", JsonConvert.SerializeObject(result.ProofNodes.ToArray()));
            var artifact = await buildHelper.AssociateArtifact(projectId, buildId, name, ArtifactResourceTypes.Drop, result.ManifestId.ValueString, propertiesDictionary, cancellationToken);
            context.Output(StringUtil.Loc("AssociateArtifactWithBuild", artifact.Id, buildId));
        }

        // Download drop artifact from VSTS BlobStore service through BuildDropManager to a target path
        internal async Task DownloadDropArtifactAsync(
            AgentTaskPluginExecutionContext context,
            VssConnection connection,
            Guid projectId,
            string manifestId,
            string targetDir,
            CancellationToken cancellationToken)
        {
            var httpclient = connection.GetClient<DedupStoreHttpClient>();
            var tracer = new CallbackAppTraceSource(str => context.Output(str), System.Diagnostics.SourceLevels.Information);
            httpclient.SetTracer(tracer);
            var client = new DedupStoreClientWithDataport(httpclient, 16 * Environment.ProcessorCount);

            var buildDropManager = new BuildDropManager(client, tracer);
            await buildDropManager.DownloadAsync(DedupIdentifier.Create(manifestId), targetDir, cancellationToken);
            context.Output(StringUtil.Loc("DownloadFromDrop", targetDir));
        }
    }
}