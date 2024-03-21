// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Agent.Sdk;
using System.Collections.Generic;

namespace Microsoft.VisualStudio.Services.Agent
{
    public enum WellKnownDirectory
    {
        Bin,
        Externals,
        LegacyPSHost,
        Root,
        ServerOM,
        Tasks,
        TaskZips,
        Tee,
        Temp,
        Tf,
        Tools,
        Update,
        Work,
    }

    public enum WellKnownConfigFile
    {
        Agent,
        Credentials,
        RSACredentials,
        Service,
        CredentialStore,
        Certificates,
        Proxy,
        ProxyCredentials,
        ProxyBypass,
        Autologon,
        Options,
        SetupInfo,
        TaskExceptionList // We need to remove this config file - once Node 6 handler is dropped
    }

    public static class WellKnownTasks
    {
        public static class PluginTaskIds
        {
            // We need have the ID for the checkout task for now since it is not present in the azure-pipelines-tasks repo.
            public static readonly Guid CheckoutTask = new Guid("6d15af64-176c-496d-b583-fd2ae21d4df4");
        }

        public static class MicrosoftExtensionTaskIds
        {
            public static readonly Guid GooglePlayIncreaseRolloutTask = new Guid("f8c97cf9-4e17-4244-b0fb-f540cea78153");
            public static readonly Guid GooglePlayPromoteTask = new Guid("4dae1f76-29d3-482f-97d5-e3189a8347c2");
            public static readonly Guid GooglePlayReleaseTask = new Guid("8cf7cac0-620b-11e5-b4cf-8565e60f4d27");
            public static readonly Guid GooglePlayStatusUpdateTask = new Guid("92e6c372-4193-44e5-9db7-58d7d253f4d8");
            public static readonly Guid AppStorePromoteTask = new Guid("cbbf7f14-c386-4c1f-80a3-fe500e2bd976");
            public static readonly Guid AppStoreReleaseTask = new Guid("2e371150-da5e-11e5-83da-0943b1acc572");
            public static readonly Guid IpaResignTask = new Guid("cbbf7f14-c386-4c1f-80a3-fe500e2bd977");
        }

        public static List<Guid> RequiredForTelemetry = new()
        {
            PluginTaskIds.CheckoutTask,
            MicrosoftExtensionTaskIds.GooglePlayIncreaseRolloutTask,
            MicrosoftExtensionTaskIds.GooglePlayPromoteTask,
            MicrosoftExtensionTaskIds.GooglePlayReleaseTask,
            MicrosoftExtensionTaskIds.GooglePlayStatusUpdateTask,
            MicrosoftExtensionTaskIds.AppStorePromoteTask,
            MicrosoftExtensionTaskIds.AppStoreReleaseTask,
            MicrosoftExtensionTaskIds.IpaResignTask
        };
    }

    public static class Constants
    {
        /// <summary>Name of environment variable holding the path.</summary>
        public static string PathVariable
        {
            get =>
                PlatformUtil.RunningOnWindows
                ? "Path"
                : "PATH";
        }
        public static string TFBuild = "TF_BUILD";
        public static string ProcessLookupId = "VSTS_PROCESS_LOOKUP_ID";
        public static string PluginTracePrefix = "##[plugin.trace]";
        public static readonly int AgentDownloadRetryMaxAttempts = 3;
        public const string projectName = "projectName";
        public const string CommandCorrelationIdEnvVar = "COMMAND_CORRELATION_ID";
        public const string TaskInternalIssueSource = "TaskInternal";

        // Environment variable set on hosted Azure Pipelines images to
        // store the version of the image
        public static readonly string ImageVersionVariable = "ImageVersion";

        public static class DefaultContainerMounts
        {
            public static readonly string Externals = "externals";
            public static readonly string Work = "work";
            public static readonly string Tasks = "tasks";
            public static readonly string Tools = "tools";
        }

        public static class Agent
        {
            public static readonly TimeSpan ExitOnUnloadTimeout = TimeSpan.FromSeconds(30);

            public static class CommandLine
            {
                //if you are adding a new arg, please make sure you update the
                //validArgs array as well present in the CommandSettings.cs
                public static class Args
                {
                    public const string Agent = "agent";
                    public const string Auth = "auth";
                    public const string CollectionName = "collectionname";
                    public const string DeploymentGroupName = "deploymentgroupname";
                    public const string DeploymentPoolName = "deploymentpoolname";
                    public const string DeploymentGroupTags = "deploymentgrouptags";
                    public const string EnvironmentName = "environmentname";
                    public const string EnvironmentVMResourceTags = "virtualmachineresourcetags";
                    public const string MachineGroupName = "machinegroupname";
                    public const string MachineGroupTags = "machinegrouptags";
                    public const string MonitorSocketAddress = "monitorsocketaddress";
                    public const string NotificationPipeName = "notificationpipename";
                    public const string NotificationSocketAddress = "notificationsocketaddress";
                    public const string Pool = "pool";
                    public const string ProjectName = "projectname";
                    public const string ProxyUrl = "proxyurl";
                    public const string ProxyUserName = "proxyusername";
                    public const string SslCACert = "sslcacert";
                    public const string SslClientCert = "sslclientcert";
                    public const string SslClientCertKey = "sslclientcertkey";
                    public const string SslClientCertArchive = "sslclientcertarchive";
                    public const string StartupType = "startuptype";
                    public const string Url = "url";
                    public const string UserName = "username";
                    public const string WindowsLogonAccount = "windowslogonaccount";
                    public const string Work = "work";
                    public const string ClientId = "clientid";
                    public const string TenantId = "tenantid";

                    // Secret args. Must be added to the "Secrets" getter as well.
                    public const string Password = "password";
                    public const string ProxyPassword = "proxypassword";
                    public const string SslClientCertPassword = "sslclientcertpassword";
                    public const string Token = "token";
                    public const string WindowsLogonPassword = "windowslogonpassword";
                    public const string ClientSecret = "clientsecret";

                    public static string[] Secrets => new[]
                    {
                        Password,
                        ProxyPassword,
                        SslClientCertPassword,
                        Token,
                        WindowsLogonPassword,
                        ClientSecret,
                    };
                }

                public static class Commands
                {
                    public const string Configure = "configure";
                    public const string Remove = "remove";
                    public const string Run = "run";
                    public const string Warmup = "warmup";
                }

                //if you are adding a new flag, please make sure you update the
                //validFlags array as well present in the CommandSettings.cs
                public static class Flags
                {
                    public const string AcceptTeeEula = "acceptteeeula";
                    public const string AddDeploymentGroupTags = "adddeploymentgrouptags";
                    public const string AddMachineGroupTags = "addmachinegrouptags";
                    public const string AddEnvironmentVirtualMachineResourceTags = "addvirtualmachineresourcetags";
                    public const string AlwaysExtractTask = "alwaysextracttask";
                    public const string Commit = "commit";
                    public const string DeploymentGroup = "deploymentgroup";
                    public const string DeploymentPool = "deploymentpool";
                    public const string Diagnostics = "diagnostics";
                    public const string Environment = "environment";
                    public const string OverwriteAutoLogon = "overwriteautologon";
                    public const string GitUseSChannel = "gituseschannel";
                    public const string Help = "help";
                    public const string DisableLogUploads = "disableloguploads";
                    public const string MachineGroup = "machinegroup";
                    public const string Replace = "replace";
                    public const string NoRestart = "norestart";
                    public const string LaunchBrowser = "launchbrowser";
                    public const string Once = "once";
                    public const string RunAsAutoLogon = "runasautologon";
                    public const string RunAsService = "runasservice";
                    public const string PreventServiceStart = "preventservicestart";
                    public const string SslSkipCertValidation = "sslskipcertvalidation";
                    public const string Unattended = "unattended";
                    public const string Version = "version";
                    public const string EnableServiceSidTypeUnrestricted = "enableservicesidtypeunrestricted";
                }
            }

            public static class ReturnCode
            {
                public const int Success = 0;
                public const int TerminatedError = 1;
                public const int RetryableError = 2;
                public const int AgentUpdating = 3;
                public const int RunOnceAgentUpdating = 4;
                public const int SessionConflict = 5;
            }

            public static class AgentConfigurationProvider
            {
                public static readonly string BuildReleasesAgentConfiguration = "BuildReleasesAgentConfiguration";
                public static readonly string DeploymentAgentConfiguration = "DeploymentAgentConfiguration";
                public static readonly string SharedDeploymentAgentConfiguration = "SharedDeploymentAgentConfiguration";
                public static readonly string EnvironmentVMResourceConfiguration = "EnvironmentVMResourceConfiguration";
            }
        }

        public static class Build
        {
            public static readonly string NoCICheckInComment = "***NO_CI***";

            public static class Path
            {
                public static readonly string ArtifactsDirectory = "a";
                public static readonly string BinariesDirectory = "b";
                public static readonly string GarbageCollectionDirectory = "GC";
                public static readonly string LegacyArtifactsDirectory = "artifacts";
                public static readonly string LegacyStagingDirectory = "staging";
                public static readonly string SourceRootMappingDirectory = "SourceRootMapping";
                public static readonly string SourcesDirectory = "s";
                public static readonly string TestResultsDirectory = "TestResults";
                public static readonly string TopLevelTrackingConfigFile = "Mappings.json";
                public static readonly string TrackingConfigFile = "SourceFolder.json";
            }
        }

        public static class Configuration
        {
            public static readonly string AAD = "AAD";
            public static readonly string PAT = "PAT";
            public static readonly string Alternate = "ALT";
            public static readonly string Negotiate = "Negotiate";
            public static readonly string Integrated = "Integrated";
            public static readonly string OAuth = "OAuth";
            public static readonly string ServiceIdentity = "ServiceIdentity";
            public static readonly string ServicePrincipal = "SP";
        }

        public static class EndpointData
        {
            public static readonly string SourcesDirectory = "SourcesDirectory";
            public static readonly string SourceVersion = "SourceVersion";
            public static readonly string SourceBranch = "SourceBranch";
            public static readonly string SourceTfvcShelveset = "SourceTfvcShelveset";
            public static readonly string GatedShelvesetName = "GatedShelvesetName";
            public static readonly string GatedRunCI = "GatedRunCI";
        }

        public static class Expressions
        {
            public static readonly string Always = "always";
            public static readonly string Canceled = "canceled";
            public static readonly string Failed = "failed";
            public static readonly string Succeeded = "succeeded";
            public static readonly string SucceededOrFailed = "succeededOrFailed";
            public static readonly string Variables = "variables";
        }

        public static class Path
        {
            public static readonly string BinDirectory = "bin";
            public static readonly string DiagDirectory = "_diag";
            public static readonly string ExternalsDirectory = "externals";
            public static readonly string LegacyPSHostDirectory = "vstshost";
            public static readonly string ServerOMDirectory = "vstsom";
            public static readonly string TempDirectory = "_temp";
            public static readonly string TeeDirectory = "tee";
            public static readonly string TfDirectory = "tf";
            public static readonly string ToolDirectory = "_tool";
            public static readonly string TaskJsonFile = "task.json";
            public static readonly string TasksDirectory = "_tasks";
            public static readonly string TaskZipsDirectory = "_taskzips";
            public static readonly string UpdateDirectory = "_update";
            public static readonly string WorkDirectory = "_work";
        }

        public static class Release
        {
            public static readonly string Map = "Map";

            public static class Path
            {
                public static readonly string ArtifactsDirectory = "a";
                public static readonly string CommitsDirectory = "c";
                public static readonly string DefinitionMapping = "DefinitionMapping.json";
                public static readonly string ReleaseDirectoryPrefix = "r";
                public static readonly string ReleaseTempDirectoryPrefix = "t";
                public static readonly string RootMappingDirectory = "ReleaseRootMapping";
                public static readonly string TrackingConfigFile = "DefinitionMapping.json";
                public static readonly string GarbageCollectionDirectory = "GC";
            }
        }

        // Related to definition variables.
        public static class Variables
        {
            public static readonly string MacroPrefix = "$(";
            public static readonly string MacroSuffix = ")";

            public static class Agent
            {
                //
                // Keep alphabetical. If you add or remove a variable here, do the same in ReadOnlyVariables
                //
                public static readonly string AcceptTeeEula = "agent.acceptteeeula";
                public static readonly string BuildDirectory = "agent.builddirectory";
                public static readonly string CloudId = "agent.cloudid";
                public static readonly string ContainerId = "agent.containerid";
                public static readonly string ContainerMapping = "agent.containermapping";
                public static readonly string ContainerNetwork = "agent.containernetwork";
                public static readonly string Diagnostic = "agent.diagnostic";
                public static readonly string FixPossibleGitOutOfMemoryProblem = "FIX_POSSIBLE_GIT_OUT_OF_MEMORY_PROBLEM";
                public static readonly string HomeDirectory = "agent.homedirectory";
                public static readonly string Id = "agent.id";
                public static readonly string IsSelfHosted = "agent.isselfhosted";
                public static readonly string GitUseSChannel = "agent.gituseschannel";
                public static readonly string JobName = "agent.jobname";
                public static readonly string JobStatus = "agent.jobstatus";
                public static readonly string MachineName = "agent.machinename";
                public static readonly string Name = "agent.name";
                public static readonly string OS = "agent.os";
                public static readonly string OSArchitecture = "agent.osarchitecture";
                public static readonly string OSVersion = "agent.osversion";
                public static readonly string ProxyUrl = "agent.proxyurl";
                public static readonly string ProxyUsername = "agent.proxyusername";
                public static readonly string ProxyPassword = "agent.proxypassword";
                public static readonly string ProxyBypassList = "agent.proxybypasslist";
                public static readonly string RetainDefaultEncoding = "agent.retainDefaultEncoding";
                public static readonly string ReadOnlyVariables = "agent.readOnlyVariables";
                public static readonly string RootDirectory = "agent.RootDirectory";
                public static readonly string RunMode = "agent.runMode";
                public static readonly string ServerOMDirectory = "agent.ServerOMDirectory";
                public static readonly string ServicePortPrefix = "agent.services";
                public static readonly string SslCAInfo = "agent.cainfo";
                public static readonly string SslClientCert = "agent.clientcert";
                public static readonly string SslClientCertKey = "agent.clientcertkey";
                public static readonly string SslClientCertArchive = "agent.clientcertarchive";
                public static readonly string SslClientCertPassword = "agent.clientcertpassword";
                public static readonly string SslSkipCertValidation = "agent.skipcertvalidation";
                public static readonly string TempDirectory = "agent.TempDirectory";
                public static readonly string ToolsDirectory = "agent.ToolsDirectory";
                public static readonly string UseGitLongPaths = "USE_GIT_LONG_PATHS";
                public static readonly string UseGitSingleThread = "USE_GIT_SINGLE_THREAD";
                public static readonly string UseLatestGitVersion = "USE_LATEST_GIT_VERSION";
                public static readonly string Version = "agent.version";
                public static readonly string WorkFolder = "agent.workfolder";
                public static readonly string WorkingDirectory = "agent.WorkingDirectory";
                public static readonly string EnableAdditionalMaskingRegexes = "agent.enableadditionalmaskingregexes";
            }

            public static class Build
            {
                //
                // Keep alphabetical. If you add or remove a variable here, do the same in ReadOnlyVariables
                //
                public static readonly string ArtifactStagingDirectory = "build.artifactstagingdirectory";
                public static readonly string BinariesDirectory = "build.binariesdirectory";
                public static readonly string Clean = "build.clean";
                public static readonly string DefinitionName = "build.definitionname";
                public static readonly string GatedRunCI = "build.gated.runci";
                public static readonly string GatedShelvesetName = "build.gated.shelvesetname";
                public static readonly string Number = "build.buildNumber";
                public static readonly string RepoClean = "build.repository.clean";
                public static readonly string RepoGitSubmoduleCheckout = "build.repository.git.submodulecheckout";
                public static readonly string RepoId = "build.repository.id";
                public static readonly string RepoLocalPath = "build.repository.localpath";
                public static readonly string PipelineRepoName = "pipeline.repository.name";
                public static readonly string RepoName = "build.Repository.name";
                public static readonly string RepoProvider = "build.repository.provider";
                public static readonly string RepoTfvcWorkspace = "build.repository.tfvc.workspace";
                public static readonly string RepoUri = "build.repository.uri";
                public static readonly string SourceBranch = "build.sourcebranch";
                public static readonly string SourceTfvcShelveset = "build.sourcetfvcshelveset";
                public static readonly string SourceVersion = "build.sourceversion";
                public static readonly string SourceVersionMessage = "build.sourceVersionMessage";
                public static readonly string SourcesDirectory = "build.sourcesdirectory";
                public static readonly string StagingDirectory = "build.stagingdirectory";
                public static readonly string SyncSources = "build.syncSources";
                public static readonly string UseServerWorkspaces = "build.useserverworkspaces";
            }

            public static class Common
            {
                //
                // Keep alphabetical. If you add or remove a variable here, do the same in ReadOnlyVariables
                //
                public static readonly string TestResultsDirectory = "common.testresultsdirectory";
            }

            public static class Features
            {
                //
                // Keep alphabetical. If you add or remove a variable here, do the same in ReadOnlyVariables
                //
                public static readonly string BuildDirectoryClean = "agent.clean.buildDirectory";
                public static readonly string GitLfsSupport = "agent.source.git.lfs";
                public static readonly string GitShallowDepth = "agent.source.git.shallowFetchDepth";
                public static readonly string SkipSyncSource = "agent.source.skip";
                public static readonly string EnableAdditionalMaskingRegexes = "agent.enableadditionalmaskingregexes";
                public static readonly string UseMaskingPerformanceEnhancements = "agent.agentUseMaskingPerformanceEnhancements";
            }

            public static class Maintenance
            {
                //
                // Keep alphabetical. If you add or remove a variable here, do the same in ReadOnlyVariables
                //
                public static readonly string DeleteWorkingDirectoryDaysThreshold = "maintenance.deleteworkingdirectory.daysthreshold";
                public static readonly string JobTimeout = "maintenance.jobtimeoutinminutes";
            }

            public static class Pipeline
            {
                //
                // Keep alphabetical. If you add or remove a variable here, do the same in ReadOnlyVariables
                //
                public static readonly string Workspace = "pipeline.workspace";
            }

            public static class Release
            {
                //
                // Keep alphabetical. If you add or remove a variable here, do the same in ReadOnlyVariables
                //
                public static readonly string AgentReleaseDirectory = "agent.releaseDirectory";
                public static readonly string ArtifactsDirectory = "system.artifactsDirectory";
                public static readonly string AttemptNumber = "release.attemptNumber";
                public static readonly string DisableRobocopy = "release.disableRobocopy";
                public static readonly string ReleaseDefinitionId = "release.definitionId";
                public static readonly string ReleaseDefinitionName = "release.definitionName";
                public static readonly string ReleaseDescription = "release.releaseDescription";
                public static readonly string ReleaseDownloadBufferSize = "release.artifact.download.buffersize";
                public static readonly string ReleaseEnvironmentName = "release.environmentName";
                public static readonly string ReleaseEnvironmentUri = "release.environmentUri";
                public static readonly string ReleaseId = "release.releaseId";
                public static readonly string ReleaseName = "release.releaseName";
                public static readonly string ReleaseParallelDownloadLimit = "release.artifact.download.parallellimit";
                public static readonly string ReleaseRequestedForId = "release.requestedForId";
                public static readonly string ReleaseUri = "release.releaseUri";
                public static readonly string ReleaseWebUrl = "release.releaseWebUrl";
                public static readonly string RequestorId = "release.requestedFor";
                public static readonly string RobocopyMT = "release.robocopyMT";
                public static readonly string SkipArtifactsDownload = "release.skipartifactsDownload";
            }

            public static class System
            {
                //
                // Keep alphabetical. If you add or remove a variable here, do the same in ReadOnlyVariables
                //
                public static readonly string AccessToken = "system.accessToken";
                public static readonly string ArtifactsDirectory = "system.artifactsdirectory";
                public static readonly string CollectionId = "system.collectionid";
                public static readonly string Culture = "system.culture";
                public static readonly string Debug = "system.debug";
                public static readonly string DefaultWorkingDirectory = "system.defaultworkingdirectory";
                public static readonly string DefinitionId = "system.definitionid";
                public static readonly string DefinitionName = "system.definitionName";
                public static readonly string EnableAccessToken = "system.enableAccessToken";
                public static readonly string HostType = "system.hosttype";
                public static readonly string IsAzureVM = "system.isazurevm";
                public static readonly string IsDockerContainer = "system.isdockercontainer";
                public static readonly string JobAttempt = "system.jobAttempt";
                public static readonly string JobDisplayName = "system.jobDisplayName";
                public static readonly string JobId = "system.jobId";
                public static readonly string JobName = "system.jobName";
                public static readonly string PhaseAttempt = "system.phaseAttempt";
                public static readonly string PhaseDisplayName = "system.phaseDisplayName";
                public static readonly string PhaseName = "system.phaseName";
                public static readonly string PlanId = "system.planId";
                public static readonly string PreferGitFromPath = "system.prefergitfrompath";
                public static readonly string PullRequestTargetBranchName = "system.pullrequest.targetbranch";
                public static readonly string SelfManageGitCreds = "system.selfmanagegitcreds";
                public static readonly string ServerType = "system.servertype";
                public static readonly string SourceVersionMessage = "system.sourceVersionMessage";
                public static readonly string StageAttempt = "system.stageAttempt";
                public static readonly string StageDisplayName = "system.stageDisplayName";
                public static readonly string StageName = "system.stageName";
                public static readonly string TFServerUrl = "system.TeamFoundationServerUri"; // back compat variable, do not document
                public static readonly string TeamProject = "system.teamproject";
                public static readonly string TeamProjectId = "system.teamProjectId";
                public static readonly string WorkFolder = "system.workfolder";
            }

            public static class Task
            {
                //
                // Keep alphabetical. If you add or remove a variable here, do the same in ReadOnlyVariables
                //
                public static readonly string DisplayName = "task.displayname";
                /// <summary>
                /// Declares requirement to skip translating of strings into checkout tasks.
                /// It's required to prevent translating of agent system paths in container jobs.
                /// This is for internal agent usage, set up during task execution and is not indented to be used in
                /// cross-service communication/obtained by users.
                /// </summary>
                public static readonly string SkipTranslatorForCheckout = "task.skipTranslatorForCheckout";

                /// <summary>
                /// Declares requirement to publish telemetry for task or not. This is based on the IsServedOwned field in the TaskStep info
                /// which the agent obtains from the execution plan (AgentJobRequestMessage) and also some certain that required for telemetry.
                /// The main idea is to avoid publishing telemetry from the customer's tasks that is installed using TFS-CLI tool.
                /// </summary>
                public static readonly string PublishTelemetry = "task.publishTelemetry";
            }

            public static List<string> ReadOnlyVariables = new List<string>(){
                // Agent variables
                Agent.AcceptTeeEula,
                Agent.BuildDirectory,
                Agent.CloudId,
                Agent.ContainerId,
                Agent.ContainerMapping,
                Agent.ContainerNetwork,
                Agent.Diagnostic,
                Agent.FixPossibleGitOutOfMemoryProblem,
                Agent.GitUseSChannel,
                Agent.HomeDirectory,
                Agent.Id,
                Agent.IsSelfHosted,
                Agent.JobName,
                Agent.JobStatus,
                Agent.MachineName,
                Agent.Name,
                Agent.OS,
                Agent.OSArchitecture,
                Agent.OSVersion,
                Agent.ProxyBypassList,
                Agent.ProxyPassword,
                Agent.ProxyUrl,
                Agent.ProxyUsername,
                Agent.ReadOnlyVariables,
                Agent.RetainDefaultEncoding,
                Agent.RootDirectory,
                Agent.RunMode,
                Agent.ServerOMDirectory,
                Agent.ServicePortPrefix,
                Agent.SslCAInfo,
                Agent.SslClientCert,
                Agent.SslClientCertArchive,
                Agent.SslClientCertKey,
                Agent.SslClientCertPassword,
                Agent.SslSkipCertValidation,
                Agent.TempDirectory,
                Agent.ToolsDirectory,
                Agent.UseGitLongPaths,
                Agent.UseGitSingleThread,
                Agent.UseLatestGitVersion,
                Agent.Version,
                Agent.WorkFolder,
                Agent.WorkingDirectory,
                Agent.EnableAdditionalMaskingRegexes,
                // Build variables
                Build.ArtifactStagingDirectory,
                Build.BinariesDirectory,
                Build.Clean,
                Build.DefinitionName,
                Build.GatedRunCI,
                Build.GatedShelvesetName,
                Build.Number,
                Build.PipelineRepoName,
                Build.RepoClean,
                Build.RepoGitSubmoduleCheckout,
                Build.RepoId,
                Build.RepoLocalPath,
                Build.RepoName,
                Build.RepoProvider,
                Build.RepoTfvcWorkspace,
                Build.RepoUri,
                Build.SourceBranch,
                Build.SourceTfvcShelveset,
                Build.SourceVersion,
                Build.SourceVersionMessage,
                Build.SourcesDirectory,
                Build.StagingDirectory,
                Build.SyncSources,
                Build.UseServerWorkspaces,
                // Common variables
                Common.TestResultsDirectory,
                // Feature variables
                Features.BuildDirectoryClean,
                Features.GitLfsSupport,
                Features.GitShallowDepth,
                Features.SkipSyncSource,
                Features.UseMaskingPerformanceEnhancements,
                // Pipeline variables
                Pipeline.Workspace,
                // Release variables
                Release.AgentReleaseDirectory,
                Release.ArtifactsDirectory,
                Release.AttemptNumber,
                Release.DisableRobocopy,
                Release.ReleaseDefinitionId,
                Release.ReleaseDefinitionName,
                Release.ReleaseDescription,
                Release.ReleaseDownloadBufferSize,
                Release.ReleaseEnvironmentName,
                Release.ReleaseEnvironmentUri,
                Release.ReleaseId,
                Release.ReleaseName,
                Release.ReleaseParallelDownloadLimit,
                Release.ReleaseRequestedForId,
                Release.ReleaseUri,
                Release.ReleaseWebUrl,
                Release.RequestorId,
                Release.RobocopyMT,
                Release.SkipArtifactsDownload,
                // System variables
                System.AccessToken,
                System.ArtifactsDirectory,
                System.CollectionId,
                System.Culture,
                System.Debug,
                System.DefaultWorkingDirectory,
                System.DefinitionId,
                System.DefinitionName,
                System.EnableAccessToken,
                System.HostType,
                System.IsAzureVM,
                System.IsDockerContainer,
                System.JobAttempt,
                System.JobDisplayName,
                System.JobId,
                System.JobName,
                System.PhaseAttempt,
                System.PhaseDisplayName,
                System.PhaseName,
                System.PlanId,
                System.PreferGitFromPath,
                System.PullRequestTargetBranchName,
                System.SelfManageGitCreds,
                System.ServerType,
                System.SourceVersionMessage,
                System.StageAttempt,
                System.StageDisplayName,
                System.StageName,
                System.TFServerUrl,
                System.TeamProject,
                System.TeamProjectId,
                System.WorkFolder,
                // Task variables
                Task.DisplayName,
                Task.SkipTranslatorForCheckout,
                Task.PublishTelemetry
            };
        }
    }
}
