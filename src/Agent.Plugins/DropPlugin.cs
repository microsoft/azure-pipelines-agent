using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agent.Sdk;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;
using Microsoft.VisualStudio.Services.BlobStore.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Agent.Plugins.Drop
{
    public abstract class ArtifactDropTaskPlugin : IAgentTaskPlugin
    {
        public abstract Guid Id { get; }
        public abstract string Version { get; }
        public abstract string Stage { get; }

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

            string localPath;
            if (!context.Inputs.TryGetValue(ArtifactEventProperties.TargetPath, out localPath) ||
                string.IsNullOrEmpty(localPath))
            {
                throw new Exception(StringUtil.Loc("ArtifactLocationRequired"));
            }

            PreprocessResult result = CheckAndTransformTargetPath(context, localPath, artifactName);
            if (result != null)
            {
                await ProcessCommandInternalAsync(context, projectId, buildId, artifactName, result, token);
            }
        }

        // Run checks and conversions before processing. Returns null if the operation should be aborted.
        protected abstract PreprocessResult CheckAndTransformTargetPath(
            AgentTaskPluginExecutionContext context, string localPath, string artifactName);

        // Process the command with preprocessed arguments.
        protected abstract Task ProcessCommandInternalAsync(
            AgentTaskPluginExecutionContext context, Guid projectId, int buildId, string artifactName, PreprocessResult result, CancellationToken token);
    }

    // To be invoked by PublishBuildArtifacts task
    public class PublishDropTask : ArtifactDropTaskPlugin
    {
        // Same as: https://github.com/Microsoft/vsts-tasks/blob/master/Tasks/PublishBuildArtifacts/task.json
        public override Guid Id => new Guid("2FF763A7-CE83-4E1F-BC89-0AE63477CEBE");

        public override string Version => "2.135.1";

        public override string Stage => "main";

        protected override PreprocessResult CheckAndTransformTargetPath(
            AgentTaskPluginExecutionContext context, string localPath, string artifactName)
        { 
            string hostType = context.Variables.GetValueOrDefault("system.hosttype")?.Value;
            if (!IsUncSharePath(context, localPath) && !string.Equals(hostType, "Build", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception(StringUtil.Loc("UploadArtifactCommandNotSupported", hostType ?? string.Empty));
            }

            string fullPath = Path.GetFullPath(localPath);

            bool isFile = File.Exists(fullPath);
            bool isDir = Directory.Exists(fullPath);
            if (!isFile && !isDir)
            {
                // if localPath is niether file nor folder
                throw new FileNotFoundException(StringUtil.Loc("PathNotExist", localPath));
            }
            else if (isDir && Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories).FirstOrDefault() == null)
            {
                // if localPath is a folder which contains nothing
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
            PreprocessResult preprocResult,
            CancellationToken token)
        { 
            // Upload to VSTS BlobStore, and associate the artifact with the build.
            context.Output($"Upload artifact: {preprocResult.FullPath} to server for build: {buildId} at backend.");
            BuildDropClient server = new BuildDropClient();
            await server.UploadDropArtifactAsync(context, context.VssConnection, projectId, buildId, artifactName, preprocResult.FullPath, token);
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

    // To be invoked by DownloadBuildArtifacts task
    public class DownloadDropTask : ArtifactDropTaskPlugin
    {
        // Same as https://github.com/Microsoft/vsts-tasks/blob/master/Tasks/DownloadBuildArtifacts/task.json
        public override Guid Id => new Guid("a433f589-fce1-4460-9ee6-44a624aeb1fb");

        public override string Version => "1.135.1";

        public override string Stage => "main";

        protected override PreprocessResult CheckAndTransformTargetPath(
            AgentTaskPluginExecutionContext context, string localPath, string artifactName)
        { 

            string fullPath = Path.GetFullPath(localPath);

            bool isDir = Directory.Exists(fullPath);
            if (!isDir)
            {
                Directory.CreateDirectory(fullPath);
            }

            PreprocessResult result = new PreprocessResult {
                FullPath = fullPath
            };

            if (context.Inputs.TryGetValue(ArtifactEventProperties.BuildId, out string buildId) && 
                UInt32.TryParse(buildId, out uint value) &&
                value != 0)
            {
                context.Output($"Download from the specified build: #{ value }");
                result.BuildId = value;
            }
            else
            {
                context.Output("Download from the current build.");
            }

            return result;
        }

        protected override async Task ProcessCommandInternalAsync(
            AgentTaskPluginExecutionContext context, 
            Guid projectId, 
            int buildId, 
            string artifactName, 
            PreprocessResult preprocResult,
            CancellationToken token)
        { 
            // Download from VSTS BlobStore.
            context.Output($"Download artifact to: {preprocResult.FullPath}");

            // Overwrite build id if specified by the user
            buildId = preprocResult.BuildId.HasValue ? (int)preprocResult.BuildId.Value : buildId;
            BuildDropClient server = new BuildDropClient();
            await server.DownloadDropArtifactAsync(context, context.VssConnection, projectId, buildId, artifactName, preprocResult.FullPath, token);
            context.Output($"Download artifact finished.");
        }
    }

    internal static class ArtifactEventProperties
    {
        // Properties set by plugins
        public static readonly string RootId = "RootId";
        public static readonly string ProofNodes = "ProofNodes";
        // Properties set by tasks
        public static readonly string ArtifactName = "artifactname";
        public static readonly string TargetPath = "targetpath";
        public static readonly string BuildId = "buildid";
    }

    public class PreprocessResult {
        internal string FullPath { get; set; }
        internal uint? BuildId { get; set; }
    }
}