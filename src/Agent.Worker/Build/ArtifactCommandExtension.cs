using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agent.Worker.Release;
using Microsoft.VisualStudio.Services.WebApi;

namespace Microsoft.VisualStudio.Services.Agent.Worker.Build
{
    public interface IArtifactCommands
    {
        Task AssociateArtifactAsync(
            IExecutionContext context,
            IAsyncCommandContext commandContext,
            VssConnection connection,
            Guid projectId,
            string name,
            string type,
            string data,
            Dictionary<string, string> propertiesDictionary,
            CancellationToken cancellationToken);

        Task UploadArtifactAsync(
            IExecutionContext context,
            IAsyncCommandContext commandContext,
            VssConnection connection,
            Guid projectId,
            string containerPath,
            string name,
            Dictionary<string, string> propertiesDictionary,
            string source,
            CancellationToken cancellationToken);
    }

    public class ReleaseArtifactCommands : IArtifactCommands
    {
        public async Task AssociateArtifactAsync(
            IExecutionContext context,
            IAsyncCommandContext commandContext,
            VssConnection connection,
            Guid projectId,
            string name,
            string type,
            string data,
            Dictionary<string, string> propertiesDictionary,
            CancellationToken cancellationToken)
        {
            int? releaseId = context.Variables.Release_ReleaseId;
            ArgUtil.NotNull(releaseId, nameof(releaseId));

            int? releaseEnvironmentId = context.Variables.Release_ReleaseEnvironmentId;
            ArgUtil.NotNull(releaseId, nameof(releaseEnvironmentId));

            int? attempt = context.Variables.Release_Attempt;
            ArgUtil.NotNull(releaseId, nameof(attempt));

            long? containerId = context.Variables.Release_ContainerId;
            ArgUtil.NotNull(containerId, nameof(containerId));

            ServiceEndpoint vssEndpoint = context.Endpoints.FirstOrDefault(e => string.Equals(e.Name, ServiceEndpoints.SystemVssConnection, StringComparison.OrdinalIgnoreCase));
            ArgUtil.NotNull(vssEndpoint, nameof(vssEndpoint));
            ArgUtil.NotNull(vssEndpoint.Url, nameof(vssEndpoint.Url));

            context.Debug($"Connecting to {vssEndpoint.Url}/{projectId}");
            var releaseServer = new ReleaseServer(vssEndpoint.Url, ApiUtil.GetVssCredential(vssEndpoint), projectId);
            var artifact = await releaseServer.AssociateArtifact(releaseId.Value, releaseEnvironmentId.Value, Convert.ToInt32(context.Variables.Release_DeploymentId), name, type, data, propertiesDictionary, cancellationToken);
            context.Output(StringUtil.Loc("AssociateArtifactWithRelease", artifact.Id, releaseId));
        }

        public async Task UploadArtifactAsync(
            IExecutionContext context,
            IAsyncCommandContext commandContext,
            VssConnection connection,
            Guid projectId,
            string containerPath,
            string name,
            Dictionary<string, string> propertiesDictionary,
            string source,
            CancellationToken cancellationToken)
        {
            int? releaseId = context.Variables.Release_ReleaseId;
            ArgUtil.NotNull(releaseId, nameof(releaseId));

            int? releaseEnvironmentId = context.Variables.Release_ReleaseEnvironmentId;
            ArgUtil.NotNull(releaseId, nameof(releaseEnvironmentId));

            int? attempt = context.Variables.Release_Attempt;
            ArgUtil.NotNull(releaseId, nameof(attempt));

            long? containerId = context.Variables.Release_ContainerId;
            ArgUtil.NotNull(containerId, nameof(containerId));

            //var releaseContainerPath = releaseId + "/" + releaseEnvironmentId + "/" + context.Variables.Release_DeploymentId + "/" + containerPath;
            var releaseContainerPath = releaseId + "/" + containerPath; // No folder change in build artifacts

            context.Debug($"Upload artifact: {source} to server for release: {releaseId.Value} at backend.");
            FileContainerServer fileContainerHelper = new FileContainerServer(connection, projectId, containerId.Value, releaseContainerPath);
            await fileContainerHelper.CopyToContainerAsync(commandContext, source, cancellationToken);
            string fileContainerFullPath = StringUtil.Format($"#/{containerId.Value}/{releaseContainerPath}");
            context.Output(StringUtil.Loc("UploadToFileContainer", source, fileContainerFullPath));

            ServiceEndpoint vssEndpoint = context.Endpoints.FirstOrDefault(e => string.Equals(e.Name, ServiceEndpoints.SystemVssConnection, StringComparison.OrdinalIgnoreCase));
            ArgUtil.NotNull(vssEndpoint, nameof(vssEndpoint));
            ArgUtil.NotNull(vssEndpoint.Url, nameof(vssEndpoint.Url));

            context.Debug($"Connecting to {vssEndpoint.Url}/{projectId}");
            var releaseServer = new ReleaseServer(vssEndpoint.Url, ApiUtil.GetVssCredential(vssEndpoint), projectId);

            var artifact = await releaseServer.AssociateArtifact(releaseId.Value, releaseEnvironmentId.Value, Convert.ToInt32(context.Variables.Release_DeploymentId), name, WellKnownArtifactResourceTypes.Container, fileContainerFullPath, propertiesDictionary, cancellationToken);
            context.Output(StringUtil.Loc("AssociateArtifactWithRelease", artifact.Id, releaseId));
        }
    }

    public class BuildArtifactCommands : IArtifactCommands
    {
        public async Task AssociateArtifactAsync(
            IExecutionContext context,
            IAsyncCommandContext commandContext,
            VssConnection connection,
            Guid projectId,
            string name,
            string type,
            string data,
            Dictionary<string, string> propertiesDictionary,
            CancellationToken cancellationToken)
        {
            int? buildId = context.Variables.Build_BuildId;
            ArgUtil.NotNull(buildId, nameof(buildId));

            context.Debug($"Associate artifact: {name} with build: {buildId.Value} at backend.");
            BuildServer buildHelper = new BuildServer(connection, projectId);
            var artifact = await buildHelper.AssociateArtifact(buildId.Value, name, type, data, propertiesDictionary, cancellationToken);
            context.Output(StringUtil.Loc("AssociateArtifactWithBuild", artifact.Id, buildId));
        }

        public async Task UploadArtifactAsync(
            IExecutionContext context,
            IAsyncCommandContext commandContext,
            VssConnection connection,
            Guid projectId,
            string containerPath,
            string name,
            Dictionary<string, string> propertiesDictionary,
            string source,
            CancellationToken cancellationToken)
        {
            int? buildId = context.Variables.Build_BuildId;
            ArgUtil.NotNull(buildId, nameof(buildId));

            long? containerId = context.Variables.Build_ContainerId;
            ArgUtil.NotNull(containerId, nameof(containerId));

            context.Debug($"Upload artifact: {source} to server for build: {buildId.Value} at backend.");
            FileContainerServer fileContainerHelper = new FileContainerServer(connection, projectId, containerId.Value, containerPath);
            await fileContainerHelper.CopyToContainerAsync(commandContext, source, cancellationToken);
            string fileContainerFullPath = StringUtil.Format($"#/{containerId.Value}/{containerPath}");
            context.Output(StringUtil.Loc("UploadToFileContainer", source, fileContainerFullPath));

            BuildServer buildHelper = new BuildServer(connection, projectId);
            var artifact = await buildHelper.AssociateArtifact(buildId.Value, name, WellKnownArtifactResourceTypes.Container, fileContainerFullPath, propertiesDictionary, cancellationToken);
            context.Output(StringUtil.Loc("AssociateArtifactWithBuild", artifact.Id, buildId));
        }
    }

    public sealed class ArtifactCommandExtension : AgentService, IWorkerCommandExtension
    {
        public Type ExtensionType => typeof(IWorkerCommandExtension);

        public string CommandArea => "artifact";

        public void ProcessCommand(IExecutionContext context, Command command)
        {
            IArtifactCommands artifactCommands;
            if (IsHostTypeRelease(context))
            {
                artifactCommands = new ReleaseArtifactCommands();
            }
            else
            {
                artifactCommands = new BuildArtifactCommands();
            }
            if (string.Equals(command.Event, WellKnownArtifactCommand.Associate, StringComparison.OrdinalIgnoreCase))
            {
                ProcessArtifactAssociateCommand(context, artifactCommands, command.Properties, command.Data);
            }
            else if (string.Equals(command.Event, WellKnownArtifactCommand.Upload, StringComparison.OrdinalIgnoreCase))
            {
                ProcessArtifactUploadCommand(context, artifactCommands, command.Properties, command.Data);
            }
            else
            {
                throw new Exception(StringUtil.Loc("ArtifactCommandNotFound", command.Event));
            }
        }

        private bool IsHostTypeRelease(IExecutionContext context)
        {
            var hostType = context.Variables.System_HostType;

            if (hostType != null && String.Equals(hostType.ToString(), "release", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private void ProcessArtifactAssociateCommand(IExecutionContext context, IArtifactCommands artifactCommands, Dictionary<string, string> eventProperties, string data)
        {
            ArgUtil.NotNull(context, nameof(context));
            ArgUtil.NotNull(context.Endpoints, nameof(context.Endpoints));

            ServiceEndpoint systemConnection = context.Endpoints.FirstOrDefault(e => string.Equals(e.Name, ServiceEndpoints.SystemVssConnection, StringComparison.OrdinalIgnoreCase));
            ArgUtil.NotNull(systemConnection, nameof(systemConnection));
            ArgUtil.NotNull(systemConnection.Url, nameof(systemConnection.Url));

            Uri projectUrl = systemConnection.Url;
            VssCredentials projectCredential = ApiUtil.GetVssCredential(systemConnection);

            Guid projectId = context.Variables.System_TeamProjectId ?? Guid.Empty;
            ArgUtil.NotEmpty(projectId, nameof(projectId));

            string artifactName;
            if (!eventProperties.TryGetValue(ArtifactAssociateEventProperties.ArtifactName, out artifactName) ||
                string.IsNullOrEmpty(artifactName))
            {
                throw new Exception(StringUtil.Loc("ArtifactNameRequired"));
            }

            string artifactLocation = data;
            if (string.IsNullOrEmpty(artifactLocation))
            {
                throw new Exception(StringUtil.Loc("ArtifactLocationRequired"));
            }

            string artifactType;
            if (!eventProperties.TryGetValue(ArtifactAssociateEventProperties.ArtifactType, out artifactType))
            {
                artifactType = InferArtifactResourceType(context, artifactLocation);
            }

            if (string.IsNullOrEmpty(artifactType))
            {
                throw new Exception(StringUtil.Loc("ArtifactTypeRequired"));
            }

            var propertyDictionary = ExtractArtifactProperties(eventProperties);

            string artifactData = "";
            if (IsContainerPath(artifactLocation) ||
                IsValidServerPath(artifactLocation))
            {
                //if artifactlocation is a file container path or a tfvc server path
                artifactData = artifactLocation;
            }
            else if (IsUncSharePath(context, artifactLocation))
            {
                //if artifactlocation is a UNC share path
                artifactData = new Uri(artifactLocation).LocalPath;
            }
            else
            {
                throw new Exception(StringUtil.Loc("ArtifactLocationNotSupport", artifactLocation));
            }

            // queue async command task to associate artifact.
            var commandContext = HostContext.CreateService<IAsyncCommandContext>();
            commandContext.InitializeCommandContext(context, StringUtil.Loc("AssociateArtifact"));
            commandContext.Task = artifactCommands.AssociateArtifactAsync(context,
                                                         commandContext,
                                                         WorkerUtilies.GetVssConnection(context),
                                                         projectId,
                                                         artifactName,
                                                         artifactType,
                                                         artifactData,
                                                         propertyDictionary,
                                                         context.CancellationToken);
            context.AsyncCommands.Add(commandContext);
        }

        private void ProcessArtifactUploadCommand(IExecutionContext context, IArtifactCommands artifactCommands, Dictionary<string, string> eventProperties, string data)
        {
            ArgUtil.NotNull(context, nameof(context));
            ArgUtil.NotNull(context.Endpoints, nameof(context.Endpoints));

            Guid projectId = context.Variables.System_TeamProjectId ?? Guid.Empty;
            ArgUtil.NotEmpty(projectId, nameof(projectId));

            string artifactName;
            if (!eventProperties.TryGetValue(ArtifactAssociateEventProperties.ArtifactName, out artifactName) ||
                string.IsNullOrEmpty(artifactName))
            {
                throw new Exception(StringUtil.Loc("ArtifactNameRequired"));
            }

            string containerFolder;
            if (!eventProperties.TryGetValue(ArtifactUploadEventProperties.ContainerFolder, out containerFolder) ||
                string.IsNullOrEmpty(containerFolder))
            {
                containerFolder = artifactName;
            }

            var propertyDictionary = ExtractArtifactProperties(eventProperties);

            string localPath = data;
            if (string.IsNullOrEmpty(localPath))
            {
                throw new Exception(StringUtil.Loc("ArtifactLocationRequired"));
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
                context.Warning(StringUtil.Loc("DirectoryIsEmptyForArtifact", fullPath, artifactName));
                return;
            }

            var commandContext = HostContext.CreateService<IAsyncCommandContext>();
            commandContext.InitializeCommandContext(context, StringUtil.Loc("UploadArtifact"));
            commandContext.Task = artifactCommands.UploadArtifactAsync(context,
                                                      commandContext,
                                                      WorkerUtilies.GetVssConnection(context),
                                                      projectId,
                                                      containerFolder,
                                                      artifactName,
                                                      propertyDictionary,
                                                      fullPath,
                                                      context.CancellationToken);
            context.AsyncCommands.Add(commandContext);
        }

        private async Task UploadArtifactAsync(
            IAsyncCommandContext context,
            VssConnection connection,
            Guid projectId,
            long containerId,
            string containerPath,
            int buildId,
            string name,
            Dictionary<string, string> propertiesDictionary,
            string source,
            CancellationToken cancellationToken)
        {
            FileContainerServer fileContainerHelper = new FileContainerServer(connection, projectId, containerId, containerPath);
            await fileContainerHelper.CopyToContainerAsync(context, source, cancellationToken);
            string fileContainerFullPath = StringUtil.Format($"#/{containerId}/{containerPath}");
            context.Output(StringUtil.Loc("UploadToFileContainer", source, fileContainerFullPath));

            BuildServer buildHelper = new BuildServer(connection, projectId);
            var artifact = await buildHelper.AssociateArtifact(buildId, name, WellKnownArtifactResourceTypes.Container, fileContainerFullPath, propertiesDictionary, cancellationToken);
            context.Output(StringUtil.Loc("AssociateArtifactWithBuild", artifact.Id, buildId));
        }

        private Boolean IsContainerPath(string path)
        {
            return !string.IsNullOrEmpty(path) &&
                    path.StartsWith("#", StringComparison.OrdinalIgnoreCase);
        }

        private Boolean IsValidServerPath(string path)
        {
            return !string.IsNullOrEmpty(path) &&
                    path.Length >= 2 &&
                    path[0] == '$' &&
                    (path[1] == '/' || path[1] == '\\');
        }

        private Boolean IsUncSharePath(IExecutionContext context, string path)
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

        private string InferArtifactResourceType(IExecutionContext context, string artifactLocation)
        {
            string type = "";
            if (!string.IsNullOrEmpty(artifactLocation))
            {
                // Prioritize UNC first as leading double-backslash can also match Tfvc VC paths (multiple slashes in a row are ignored)
                if (IsUncSharePath(context, artifactLocation))
                {
                    type = WellKnownArtifactResourceTypes.FilePath;
                }
                else if (IsValidServerPath(artifactLocation))
                {
                    // TFVC artifact
                    type = WellKnownArtifactResourceTypes.VersionControl;
                }
                else if (IsContainerPath(artifactLocation))
                {
                    // file container artifact
                    type = WellKnownArtifactResourceTypes.Container;
                }
            }

            if (string.IsNullOrEmpty(type))
            {
                throw new Exception(StringUtil.Loc("UnableResolveArtifactType", artifactLocation));
            }

            return type;
        }

        private Dictionary<string, string> ExtractArtifactProperties(Dictionary<string, string> eventProperties)
        {
            return eventProperties.Where(pair => !(string.Compare(pair.Key, ArtifactUploadEventProperties.ContainerFolder, StringComparison.OrdinalIgnoreCase) == 0 ||
                                                  string.Compare(pair.Key, ArtifactUploadEventProperties.ArtifactName, StringComparison.OrdinalIgnoreCase) == 0 ||
                                                  string.Compare(pair.Key, ArtifactAssociateEventProperties.ArtifactType, StringComparison.OrdinalIgnoreCase) == 0)).ToDictionary(pair => pair.Key, pair => pair.Value);
        }
    }

    internal static class WellKnownArtifactCommand
    {
        public static readonly string Associate = "associate";
        public static readonly string Upload = "upload";
    }

    internal static class ArtifactAssociateEventProperties
    {
        public static readonly string ArtifactName = "artifactname";
        public static readonly string ArtifactType = "artifacttype";
        public static readonly string Browsable = "Browsable";
    }

    internal static class ArtifactUploadEventProperties
    {
        public static readonly string ContainerFolder = "containerfolder";
        public static readonly string ArtifactName = "artifactname";
        public static readonly string Browsable = "Browsable";
    }
}