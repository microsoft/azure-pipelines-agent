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
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Agent.Sdk;

namespace Agent.Plugins.BuildDrop
{
    public abstract class BuildDropTaskPluginBase : IAgentTaskPlugin
    {
        public abstract Guid Id { get; }
        public string Version => "0.138.0"; // Publish and Download tasks will be always on the same version.
        public string Stage => "main";

        public async Task RunAsync(AgentTaskPluginExecutionContext context, CancellationToken token)
        {
            ArgUtil.NotNull(context, nameof(context));

            // Project ID
            Guid projectId = new Guid(context.Variables.GetValueOrDefault(BuildVariables.TeamProjectId)?.Value ?? Guid.Empty.ToString());
            ArgUtil.NotEmpty(projectId, nameof(projectId));

            // Artifact Name
            string artifactName;
            if (!context.Inputs.TryGetValue(ArtifactEventProperties.ArtifactName, out artifactName) ||
                string.IsNullOrEmpty(artifactName))
            {
                throw new Exception(StringUtil.Loc("ArtifactNameRequired"));
            }

            // Path
            string targetPath;
            if (!context.Inputs.TryGetValue(ArtifactEventProperties.TargetPath, out targetPath) ||
                string.IsNullOrEmpty(targetPath))
            {
                throw new Exception(StringUtil.Loc("ArtifactLocationRequired"));
            }

            await ProcessCommandInternalAsync(context, targetPath, projectId, artifactName, token);
        }

        // Process the command with preprocessed arguments.
        protected abstract Task ProcessCommandInternalAsync(
            AgentTaskPluginExecutionContext context, 
            string targetPath, 
            Guid projectId,
            string artifactName, 
            CancellationToken token);
    }

    // Caller: PublishBuildDrop task
    // Can be invoked from a build run or a release run should a build be set as the artifact. 
    public class PublishBuildDropTask : BuildDropTaskPluginBase
    {
        // Same as: https://github.com/Microsoft/vsts-tasks/blob/master/Tasks/PublishBuildDropV0/task.json
        public override Guid Id => new Guid("ECDC45F6-832D-4AD9-B52B-EE49E94659BE");

        protected override async Task ProcessCommandInternalAsync(
            AgentTaskPluginExecutionContext context, 
            string targetPath, 
            Guid projectId,
            string artifactName,
            CancellationToken token)
        {
            // Build ID - support publishing in a build run or a release run should the build drop be the artifact.
            string buildIdStr = context.Variables.GetValueOrDefault(BuildVariables.BuildId)?.Value ?? string.Empty;
            if (!int.TryParse(buildIdStr, out int buildId))
            {
                string hostType = context.Variables.GetValueOrDefault("system.hosttype")?.Value; 
                if (string.Equals(hostType, "Release", StringComparison.OrdinalIgnoreCase)) {
                    throw new InvalidOperationException(
                        $"Trying to upload build drop in '{ hostType ?? string.Empty }' environment but build id is not present. " + 
                        $"Can only upload to a build drop from '{ hostType ?? string.Empty }' environment if the artifact is a build."); 
                } else if (!string.Equals(hostType, "Build", StringComparison.OrdinalIgnoreCase)) {
                    throw new InvalidOperationException(
                        $"Cannot upload to a build drop from '{ hostType ?? string.Empty }' environment."); 
                } else {
                    // This should not happen since the build id comes from build environment. But a user may override that so we must be careful.
                    throw new ArgumentException($"Build Id is not valid: #{ buildIdStr }");
                }
            }

            string fullPath = Path.GetFullPath(targetPath);
            bool isFile = File.Exists(fullPath);
            bool isDir = Directory.Exists(fullPath);
            if (!isFile && !isDir)
            {
                // if local path is neither file nor folder
                throw new FileNotFoundException(StringUtil.Loc("PathNotExist", targetPath));
            }
            else if (isDir && Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories).FirstOrDefault() == null)
            {
                // if local path is a folder which contains nothing
                throw new ArgumentException(StringUtil.Loc("DirectoryIsEmptyForArtifact", fullPath, artifactName));
            }

            // Upload to VSTS BlobStore, and associate the artifact with the build.
            context.Output($"Uploading drop from { fullPath } for build: { buildId }");
            BuildDropServer server = new BuildDropServer();
            await server.UploadDropArtifactAsync(context, projectId, buildId, artifactName, fullPath, token);
            context.Output($"Finished drop uploading.");
        }
    }

    // CAller: DownloadBuildDrop task
    // Can be invoked from a build run or a release run should a build be set as the artifact. 
    public class DownloadBuildDropTask : BuildDropTaskPluginBase
    {
        // Same as https://github.com/Microsoft/vsts-tasks/blob/master/Tasks/DownloadBuildDropV0/task.json
        public override Guid Id => new Guid("61F2A582-95AE-4948-B34D-A1B3C4F6A737");

        protected override async Task ProcessCommandInternalAsync(
            AgentTaskPluginExecutionContext context, 
            string targetPath, 
            Guid projectId,
            string artifactName,
            CancellationToken token)
        {
            // Create target directory if absent
            string fullPath = Path.GetFullPath(targetPath);
            bool isDir = Directory.Exists(fullPath);
            if (!isDir)
            {
                Directory.CreateDirectory(fullPath);
            }

            // Build ID
            int buildId = 0;
            string buildIdStr = string.Empty;
            // Determine the build id
            if (context.Inputs.TryGetValue(ArtifactEventProperties.BuildId, out buildIdStr) && 
                Int32.TryParse(buildIdStr, out buildId) &&
                buildId != 0)
            {
                // A) Build Id provided by user input
                context.Output($"Download from the specified build: #{ buildId }");
            }
            else
            {
                // B) Build Id provided by environment
                buildIdStr = context.Variables.GetValueOrDefault(BuildVariables.BuildId)?.Value ?? string.Empty;
                if (int.TryParse(buildIdStr, out buildId) &&
                    buildId != 0)
                {
                    context.Output($"Download from the current build: #{ buildId }");
                }
                else
                {
                    string hostType = context.Variables.GetValueOrDefault("system.hosttype")?.Value; 
                    if (string.Equals(hostType, "Release", StringComparison.OrdinalIgnoreCase)) {
                        throw new InvalidOperationException(
                            $"Trying to download build drop in '{ hostType ?? string.Empty }' environment but build id is not present. " + 
                            $"Can only download a build drop in '{ hostType ?? string.Empty }' environment if the artifact is a build."); 
                    } else if (!string.Equals(hostType, "Build", StringComparison.OrdinalIgnoreCase)) {
                        throw new InvalidOperationException(
                            $"Cannot download a build drop from '{ hostType ?? string.Empty }' environment."); 
                    } else {
                        // This should not happen since the build id comes from build environment. But a user may override that so we must be careful.
                        throw new ArgumentException($"Build Id is not valid: #{ buildIdStr }");
                    }
                }
            }

            // Download from VSTS BlobStore.
            context.Output($"Download artifact to: { targetPath }");

            // Overwrite build id if specified by the user
            BuildDropServer server = new BuildDropServer();
            await server.DownloadDropArtifactAsync(context, projectId, buildId, artifactName, targetPath, token);
            context.Output($"Download artifact finished.");
        }
    }

    // Properties set by tasks
    internal static class ArtifactEventProperties
    {
        public static readonly string ArtifactName = "artifactname";
        public static readonly string TargetPath = "targetpath";
        public static readonly string BuildId = "buildid";
    }
}