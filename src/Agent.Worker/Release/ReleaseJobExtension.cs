// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.ReleaseManagement.WebApi.Contracts;
using Microsoft.VisualStudio.Services.Agent.Worker.Release.Artifacts;
using Microsoft.VisualStudio.Services.Agent.Worker.Release.Artifacts.Definition;
using Newtonsoft.Json;
using Microsoft.TeamFoundation.DistributedTask.Pipelines;

using Agent.Sdk;
using Agent.Sdk.Knob;
using Agent.Sdk.Util;

namespace Microsoft.VisualStudio.Services.Agent.Worker.Release
{
    public class ReleaseJobExtension : JobExtension
    {
        private const string DownloadArtifactsFailureSystemError = "DownloadArtifactsFailureSystemError";

        private const string DownloadArtifactsFailureUserError = "DownloadArtifactsFailureUserError";

        private static readonly Guid DownloadArtifactsTaskId = new Guid("B152FEAA-7E65-43C9-BCC4-07F6883EE793");

        private int ReleaseId { get; set; }
        private Guid TeamProjectId { get; set; }
        private string ReleaseWorkingFolder { get; set; }
        private string ArtifactsWorkingFolder { get; set; }
        private bool SkipArtifactsDownload { get; set; }
        private IList<AgentArtifactDefinition> ReleaseArtifacts { get; set; } = new List<AgentArtifactDefinition>();

        public override Type ExtensionType => typeof(IJobExtension);
        public override HostTypes HostType => HostTypes.Release;

        private TeeUtil teeUtil;

        public override IStep GetExtensionPreJobStep(IExecutionContext jobContext)
        {
            if (ReleaseArtifacts.Any())
            {
                return new JobExtensionRunner(
                    runAsync: DownloadArtifactsAndCommitsAsync,
                    condition: ExpressionManager.Succeeded,
                    displayName: StringUtil.Loc("DownloadArtifacts"),
                    data: null);
            }

            return null;
        }

        public override IStep GetExtensionPostJobStep(IExecutionContext jobContext)
        {
            return null;
        }

        public override string GetRootedPath(IExecutionContext context, string path)
        {
            ArgUtil.NotNull(context, nameof(context));

            string rootedPath = null;

            if (!string.IsNullOrEmpty(path) &&
                   path.IndexOfAny(Path.GetInvalidPathChars()) < 0 &&
                   Path.IsPathRooted(path))
            {
                try
                {
                    rootedPath = Path.GetFullPath(path);
                    Trace.Info($"Path resolved by source provider is a rooted path, return absolute path: {rootedPath}");
                    return rootedPath;
                }
                catch (Exception ex)
                {
                    Trace.Info($"Path resolved is a rooted path, but it is not fully qualified, return the path: {path}");
                    Trace.Error(ex);
                    return path;
                }
            }

            string artifactRootPath = context.Variables.Release_ArtifactsDirectory ?? string.Empty;
            Trace.Info($"Artifact root path is system.artifactsDirectory: {artifactRootPath}");

            if (!string.IsNullOrEmpty(artifactRootPath) && artifactRootPath.IndexOfAny(Path.GetInvalidPathChars()) < 0 &&
                path != null && path.IndexOfAny(Path.GetInvalidPathChars()) < 0)
            {
                path = Path.Combine(artifactRootPath, path);
                Trace.Info($"After prefix Artifact Path Root provide by JobExtension: {path}");
                if (Path.IsPathRooted(path))
                {
                    try
                    {
                        rootedPath = Path.GetFullPath(path);
                        Trace.Info($"Return absolute path after prefix ArtifactPathRoot: {rootedPath}");
                        return rootedPath;
                    }
                    catch (Exception ex)
                    {
                        Trace.Info($"After prefix Artifact Path Root provide by JobExtension. The Path is a rooted path, but it is not fully qualified, return the path: {path}");
                        Trace.Error(ex);
                        return path;
                    }
                }
            }

            return rootedPath;
        }

        public override void ConvertLocalPath(IExecutionContext context, string localPath, out string repoName, out string sourcePath)
        {
            Trace.Info($"Received localpath {localPath}");
            repoName = string.Empty;
            sourcePath = string.Empty;
        }

        private async Task DownloadArtifactsAndCommitsAsync(IExecutionContext executionContext, object data)
        {
            Trace.Entering();

            try
            {
                await DownloadArtifacts(executionContext, ReleaseArtifacts, ArtifactsWorkingFolder);
                await DownloadCommits(executionContext, TeamProjectId, ReleaseArtifacts);
            }
            catch (SocketException ex)
            {
                LogDownloadFailureTelemetry(executionContext, ex);

                ExceptionsUtil.HandleSocketException(ex, WorkerUtilities.GetVssConnection(executionContext).Uri.ToString(), Trace.Error);
                throw;
            }
            catch (Exception ex)
            {
                LogDownloadFailureTelemetry(executionContext, ex);
                throw;
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA2000:Dispose objects before losing scope", MessageId = "releaseServer")]
        private IList<AgentArtifactDefinition> GetReleaseArtifacts(IExecutionContext executionContext)
        {
            try
            {
                var connection = WorkerUtilities.GetVssConnection(executionContext);
                var releaseServer = executionContext.GetHostContext().GetService<IReleaseServer>();
                releaseServer.ConnectAsync(connection).GetAwaiter().GetResult();

                IList<AgentArtifactDefinition> releaseArtifacts = releaseServer.GetReleaseArtifactsFromService(ReleaseId, TeamProjectId).ToList();
                IList<AgentArtifactDefinition> filteredReleaseArtifacts = FilterArtifactDefintions(releaseArtifacts);
                filteredReleaseArtifacts.ToList().ForEach(x => Trace.Info($"Found Artifact = {x.Alias} of type {x.ArtifactType}"));
                return filteredReleaseArtifacts;
            }
            catch (Exception ex)
            {
                LogDownloadFailureTelemetry(executionContext, ex);
                throw;
            }
        }

        private async Task DownloadCommits(
            IExecutionContext executionContext,
            Guid teamProjectId,
            IList<AgentArtifactDefinition> agentArtifactDefinitions)
        {
            Trace.Entering();
            string commitsWorkFolder = String.Empty;

            if (agentArtifactDefinitions?.Any(x => x.ArtifactType == AgentArtifactType.Jenkins) == true)
            {
                Trace.Info("Creating commit work folder");
                commitsWorkFolder = GetCommitsWorkFolder(executionContext);
            }

            // Note: We are having an explicit type here. For other artifact types we are planning to go with tasks
            // Only for jenkins we are making the agent to download
            var extensionManager = HostContext.GetService<IExtensionManager>();
            JenkinsArtifact jenkinsExtension = (extensionManager.GetExtensions<IArtifactExtension>()).FirstOrDefault(x => x.ArtifactType == AgentArtifactType.Jenkins) as JenkinsArtifact;

            foreach (AgentArtifactDefinition agentArtifactDefinition in agentArtifactDefinitions)
            {
                if (agentArtifactDefinition.ArtifactType == AgentArtifactType.Jenkins)
                {
                    Trace.Info($"Found supported artifact {agentArtifactDefinition.Alias} for downloading commits");
                    ArtifactDefinition artifactDefinition = ConvertToArtifactDefinition(agentArtifactDefinition, executionContext, jenkinsExtension);
                    await jenkinsExtension.DownloadCommitsAsync(executionContext, artifactDefinition, commitsWorkFolder);
                }
            }
        }

        private string GetCommitsWorkFolder(IExecutionContext context)
        {
            string commitsRootDirectory = Path.Combine(ReleaseWorkingFolder, Constants.Release.Path.ReleaseTempDirectoryPrefix, Constants.Release.Path.CommitsDirectory);

            Trace.Info($"Ensuring commit work folder {commitsRootDirectory} exists");
            var releaseFileSystemManager = HostContext.GetService<IReleaseFileSystemManager>();
            releaseFileSystemManager.EnsureDirectoryExists(commitsRootDirectory);

            return commitsRootDirectory;
        }

        private async Task DownloadArtifacts(IExecutionContext executionContext,
            IList<AgentArtifactDefinition> agentArtifactDefinitions, string artifactsWorkingFolder)
        {
            Trace.Entering();

            CreateArtifactsFolder(executionContext, artifactsWorkingFolder);

            executionContext.Output(StringUtil.Loc("RMDownloadingArtifact"));

            bool isTeeUsed = PlatformUtil.RunningOnLinux && agentArtifactDefinitions.Any(x =>
                x.ArtifactType == AgentArtifactType.Tfvc);
            if (isTeeUsed)
            {
                teeUtil = new TeeUtil(
                    executionContext.GetVariableValueOrDefault("Agent.HomeDirectory"),
                    executionContext.GetVariableValueOrDefault("Agent.TempDirectory"),
                    AgentKnobs.TeePluginDownloadRetryCount.GetValue(executionContext).AsInt(),
                    executionContext.Debug,
                    executionContext.CancellationToken
                );
                await teeUtil.DownloadTeeIfAbsent();
            }

            try
            {
                foreach (AgentArtifactDefinition agentArtifactDefinition in agentArtifactDefinitions)
                {
                    // We don't need to check if its old style artifact anymore. All the build data has been fixed and all the build artifact has Alias now.
                    ArgUtil.NotNullOrEmpty(agentArtifactDefinition.Alias, nameof(agentArtifactDefinition.Alias));

                    var extensionManager = HostContext.GetService<IExtensionManager>();
                    IArtifactExtension extension =
                        (extensionManager.GetExtensions<IArtifactExtension>()).FirstOrDefault(x =>
                            agentArtifactDefinition.ArtifactType == x.ArtifactType);

                    if (extension == null)
                    {
                        throw new InvalidOperationException(StringUtil.Loc("RMArtifactTypeNotSupported",
                            agentArtifactDefinition.ArtifactType));
                    }

                    Trace.Info($"Found artifact extension of type {extension.ArtifactType}");
                    executionContext.Output(StringUtil.Loc("RMStartArtifactsDownload"));
                    ArtifactDefinition artifactDefinition =
                        ConvertToArtifactDefinition(agentArtifactDefinition, executionContext, extension);
                    executionContext.Output(StringUtil.Loc("RMArtifactDownloadBegin", agentArtifactDefinition.Alias,
                        agentArtifactDefinition.ArtifactType));

                    // Get the local path where this artifact should be downloaded.
                    string downloadFolderPath = Path.GetFullPath(Path.Combine(artifactsWorkingFolder,
                        agentArtifactDefinition.Alias ?? string.Empty));

                    // download the artifact to this path.
                    RetryExecutor retryExecutor = new RetryExecutor();
                    retryExecutor.ShouldRetryAction = (ex) =>
                    {
                        executionContext.Output(StringUtil.Loc("RMErrorDuringArtifactDownload", ex));

                        bool retry = true;
                        if (ex is ArtifactDownloadException)
                        {
                            retry = false;
                        }
                        else
                        {
                            executionContext.Output(StringUtil.Loc("RMRetryingArtifactDownload"));
                            Trace.Warning(ex.ToString());
                        }

                        return retry;
                    };

                    await retryExecutor.ExecuteAsync(
                        async () =>
                        {
                            var releaseFileSystemManager = HostContext.GetService<IReleaseFileSystemManager>();
                            executionContext.Output(StringUtil.Loc("RMEnsureArtifactFolderExistsAndIsClean",
                                downloadFolderPath));
                            releaseFileSystemManager.EnsureEmptyDirectory(downloadFolderPath,
                                executionContext.CancellationToken);

                            await extension.DownloadAsync(executionContext, artifactDefinition, downloadFolderPath);
                        });

                    executionContext.Output(StringUtil.Loc("RMArtifactDownloadFinished",
                        agentArtifactDefinition.Alias));
                }

                executionContext.Output(StringUtil.Loc("RMArtifactsDownloadFinished"));
            }
            finally
            {
                if (isTeeUsed && !AgentKnobs.DisableTeePluginRemoval.GetValue(executionContext).AsBoolean())
                {
                    teeUtil.DeleteTee();
                }
            }
        }

        private void CreateArtifactsFolder(IExecutionContext executionContext, string artifactsWorkingFolder)
        {
            Trace.Entering();

            RetryExecutor retryExecutor = new RetryExecutor();
            retryExecutor.ShouldRetryAction = (ex) =>
            {
                executionContext.Output(StringUtil.Loc("RMRetryingCreatingArtifactsDirectory", artifactsWorkingFolder, ex));
                Trace.Error(ex);

                return true;
            };

            retryExecutor.Execute(
                () =>
                {
                    executionContext.Output(StringUtil.Loc("RMCreatingArtifactsDirectory", artifactsWorkingFolder));

                    var releaseFileSystemManager = HostContext.GetService<IReleaseFileSystemManager>();
                    releaseFileSystemManager.EnsureEmptyDirectory(artifactsWorkingFolder, executionContext.CancellationToken);
                });

            executionContext.Output(StringUtil.Loc("RMCreatedArtifactsDirectory", artifactsWorkingFolder));
        }

        public override void InitializeJobExtension(IExecutionContext executionContext, IList<JobStep> steps, WorkspaceOptions workspace)
        {
            ArgUtil.NotNull(executionContext, nameof(executionContext));
            Trace.Entering();

            executionContext.Output(StringUtil.Loc("PrepareReleasesDir"));
            var directoryManager = HostContext.GetService<IReleaseDirectoryManager>();
            ReleaseId = executionContext.Variables.GetInt(Constants.Variables.Release.ReleaseId) ?? 0;
            TeamProjectId = executionContext.Variables.GetGuid(Constants.Variables.System.TeamProjectId) ?? Guid.Empty;
            SkipArtifactsDownload = executionContext.Variables.GetBoolean(Constants.Variables.Release.SkipArtifactsDownload) ?? false;
            string releaseDefinitionName = executionContext.Variables.Get(Constants.Variables.Release.ReleaseDefinitionName);

            // TODO: Should we also write to log in executionContext.Output methods? so that we don't have to repeat writing into logs?
            // Log these values here to debug scenarios where downloading the artifact fails.
            executionContext.Output($"ReleaseId={ReleaseId}, TeamProjectId={TeamProjectId}, ReleaseDefinitionName={releaseDefinitionName}");

            var releaseDefinition = executionContext.Variables.Get(Constants.Variables.Release.ReleaseDefinitionId);
            if (string.IsNullOrEmpty(releaseDefinition))
            {
                string pattern = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
                Regex regex = new Regex(string.Format("[{0}]", Regex.Escape(pattern)));
                releaseDefinition = regex.Replace(releaseDefinitionName, string.Empty);
            }

            ArgUtil.NotNull(executionContext, nameof(executionContext)); // I am not sure why this is needed, but static analysis flagged all uses of executionContext below this point
            var releaseTrackingConfig = directoryManager.PrepareArtifactsDirectory(
                HostContext.GetDirectory(WellKnownDirectory.Work),
                executionContext.Variables.System_CollectionId,
                executionContext.Variables.System_TeamProjectId.ToString(),
                releaseDefinition);

            ReleaseWorkingFolder = Path.Combine(
                        HostContext.GetDirectory(WellKnownDirectory.Work),
                        releaseTrackingConfig.ReleaseDirectory);

            ArtifactsWorkingFolder = string.IsNullOrEmpty(executionContext.Variables.Release_ArtifactsDirectory)
                ? Path.Combine(
                        HostContext.GetDirectory(WellKnownDirectory.Work),
                        releaseTrackingConfig.ReleaseDirectory,
                        Constants.Release.Path.ArtifactsDirectory)
                : executionContext.Variables.Release_ArtifactsDirectory;
            executionContext.Output($"Release folder: {ArtifactsWorkingFolder}");

            // Ensure directory exist
            if (!Directory.Exists(ArtifactsWorkingFolder))
            {
                Trace.Info($"Creating {ArtifactsWorkingFolder}.");
                Directory.CreateDirectory(ArtifactsWorkingFolder);
            }

            SetLocalVariables(executionContext, ArtifactsWorkingFolder);

            // Log the environment variables available after populating the variable service with our variables
            LogEnvironmentVariables(executionContext);

            if (SkipArtifactsDownload)
            {
                // If this is the first time the agent is executing a task, we need to create the artifactsFolder
                // otherwise Process.StartWithCreateProcess() will fail with the error "The directory name is invalid"
                // because the working folder doesn't exist
                CreateWorkingFolderIfRequired(executionContext, ArtifactsWorkingFolder);

                // log the message that the user chose to skip artifact download and move on
                executionContext.Output(StringUtil.Loc("RMUserChoseToSkipArtifactDownload"));
                Trace.Info("Skipping artifact download based on the setting specified.");
            }
            else
            {
                ReleaseArtifacts = GetReleaseArtifacts(executionContext);

                if (!ReleaseArtifacts.Any())
                {
                    CreateArtifactsFolder(executionContext, ArtifactsWorkingFolder);
                    Trace.Info("No artifacts found to be downloaded by agent.");
                }
            }

            CheckForAvailableDiskSpace(executionContext);
        }

        private void CheckForAvailableDiskSpace(IExecutionContext executionContext)
        {
            try
            {
                var root = Path.GetPathRoot(ArtifactsWorkingFolder);

                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (string.Equals(root, drive.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        var availableSpaceInMB = drive.AvailableFreeSpace / (1024 * 1024);
                        if (availableSpaceInMB < 100)
                        {
                            executionContext.Warning(StringUtil.Loc("RMLowAvailableDiskSpace", root));
                        }

                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                // ignore any exceptions during checking for free disk space
                Trace.Error("Failed to check for available disk space: " + ex);
            }
        }

        private void SetLocalVariables(IExecutionContext executionContext, string artifactsDirectoryPath)
        {
            Trace.Entering();

            // Always set the AgentReleaseDirectory because this is set as the WorkingDirectory of the task.
            executionContext.SetVariable(Constants.Variables.Release.AgentReleaseDirectory, artifactsDirectoryPath);

            // Set the ArtifactsDirectory even when artifacts downloaded is skipped. Reason: The task might want to access the old artifact.
            executionContext.SetVariable(Constants.Variables.Release.ArtifactsDirectory, artifactsDirectoryPath);
            executionContext.SetVariable(Constants.Variables.System.DefaultWorkingDirectory, artifactsDirectoryPath);
        }

        private void LogEnvironmentVariables(IExecutionContext executionContext)
        {
            Trace.Entering();
            string stringifiedEnvironmentVariables = AgentUtilities.GetPrintableEnvironmentVariables(executionContext.Variables.Public);

            // Use LogMessage to ensure that the logs reach the TWA UI, but don't spam the console cmd window
            executionContext.Output(StringUtil.Loc("RMEnvironmentVariablesAvailable", stringifiedEnvironmentVariables));
        }

        private void CreateWorkingFolderIfRequired(IExecutionContext executionContext, string artifactsFolderPath)
        {
            Trace.Entering();
            if (!Directory.Exists(artifactsFolderPath))
            {
                executionContext.Output($"Creating artifacts folder: {artifactsFolderPath}");
                Directory.CreateDirectory(artifactsFolderPath);
            }
        }

        private ArtifactDefinition ConvertToArtifactDefinition(AgentArtifactDefinition agentArtifactDefinition, IExecutionContext executionContext, IArtifactExtension extension)
        {
            Trace.Entering();

            ArgUtil.NotNull(agentArtifactDefinition, nameof(agentArtifactDefinition));
            ArgUtil.NotNull(executionContext, nameof(executionContext));

            var artifactDefinition = new ArtifactDefinition
            {
                ArtifactType = agentArtifactDefinition.ArtifactType,
                Name = agentArtifactDefinition.Name,
                Version = agentArtifactDefinition.Version
            };

            RetryExecutor retryExecutor = new RetryExecutor();
                retryExecutor.ShouldRetryAction = (ex) =>
                {
                    bool retry = true;
                    if (ex is InvalidOperationException)
                    {
                        retry = false;
                    }
                    else
                    {
                        Trace.Warning(ex.ToString());
                    }

                    return retry;
                };

            retryExecutor.Execute(
                () =>
                {
                    artifactDefinition.Details = extension.GetArtifactDetails(executionContext, agentArtifactDefinition);
                });

            return artifactDefinition;
        }

        private void LogDownloadFailureTelemetry(IExecutionContext executionContext, Exception ex)
        {
            var code = (ex is ArtifactDownloadException ||
                        ex is ArtifactDirectoryCreationFailedException ||
                        ex is IOException ||
                        ex is UnauthorizedAccessException) ? DownloadArtifactsFailureUserError : DownloadArtifactsFailureSystemError;
            var issue = new Issue
            {
                Type = IssueType.Error,
                Message = StringUtil.Loc("DownloadArtifactsFailed", ex)
            };

            issue.Data.Add("AgentVersion", BuildConstants.AgentPackage.Version);
            issue.Data.Add("code", code);
            issue.Data.Add("TaskId", DownloadArtifactsTaskId.ToString());

            executionContext.AddIssue(issue);
        }

        private IList<AgentArtifactDefinition> FilterArtifactDefintions(IList<AgentArtifactDefinition> agentArtifactDefinitions)
        {
            var definitions = new List<AgentArtifactDefinition>();
            foreach (var agentArtifactDefinition in agentArtifactDefinitions)
            {
                if (agentArtifactDefinition.ArtifactType != AgentArtifactType.Custom)
                {
                    definitions.Add(agentArtifactDefinition);
                }
                else
                {
                    string artifactType = string.Empty;
                    var artifactDetails = JsonConvert.DeserializeObject<Dictionary<string, string>>(agentArtifactDefinition.Details);
                    if (artifactDetails.TryGetValue("ArtifactType", out artifactType))
                    {
                        if (artifactType == null || artifactType.Equals("Build", StringComparison.OrdinalIgnoreCase))
                        {
                            definitions.Add(agentArtifactDefinition);
                        }
                    }
                    else
                    {
                        definitions.Add(agentArtifactDefinition);
                    }
                }
            }

            return definitions;
        }
    }
}
