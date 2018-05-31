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

namespace Agent.Plugins.Drop
{
    // These commands will eventually replace ArtifactUploadCommand in DropPlugin.cs
    public abstract class DropArtifactCommand : IAgentCommandPlugin
    {
        public string Area => "artifact";

        public abstract string Event { get; }

        public abstract string DisplayName { get; }

        public async Task ProcessCommandAsync(AgentCommandPluginExecutionContext context, CancellationToken token)
        {
            PluginUtil.NotNull(context, nameof(context));

            Guid projectId = new Guid(context.Variables.GetValueOrDefault(BuildVariables.TeamProjectId)?.Value ?? Guid.Empty.ToString());
            PluginUtil.NotEmpty(projectId, nameof(projectId));

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
            if (!context.Properties.TryGetValue(ArtifactEventProperties.ArtifactName, out artifactName) ||
                string.IsNullOrEmpty(artifactName))
            {
                throw new Exception(PluginUtil.Loc("ArtifactNameRequired"));
            }

            var propertyDictionary = ExtractArtifactProperties(context.Properties);

            string localPath = context.Data;
            if (context.ContainerPathMappings.Count > 0)
            {
                // Translate file path back from container path
                localPath = context.TranslateContainerPathToHostPath(localPath);
            }

            if (string.IsNullOrEmpty(localPath))
            {
                throw new Exception(PluginUtil.Loc("ArtifactLocationRequired"));
            }

            PreprocessResult result = CheckAndTrasformTargetPath(context, localPath, propertyDictionary, artifactName);
            if (result != null)
            {
                await ProcessCommandInternalAsync(context, projectId, buildId, artifactName, propertyDictionary, result, token);
            }
        }

        // Run checks and conversions before processing. Returns null if the operation should be aborted.
        protected abstract PreprocessResult CheckAndTrasformTargetPath(
            AgentCommandPluginExecutionContext context, string localPath, Dictionary<string, string> propertyDict, string artifactName);

        // Process the command with preprocessed arguments.
        protected abstract Task ProcessCommandInternalAsync(
            AgentCommandPluginExecutionContext context, Guid projectId, int buildId, string artifactName, Dictionary<string, string> propertyDict, PreprocessResult result, CancellationToken token);

        private Dictionary<string, string> ExtractArtifactProperties(Dictionary<string, string> eventProperties)
        {
            return eventProperties.Where(pair => !(string.Compare(pair.Key, ArtifactEventProperties.ContainerFolder, StringComparison.OrdinalIgnoreCase) == 0 ||
                                                  string.Compare(pair.Key, ArtifactEventProperties.ArtifactName, StringComparison.OrdinalIgnoreCase) == 0 ||
                                                  string.Compare(pair.Key, ArtifactEventProperties.ArtifactType, StringComparison.OrdinalIgnoreCase) == 0)).ToDictionary(pair => pair.Key, pair => pair.Value);
        }
    }

    public class DropArtifactUploadCommand : DropArtifactCommand
    {
        public override string Event => "upload";

        public override string DisplayName => PluginUtil.Loc("UploadArtifact");

        protected override PreprocessResult CheckAndTrasformTargetPath(
            AgentCommandPluginExecutionContext context, string localPath, Dictionary<string, string> propertyDict, string artifactName)
        { 
            string hostType = context.Variables.GetValueOrDefault("system.hosttype")?.Value;
            if (!IsUncSharePath(context, localPath) && !string.Equals(hostType, "Build", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception(PluginUtil.Loc("UploadArtifactCommandNotSupported", hostType ?? string.Empty));
            }

            string fullPath = Path.GetFullPath(localPath);
            if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
            {
                // if localPath is not a file or folder on disk
                throw new FileNotFoundException(PluginUtil.Loc("PathNotExist", localPath));
            }
            else if (Directory.Exists(fullPath) && Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories).FirstOrDefault() == null)
            {
                // if localPath is a folder but the folder contains nothing
                context.Output(PluginUtil.Loc("DirectoryIsEmptyForArtifact", fullPath, artifactName));
                return null;
            }

            return new PreprocessResult {
                FullPath = fullPath
            };
        }

        protected override async Task ProcessCommandInternalAsync(
            AgentCommandPluginExecutionContext context, 
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

        private Boolean IsUncSharePath(AgentCommandPluginExecutionContext context, string path)
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

    public class DropArtifactDownloadCommand : DropArtifactCommand
    {
        public override string Event => "download";

        public override string DisplayName => PluginUtil.Loc("DownloadArtifact");

        protected override PreprocessResult CheckAndTrasformTargetPath(AgentCommandPluginExecutionContext context, string localPath, Dictionary<string, string> propertyDict, string artifactName)
        { 
            string fullPath = Path.GetFullPath(localPath);

            string manifestId;
            if (!propertyDict.TryGetValue(ArtifactEventProperties.ManifestId, out manifestId) ||
                string.IsNullOrEmpty(manifestId))
            {
                throw new Exception(PluginUtil.Loc("ManifestIdRequired"));
            }

            return new PreprocessResult {
                FullPath = fullPath,
                ExtraData = manifestId
            };
        }

        protected override async Task ProcessCommandInternalAsync(
            AgentCommandPluginExecutionContext context, 
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
            context.Output(PluginUtil.Loc("DownloadFromDrop", preprocResult.FullPath));
        }
    }

    internal static class ArtifactEventProperties
    {
        public static readonly string ContainerFolder = "containerfolder";
        public static readonly string ArtifactName = "artifactname";
        public static readonly string ArtifactType = "artifacttype";
        public static readonly string Browsable = "Browsable";
        public static readonly string ManifestId = "manifestid";
        public static readonly string ItemPattern = "itempattern";
    }

    public class PreprocessResult {
        internal string FullPath { get; set; }
        internal object ExtraData { get; set; }
    }
}
