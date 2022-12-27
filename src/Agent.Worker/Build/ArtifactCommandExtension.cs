// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
using Microsoft.VisualStudio.Services.WebApi;

namespace Microsoft.VisualStudio.Services.Agent.Worker.Build
{
    public sealed class ArtifactCommandExtension : BaseWorkerCommandExtension
    {
        public ArtifactCommandExtension()
        {
            CommandArea = "artifact";
            SupportedHostTypes = HostTypes.Build | HostTypes.Release;
            InstallWorkerCommand(new ArtifactAssociateCommand());
            InstallWorkerCommand(new ArtifactUploadCommand());
        }
    }

    public sealed class ArtifactAssociateCommand : IWorkerCommand
    {
        public string Name => "associate";
        public List<string> Aliases => null;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA2000:Dispose objects before losing scope", MessageId = "WorkerUtilities")]
        public void Execute(IExecutionContext context, Command command)
        {
            ArgUtil.NotNull(context, nameof(context));
            ArgUtil.NotNull(context.Endpoints, nameof(context.Endpoints));
            ArgUtil.NotNull(command, nameof(command));

            var eventProperties = command.Properties;
            var data = command.Data;

            ServiceEndpoint systemConnection = context.Endpoints.FirstOrDefault(e => string.Equals(e.Name, WellKnownServiceEndpointNames.SystemVssConnection, StringComparison.OrdinalIgnoreCase));
            ArgUtil.NotNull(systemConnection, nameof(systemConnection));
            ArgUtil.NotNull(systemConnection.Url, nameof(systemConnection.Url));

            Uri projectUrl = systemConnection.Url;
            VssCredentials projectCredential = VssUtil.GetVssCredential(systemConnection);

            Guid projectId = context.Variables.System_TeamProjectId ?? Guid.Empty;
            ArgUtil.NotEmpty(projectId, nameof(projectId));

            int? buildId = context.Variables.Build_BuildId;
            ArgUtil.NotNull(buildId, nameof(buildId));

            string artifactName;
            if (!eventProperties.TryGetValue(ArtifactAssociateEventProperties.ArtifactName, out artifactName) ||
                string.IsNullOrEmpty(artifactName))
            {
                throw new Exception(StringUtil.Loc("ArtifactNameRequired"));
            }

            string artifactType;
            if (!eventProperties.TryGetValue(ArtifactAssociateEventProperties.ArtifactType, out artifactType))
            {
                artifactType = ArtifactCommandExtensionUtil.InferArtifactResourceType(context, data);
            }

            if (string.IsNullOrEmpty(artifactType))
            {
                throw new Exception(StringUtil.Loc("ArtifactTypeRequired"));
            }
            else if ((artifactType.Equals(ArtifactResourceTypes.Container, StringComparison.OrdinalIgnoreCase) ||
                      artifactType.Equals(ArtifactResourceTypes.FilePath, StringComparison.OrdinalIgnoreCase) ||
                      artifactType.Equals(ArtifactResourceTypes.VersionControl, StringComparison.OrdinalIgnoreCase)) &&
                      string.IsNullOrEmpty(data))
            {
                throw new Exception(StringUtil.Loc("ArtifactLocationRequired"));
            }

            if (!artifactType.Equals(ArtifactResourceTypes.FilePath, StringComparison.OrdinalIgnoreCase) &&
                context.Variables.System_HostType != HostTypes.Build)
            {
                throw new Exception(StringUtil.Loc("AssociateArtifactCommandNotSupported", context.Variables.System_HostType));
            }

            var propertyDictionary = ArtifactCommandExtensionUtil.ExtractArtifactProperties(eventProperties);

            string artifactData = "";
            if (ArtifactCommandExtensionUtil.IsContainerPath(data) ||
                ArtifactCommandExtensionUtil.IsValidServerPath(data))
            {
                //if data is a file container path or a tfvc server path
                artifactData = data;
            }
            else if (ArtifactCommandExtensionUtil.IsUncSharePath(context, data))
            {
                //if data is a UNC share path
                artifactData = new Uri(data).LocalPath;
            }
            else
            {
                artifactData = data ?? string.Empty;
            }

            // queue async command task to associate artifact.
            context.Debug($"Associate artifact: {artifactName} with build: {buildId.Value} at backend.");
            var commandContext = context.GetHostContext().CreateService<IAsyncCommandContext>();
            commandContext.InitializeCommandContext(context, StringUtil.Loc("AssociateArtifact"));
            commandContext.Task = ArtifactCommandExtensionUtil.AssociateArtifactAsync(commandContext,
                                                         WorkerUtilities.GetVssConnection(context),
                                                         projectId,
                                                         buildId.Value,
                                                         artifactName,
                                                         context.Variables.System_JobId,
                                                         artifactType,
                                                         artifactData,
                                                         propertyDictionary,
                                                         context.CancellationToken);
            context.AsyncCommands.Add(commandContext);
        }
    }

    public sealed class ArtifactUploadCommand : IWorkerCommand
    {
        public string Name => "upload";
        public List<string> Aliases => null;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA2000:Dispose objects before losing scope", MessageId = "WorkerUtilities")]
        public void Execute(IExecutionContext context, Command command)
        {
            ArgUtil.NotNull(context, nameof(context));
            ArgUtil.NotNull(context.Endpoints, nameof(context.Endpoints));
            ArgUtil.NotNull(command, nameof(command));

            var eventProperties = command.Properties;
            var data = command.Data;

            Guid projectId = context.Variables.System_TeamProjectId ?? Guid.Empty;
            ArgUtil.NotEmpty(projectId, nameof(projectId));

            int? buildId = context.Variables.Build_BuildId;
            ArgUtil.NotNull(buildId, nameof(buildId));

            long? containerId = context.Variables.Build_ContainerId;
            ArgUtil.NotNull(containerId, nameof(containerId));

            string artifactName;
            if (!eventProperties.TryGetValue(ArtifactUploadEventProperties.ArtifactName, out artifactName) ||
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

            var propertyDictionary = ArtifactCommandExtensionUtil.ExtractArtifactProperties(eventProperties);

            // Translate file path back from container path
            string localPath = context.TranslateToHostPath(data);

            if (string.IsNullOrEmpty(localPath))
            {
                throw new Exception(StringUtil.Loc("ArtifactLocationRequired"));
            }

            if (!ArtifactCommandExtensionUtil.IsUncSharePath(context, localPath) && (context.Variables.System_HostType != HostTypes.Build))
            {
                throw new Exception(StringUtil.Loc("UploadArtifactCommandNotSupported", context.Variables.System_HostType));
            }

            string fullPath = Path.GetFullPath(localPath);
            if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
            {
                // if localPath is not a file or folder on disk
                throw new FileNotFoundException(StringUtil.Loc("PathDoesNotExist", localPath));
            }
            else if (Directory.Exists(fullPath) && Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories).FirstOrDefault() == null)
            {
                // if localPath is a folder but the folder contains nothing
                context.Warning(StringUtil.Loc("DirectoryIsEmptyForArtifact", fullPath, artifactName));
                return;
            }

            // queue async command task to associate artifact.
            context.Debug($"Upload artifact: {fullPath} to server for build: {buildId.Value} at backend.");
            var commandContext = context.GetHostContext().CreateService<IAsyncCommandContext>();
            commandContext.InitializeCommandContext(context, StringUtil.Loc("UploadArtifact"));
            commandContext.Task = ArtifactCommandExtensionUtil.UploadArtifactAsync(commandContext,
                                                      WorkerUtilities.GetVssConnection(context),
                                                      projectId,
                                                      containerId.Value,
                                                      containerFolder,
                                                      buildId.Value,
                                                      artifactName,
                                                      context.Variables.System_JobId,
                                                      propertyDictionary,
                                                      fullPath,
                                                      context.CancellationToken);
            context.AsyncCommands.Add(commandContext);
        }
    }


    internal static class ArtifactCommandExtensionUtil
    {
        public static async Task AssociateArtifactAsync(
            IAsyncCommandContext context,
            VssConnection connection,
            Guid projectId,
            int buildId,
            string name,
            string jobId,
            string type,
            string data,
            Dictionary<string, string> propertiesDictionary,
            CancellationToken cancellationToken)
        {
            var buildHelper = context.GetHostContext().GetService<IBuildServer>();
            await buildHelper.ConnectAsync(connection);
            var artifact = await buildHelper.AssociateArtifactAsync(buildId, projectId, name, jobId, type, data, propertiesDictionary, cancellationToken);
            context.Output(StringUtil.Loc("AssociateArtifactWithBuild", artifact.Id, buildId));
        }

        public static async Task UploadArtifactAsync(
            IAsyncCommandContext context,
            VssConnection connection,
            Guid projectId,
            long containerId,
            string containerPath,
            int buildId,
            string name,
            string jobId,
            Dictionary<string, string> propertiesDictionary,
            string source,
            CancellationToken cancellationToken)
        {
            var fileContainerHelper = new FileContainerServer(connection, projectId, containerId, containerPath);
            var size = await fileContainerHelper.CopyToContainerAsync(context, source, cancellationToken);
            propertiesDictionary.Add(ArtifactUploadEventProperties.ArtifactSize, size.ToString());

            var fileContainerFullPath = StringUtil.Format($"#/{containerId}/{containerPath}");
            context.Output(StringUtil.Loc("UploadToFileContainer", source, fileContainerFullPath));

            var buildHelper = context.GetHostContext().GetService<IBuildServer>();
            await buildHelper.ConnectAsync(connection);
            var artifact = await buildHelper.AssociateArtifactAsync(buildId, projectId, name, jobId, ArtifactResourceTypes.Container, fileContainerFullPath, propertiesDictionary, cancellationToken);
            context.Output(StringUtil.Loc("AssociateArtifactWithBuild", artifact.Id, buildId));
        }

        public static Boolean IsContainerPath(string path)
        {
            return !string.IsNullOrEmpty(path) &&
                    path.StartsWith("#", StringComparison.OrdinalIgnoreCase);
        }

        public static Boolean IsValidServerPath(string path)
        {
            return !string.IsNullOrEmpty(path) &&
                    path.Length >= 2 &&
                    path[0] == '$' &&
                    (path[1] == '/' || path[1] == '\\');
        }

        public static Boolean IsUncSharePath(IExecutionContext context, string path)
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

        public static string InferArtifactResourceType(IExecutionContext context, string artifactLocation)
        {
            string type = "";
            if (!string.IsNullOrEmpty(artifactLocation))
            {
                // Prioritize UNC first as leading double-backslash can also match Tfvc VC paths (multiple slashes in a row are ignored)
                if (IsUncSharePath(context, artifactLocation))
                {
                    type = ArtifactResourceTypes.FilePath;
                }
                else if (IsValidServerPath(artifactLocation))
                {
                    // TFVC artifact
                    type = ArtifactResourceTypes.VersionControl;
                }
                else if (IsContainerPath(artifactLocation))
                {
                    // file container artifact
                    type = ArtifactResourceTypes.Container;
                }
            }

            if (string.IsNullOrEmpty(type))
            {
                throw new Exception(StringUtil.Loc("UnableResolveArtifactType", artifactLocation ?? string.Empty));
            }

            return type;
        }

        public static Dictionary<string, string> ExtractArtifactProperties(Dictionary<string, string> eventProperties)
        {
            return eventProperties.Where(pair => !(string.Compare(pair.Key, ArtifactUploadEventProperties.ContainerFolder, StringComparison.OrdinalIgnoreCase) == 0 ||
                                                  string.Compare(pair.Key, ArtifactUploadEventProperties.ArtifactName, StringComparison.OrdinalIgnoreCase) == 0 ||
                                                  string.Compare(pair.Key, ArtifactUploadEventProperties.ArtifactType, StringComparison.OrdinalIgnoreCase) == 0)).ToDictionary(pair => pair.Key, pair => pair.Value);
        }
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
        public static readonly string ArtifactSize = "artifactsize";
        public static readonly string ArtifactType = "artifacttype";
        public static readonly string Browsable = "Browsable";
    }
}