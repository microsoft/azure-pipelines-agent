using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;
using Microsoft.VisualStudio.Services.BlobStore.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Agent.Sdk;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Agent.Plugins.Drop
{
    public abstract class ArtifactDropTaskPlugin : IAgentTaskPlugin
    {
        public virtual Guid Id { get; }
        public virtual string Version { get; }
        public virtual string Stage { get; }

        public async Task RunAsync(AgentTaskPluginExecutionContext context, CancellationToken token)
        {
            ArgUtil.NotNull(context, nameof(context));

            Guid projectId = new Guid(context.Variables.GetValueOrDefault(BuildVariables.TeamProjectId)?.Value ?? Guid.Empty.ToString());
            ArgUtil.NotEmpty(projectId, nameof(projectId));

            string buildIdStr = context.Variables.GetValueOrDefault(BuildVariables.BuildId)?.Value ?? string.Empty;
            if (!int.TryParse(buildIdStr, out int buildId))
            {
                throw new ArgumentOutOfRangeException(buildIdStr);
            }

            string containerIdStr = context.Variables.GetValueOrDefault(BuildVariables.ContainerId)?.Value ?? string.Empty;
            if (!long.TryParse(containerIdStr, out long containerId))
            {
                throw new ArgumentOutOfRangeException(buildIdStr);
            }

            string artifactName;
            if (!context.Inputs.TryGetValue(ArtifactEventProperties.ArtifactName, out artifactName) ||
                string.IsNullOrEmpty(artifactName))
            {
                throw new Exception(StringUtil.Loc("ArtifactNameRequired"));
            }

            var propertyDictionary = ExtractArtifactProperties(context.Inputs);

            string localPath;
            if (!context.Inputs.TryGetValue(ArtifactEventProperties.TargetPath, out localPath) ||
                string.IsNullOrEmpty(localPath))
            {
                throw new Exception(StringUtil.Loc("ArtifactLocationRequired"));
            }

            //string localPath = context.Data;
            // if (context.ContainerPathMappings.Count > 0)
            // {
            //     // Translate file path back from container path
            //     localPath = context.TranslateContainerPathToHostPath(localPath);
            // }

            // if (string.IsNullOrEmpty(localPath))
            // {
            //     throw new Exception(StringUtil.Loc("ArtifactLocationRequired"));
            // }

            PreprocessResult result = CheckAndTrasformTargetPath(context, localPath, propertyDictionary, artifactName);
            if (result != null)
            {
                await ProcessCommandInternalAsync(context, projectId, buildId, artifactName, propertyDictionary, result, token);
            }
        }

        // Run checks and conversions before processing. Returns null if the operation should be aborted.
        protected abstract PreprocessResult CheckAndTrasformTargetPath(
            AgentTaskPluginExecutionContext context, string localPath, Dictionary<string, string> propertyDict, string artifactName);

        // Process the command with preprocessed arguments.
        protected abstract Task ProcessCommandInternalAsync(
            AgentTaskPluginExecutionContext context, Guid projectId, int buildId, string artifactName, Dictionary<string, string> propertyDict, PreprocessResult result, CancellationToken token);

        private Dictionary<string, string> ExtractArtifactProperties(Dictionary<string, string> eventProperties)
        {
            return eventProperties.Where(pair => !(string.Compare(pair.Key, ArtifactEventProperties.ContainerFolder, StringComparison.OrdinalIgnoreCase) == 0 ||
                                                  string.Compare(pair.Key, ArtifactEventProperties.ArtifactName, StringComparison.OrdinalIgnoreCase) == 0 ||
                                                  string.Compare(pair.Key, ArtifactEventProperties.ArtifactType, StringComparison.OrdinalIgnoreCase) == 0)).ToDictionary(pair => pair.Key, pair => pair.Value);
        }

        // protected VssConnection GetConnection(AgentTaskPluginExecutionContext context)
        // {
        //     ServiceEndpoint systemConnection = context.Endpoints.FirstOrDefault(
        //         e => string.Equals(e.Name, WellKnownServiceEndpointNames.SystemVssConnection, StringComparison.OrdinalIgnoreCase));
        //     ArgUtil.NotNull(systemConnection, nameof(systemConnection));
        //     ArgUtil.NotNull(systemConnection.Url, nameof(systemConnection.Url));

        //     VssCredentials credentials = ArgUtil.GetVssCredential(systemConnection);
        //     ArgUtil.NotNull(credentials, nameof(credentials));
        //     return ArgUtil.CreateConnection(systemConnection.Url, credentials);
        // }
    }

    public class PublishDropTask : ArtifactDropTaskPlugin
    {
        // Same as: https://github.com/Microsoft/vsts-tasks/blob/master/Tasks/PublishBuildArtifacts/task.json
        // You can change the guid if you decide to create a new task.
        public override Guid Id => new Guid("2FF763A7-CE83-4E1F-BC89-0AE63477CEBE");

        // 2.x preview or any version make sense to you.
        public override string Version => "2.135.1";

        public override string Stage => "main";

        protected override PreprocessResult CheckAndTrasformTargetPath(
            AgentTaskPluginExecutionContext context, string localPath, Dictionary<string, string> propertyDict, string artifactName)
        { 
            string hostType = context.Variables.GetValueOrDefault("system.hosttype")?.Value;
            if (!IsUncSharePath(context, localPath) && !string.Equals(hostType, "Build", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception(StringUtil.Loc("UploadArtifactCommandNotSupported", hostType ?? string.Empty));
            }

            string fullPath = Path.GetFullPath(localPath);
            if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
            {
                // if localPath is not a file or folder on disk
                throw new FileNotFoundException(StringUtil.Loc("PathNotExist", localPath));
            }
            else if (Directory.Exists(fullPath) && Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories).FirstOrDefault() == null)
            {
                // if localPath is a folder but the folder contains nothing
                context.Output(StringUtil.Loc("DirectoryIsEmptyForArtifact", fullPath, artifactName));
                return null;
            }

            return new PreprocessResult {
                FullPath = fullPath
            };
        }

        protected override async Task ProcessCommandInternalAsync(
            AgentTaskPluginExecutionContext context, 
            Guid projectId, 
            int buildId, 
            string artifactName, 
            Dictionary<string, string> propertyDict, 
            PreprocessResult preprocResult,
            CancellationToken token)
        { 
            // Upload to VSTS BlobStore, and associate the artifact with the build.
            context.Output($"Upload artifact: {preprocResult.FullPath} to server for build: {buildId} at backend.");
            BuildDropServer server = new BuildDropServer();
            await server.UploadDropArtifactAsync(context, context.VssConnection, projectId, buildId, artifactName, propertyDict, preprocResult.FullPath, token);
            context.Output($"Upload artifact finished.");
        }

        private Boolean IsUncSharePath(AgentTaskPluginExecutionContext context, string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            Uri uri;
            // Add try catch to avoid unexpected throw from Uri.Property.
            try
            {
                if (Uri.TryCreate(path, UriKind.RelativeOrAbsolute, out uri))
                {
                    if (uri.IsAbsoluteUri && uri.IsUnc)
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                context.Debug($"Can't determine path: {path} is UNC or not.");
                context.Debug(ex.ToString());
                return false;
            }

            return false;
        }
    }

    public class DownloadDropTask : ArtifactDropTaskPlugin
    {
        // Same as: https://github.com/Microsoft/vsts-tasks/blob/master/Tasks/DownloadBuildArtifacts/task.json
        // You can change the guid if you decide to create a new task.
        public override Guid Id => new Guid("a433f589-fce1-4460-9ee6-44a624aeb1fb");

        // 1.x preview or any version make sense to you.
        public override string Version => "1.135.0";

        public override string Stage => "main";

        protected override PreprocessResult CheckAndTrasformTargetPath(
            AgentTaskPluginExecutionContext context, string localPath, Dictionary<string, string> propertyDict, string artifactName)
        { 
            string fullPath = Path.GetFullPath(localPath);

            string manifestId;
            if (!propertyDict.TryGetValue(ArtifactEventProperties.ManifestId, out manifestId) ||
                string.IsNullOrEmpty(manifestId))
            {
                throw new Exception(StringUtil.Loc("ManifestIdRequired"));
            }

            return new PreprocessResult {
                FullPath = fullPath,
                ExtraData = manifestId
            };
        }

        protected override async Task ProcessCommandInternalAsync(
            AgentTaskPluginExecutionContext context, 
            Guid projectId, 
            int buildId, 
            string artifactName, 
            Dictionary<string, string> propertyDict, 
            PreprocessResult preprocResult,
            CancellationToken token)
        { 
            // Download from VSTS BlobStore.
            var httpclient = context.VssConnection.GetClient<DedupStoreHttpClient>();
            var tracer = new CallbackAppTraceSource(str => context.Output(str), System.Diagnostics.SourceLevels.Information);
            httpclient.SetTracer(tracer);
            var client = new DedupStoreClientWithDataport(httpclient, 16 * Environment.ProcessorCount);

            var buildDropManager = new BuildDropManager(client, tracer);
            var manifestId = DedupIdentifier.Create((string)preprocResult.ExtraData);
            await buildDropManager.DownloadAsync(manifestId, preprocResult.FullPath, token);
            context.Output(StringUtil.Loc("DownloadFromDrop", preprocResult.FullPath));
        }
    }

}