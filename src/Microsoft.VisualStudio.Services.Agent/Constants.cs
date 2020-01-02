// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Agent.Sdk;
using System.Collections.Generic;

namespace Microsoft.VisualStudio.Services.Agent
{
    public enum RunMode
    {
        Normal, // Keep "Normal" first (default value).
        Local,
    }

    public enum WellKnownDirectory
    {
        Bin,
        Diag,
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

        // Environment variable set on hosted Azure Pipelines images to
        // store the version of the image
        public static readonly string ImageVersionVariable = "ImageVersion";

        public static class Agent
        {
            public static readonly TimeSpan ExitOnUnloadTimeout = TimeSpan.FromSeconds(30);

            public static class CommandLine
            {
                //if you are adding a new arg, please make sure you update the
                //validArgs array as well present in the CommandSettings.cs
                public static class Args
                {
                    public static readonly string Agent = "agent";
                    public static readonly string Auth = "auth";
                    public static readonly string CollectionName = "collectionname";
                    public static readonly string DeploymentGroupName = "deploymentgroupname";
                    public static readonly string DeploymentPoolName = "deploymentpoolname";
                    public static readonly string DeploymentGroupTags = "deploymentgrouptags";
                    public static readonly string EnvironmentName = "environmentname";
                    public static readonly string EnvironmentVMResourceTags = "virtualmachineresourcetags";
                    public static readonly string MachineGroupName = "machinegroupname";
                    public static readonly string MachineGroupTags = "machinegrouptags";
                    public static readonly string Matrix = "matrix";
                    public static readonly string MonitorSocketAddress = "monitorsocketaddress";
                    public static readonly string NotificationPipeName = "notificationpipename";
                    public static readonly string NotificationSocketAddress = "notificationsocketaddress";
                    public static readonly string Phase = "phase";
                    public static readonly string Pool = "pool";
                    public static readonly string ProjectName = "projectname";
                    public static readonly string ProxyUrl = "proxyurl";
                    public static readonly string ProxyUserName = "proxyusername";
                    public static readonly string SslCACert = "sslcacert";
                    public static readonly string SslClientCert = "sslclientcert";
                    public static readonly string SslClientCertKey = "sslclientcertkey";
                    public static readonly string SslClientCertArchive = "sslclientcertarchive";
                    public static readonly string SslClientCertPassword = "sslclientcertpassword";
                    public static readonly string StartupType = "startuptype";
                    public static readonly string Url = "url";
                    public static readonly string UserName = "username";
                    public static readonly string WindowsLogonAccount = "windowslogonaccount";
                    public static readonly string Work = "work";
                    public static readonly string Yml = "yml";

                    // Secret args. Must be added to the "Secrets" getter as well.
                    public static readonly string Password = "password";
                    public static readonly string ProxyPassword = "proxypassword";
                    public static readonly string Token = "token";
                    public static readonly string WindowsLogonPassword = "windowslogonpassword";
                    public static string[] Secrets => new[]
                    {
                        Password,
                        ProxyPassword,
                        SslClientCertPassword,
                        Token,
                        WindowsLogonPassword,
                    };
                }

                public static class Commands
                {
                    public static readonly string Configure = "configure";
                    public static readonly string LocalRun = "localRun";
                    public static readonly string Remove = "remove";
                    public static readonly string Run = "run";
                    public static readonly string Warmup = "warmup";
                }

                //if you are adding a new flag, please make sure you update the
                //validFlags array as well present in the CommandSettings.cs
                public static class Flags
                {
                    public static readonly string AcceptTeeEula = "acceptteeeula";
                    public static readonly string AddDeploymentGroupTags = "adddeploymentgrouptags";
                    public static readonly string AddMachineGroupTags = "addmachinegrouptags";
                    public static readonly string AddEnvironmentVirtualMachineResourceTags = "addvirtualmachineresourcetags";
                    public static readonly string Commit = "commit";
                    public static readonly string DeploymentGroup = "deploymentgroup";
                    public static readonly string DeploymentPool = "deploymentpool";
                    public static readonly string Environment = "environment";
                    public static readonly string OverwriteAutoLogon = "overwriteautologon";
                    public static readonly string GitUseSChannel = "gituseschannel";
                    public static readonly string Help = "help";
                    public static readonly string MachineGroup = "machinegroup";
                    public static readonly string Replace = "replace";
                    public static readonly string NoRestart = "norestart";
                    public static readonly string LaunchBrowser = "launchbrowser";
                    public static readonly string Once = "once";
                    public static readonly string RunAsAutoLogon = "runasautologon";
                    public static readonly string RunAsService = "runasservice";
                    public static readonly string SslSkipCertValidation = "sslskipcertvalidation";
                    public static readonly string Unattended = "unattended";
                    public static readonly string Version = "version";
                    public static readonly string WhatIf = "whatif";
                }
            }

            public static class ReturnCode
            {
                public const int Success = 0;
                public const int TerminatedError = 1;
                public const int RetryableError = 2;
                public const int AgentUpdating = 3;
                public const int RunOnceAgentUpdating = 4;
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
                public static readonly string AllowAllEndpoints = "agent.allowAllEndpoints"; // remove after sprint 120 or so.
                public static readonly string AllowAllSecureFiles = "agent.allowAllSecureFiles"; // remove after sprint 121 or so.
                public static readonly string BuildDirectory = "agent.builddirectory";
                public static readonly string ContainerId = "agent.containerid";
                public static readonly string ContainerNetwork = "agent.containernetwork";
                public static readonly string Diagnostic = "agent.diagnostic";
                public static readonly string HomeDirectory = "agent.homedirectory";
                public static readonly string Id = "agent.id";
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
                public static readonly string RunMode = "agent.runmode";
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
                public static readonly string Version = "agent.version";
                public static readonly string WorkFolder = "agent.workfolder";
                public static readonly string WorkingDirectory = "agent.WorkingDirectory";
            }

            public static class Build
            {
                //
                // Keep alphabetical. If you add or remove a variable here, do the same in ReadOnlyVariables
                //
                public static readonly string ArtifactStagingDirectory = "build.artifactstagingdirectory";
                public static readonly string BinariesDirectory = "build.binariesdirectory";
                public static readonly string Number = "build.buildNumber";
                public static readonly string Clean = "build.clean";
                public static readonly string DefinitionName = "build.definitionname";
                public static readonly string GatedRunCI = "build.gated.runci";
                public static readonly string GatedShelvesetName = "build.gated.shelvesetname";
                public static readonly string RepoClean = "build.repository.clean";
                public static readonly string RepoGitSubmoduleCheckout = "build.repository.git.submodulecheckout";
                public static readonly string RepoId = "build.repository.id";
                public static readonly string RepoLocalPath = "build.repository.localpath";
                public static readonly string RepoName = "build.Repository.name";
                public static readonly string RepoProvider = "build.repository.provider";
                public static readonly string RepoTfvcWorkspace = "build.repository.tfvc.workspace";
                public static readonly string RepoUri = "build.repository.uri";
                public static readonly string SourceBranch = "build.sourcebranch";
                public static readonly string SourceTfvcShelveset = "build.sourcetfvcshelveset";
                public static readonly string SourceVersion = "build.sourceversion";
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
                public static readonly string ReleaseDefinitionName = "release.definitionName";
                public static readonly string ReleaseEnvironmentName = "release.environmentName";
                public static readonly string ReleaseEnvironmentUri = "release.environmentUri";
                public static readonly string ReleaseDefinitionId = "release.definitionId";
                public static readonly string ReleaseDescription = "release.releaseDescription";
                public static readonly string ReleaseId = "release.releaseId";
                public static readonly string ReleaseName = "release.releaseName";
                public static readonly string ReleaseRequestedForId = "release.requestedForId";
                public static readonly string ReleaseUri = "release.releaseUri";
                public static readonly string ReleaseDownloadBufferSize = "release.artifact.download.buffersize";
                public static readonly string ReleaseParallelDownloadLimit = "release.artifact.download.parallellimit";
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
                public static readonly string EnableAccessToken = "system.enableAccessToken";
                public static readonly string HostType = "system.hosttype";
                public static readonly string JobAttempt = "system.jobAttempt";
                public static readonly string JobId = "system.jobId";
                public static readonly string JobName = "system.jobName";
                public static readonly string PhaseAttempt = "system.phaseAttempt";
                public static readonly string PhaseDisplayName = "system.phaseDisplayName";
                public static readonly string PhaseName = "system.phaseName";
                public static readonly string PreferGitFromPath = "system.prefergitfrompath";
                public static readonly string PullRequestTargetBranchName = "system.pullrequest.targetbranch";
                public static readonly string SelfManageGitCreds = "system.selfmanagegitcreds";
                public static readonly string ServerType = "system.servertype";
                public static readonly string StageAttempt = "system.stageAttempt";
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
            }

            public static List<string> ReadOnlyVariables = new List<string>(){
                // Agent variables
                Agent.AcceptTeeEula,
                Agent.AllowAllEndpoints,
                Agent.AllowAllSecureFiles,
                Agent.BuildDirectory,
                Agent.ContainerId,
                Agent.ContainerNetwork,
                Agent.Diagnostic,
                Agent.HomeDirectory,
                Agent.Id,
                Agent.GitUseSChannel,
                Agent.JobName,
                Agent.JobStatus,
                Agent.MachineName,
                Agent.Name,
                Agent.OS,
                Agent.OSArchitecture,
                Agent.OSVersion,
                Agent.ProxyUrl,
                Agent.ProxyUsername,
                Agent.ProxyPassword,
                Agent.ProxyBypassList,
                Agent.RetainDefaultEncoding,
                Agent.ReadOnlyVariables,
                Agent.RootDirectory,
                Agent.RunMode,
                Agent.ServerOMDirectory,
                Agent.ServicePortPrefix,
                Agent.SslCAInfo,
                Agent.SslClientCert,
                Agent.SslClientCertKey,
                Agent.SslClientCertArchive,
                Agent.SslClientCertPassword,
                Agent.SslSkipCertValidation,
                Agent.TempDirectory,
                Agent.ToolsDirectory,
                Agent.Version,
                Agent.WorkFolder,
                Agent.WorkingDirectory,
                // Build variables
                Build.ArtifactStagingDirectory,
                Build.BinariesDirectory,
                Build.Number,
                Build.Clean,
                Build.DefinitionName,
                Build.GatedRunCI,
                Build.GatedShelvesetName,
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
                // Pipeline variables
                Pipeline.Workspace,
                // Release variables
                Release.AgentReleaseDirectory,
                Release.ArtifactsDirectory,
                Release.AttemptNumber,
                Release.DisableRobocopy,
                Release.ReleaseDefinitionName,
                Release.ReleaseEnvironmentName,
                Release.ReleaseEnvironmentUri,
                Release.ReleaseDefinitionId,
                Release.ReleaseDescription,
                Release.ReleaseId,
                Release.ReleaseName,
                Release.ReleaseRequestedForId,
                Release.ReleaseUri,
                Release.ReleaseDownloadBufferSize,
                Release.ReleaseParallelDownloadLimit,
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
                System.EnableAccessToken,
                System.HostType,
                System.JobAttempt,
                System.JobId,
                System.JobName,
                System.PhaseAttempt,
                System.PhaseDisplayName,
                System.PhaseName,
                System.PreferGitFromPath,
                System.PullRequestTargetBranchName,
                System.SelfManageGitCreds,
                System.ServerType,
                System.StageAttempt,
                System.StageName,
                System.TFServerUrl,
                System.TeamProject,
                System.TeamProjectId,
                System.WorkFolder,
                // Task variables
                Task.DisplayName
            };
        }
    }
}
