// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Agent.Sdk.Knob;
using Microsoft.TeamFoundation.DistributedTask.Pipelines;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Microsoft.VisualStudio.Services.Agent.Worker.Build
{
    /// <summary>
    /// This class is used to prepare and maintain the folders that contain the pipeline workspace.
    /// Whenever possible, we attempt to reuse pipeline workspaces that were created in prior executions.
    /// However, these workspaces are per pipeline and cannot be reused if the list of repositories
    /// used by the pipeline has changed.
    /// </summary>
    [ServiceLocator(Default = typeof(BuildDirectoryManager))]
    public interface IBuildDirectoryManager : IAgentService
    {
        TrackingConfig PrepareDirectory(
            IExecutionContext executionContext,
            IList<RepositoryResource> repositories,
            WorkspaceOptions workspace);

        void CreateDirectory(
            IExecutionContext executionContext,
            string description, string path,
            bool deleteExisting);

        TrackingConfig UpdateDirectory(
            IExecutionContext executionContext,
            RepositoryResource updatedRepository);

        string GetRelativeRepositoryPath(
            string buildDirectory,
            string repositoryPath);
    }

    public sealed class BuildDirectoryManager : AgentService, IBuildDirectoryManager
    {
        public TrackingConfig PrepareDirectory(
            IExecutionContext executionContext,
            IList<RepositoryResource> repositories,
            WorkspaceOptions workspace)
        {
            // Validate parameters.
            Trace.Entering();
            ArgUtil.NotNull(executionContext, nameof(executionContext));
            ArgUtil.NotNull(executionContext.Variables, nameof(executionContext.Variables));
            ArgUtil.NotNull(repositories, nameof(repositories));

            var trackingManager = HostContext.GetService<ITrackingManager>();

            // Create the tracking config for this execution of the pipeline
            var agentSettings = HostContext.GetService<IConfigurationStore>().GetSettings();
            var shouldOverrideBuildDirectory = ShouldOverrideBuildDirectory(repositories, agentSettings);
            var newConfig = trackingManager.Create(executionContext, repositories, shouldOverrideBuildDirectory);

            // Load the tracking config from the last execution of the pipeline
            var existingConfig = trackingManager.LoadExistingTrackingConfig(executionContext);

            // If there aren't any major changes, merge the configurations and use the same workspace
            if (trackingManager.AreTrackingConfigsCompatible(executionContext, newConfig, existingConfig))
            {
                bool disableOverrideTfvcBuildDirectoryKnob = AgentKnobs.DisableOverrideTfvcBuildDirectory.GetValue(executionContext).AsBoolean();
                newConfig = trackingManager.MergeTrackingConfigs(executionContext, newConfig, existingConfig, shouldOverrideBuildDirectory && !disableOverrideTfvcBuildDirectoryKnob);
            }
            else if (existingConfig != null)
            {
                // If the previous config had different repos, get a new workspace folder and mark the old one for clean up
                trackingManager.MarkForGarbageCollection(executionContext, existingConfig);

                // If the config file was updated to a new config, we need to delete the legacy artifact/staging directories.
                // DeleteDirectory will check for the existence of the folders first.
                DeleteDirectory(
                    executionContext,
                    description: "legacy artifacts directory",
                    path: Path.Combine(existingConfig.BuildDirectory, Constants.Build.Path.LegacyArtifactsDirectory));
                DeleteDirectory(
                    executionContext,
                    description: "legacy staging directory",
                    path: Path.Combine(existingConfig.BuildDirectory, Constants.Build.Path.LegacyStagingDirectory));
            }

            // Save any changes to the config file
            trackingManager.UpdateTrackingConfig(executionContext, newConfig);

            // Prepare the build directory.
            // There are 2 ways to provide build directory clean policy.
            //     1> set definition variable build.clean or agent.clean.buildDirectory. (on-prem user need to use this, since there is no Web UI in TFS 2016)
            //     2> select source clean option in definition repository tab. (VSTS will have this option in definition designer UI)
            BuildCleanOption cleanOption = GetBuildDirectoryCleanOption(executionContext, workspace);

            CreateDirectory(
                executionContext,
                description: "build directory",
                path: Path.Combine(HostContext.GetDirectory(WellKnownDirectory.Work), newConfig.BuildDirectory),
                deleteExisting: cleanOption == BuildCleanOption.All);
            CreateDirectory(
                executionContext,
                description: "artifacts directory",
                path: Path.Combine(HostContext.GetDirectory(WellKnownDirectory.Work), newConfig.ArtifactsDirectory),
                deleteExisting: true);
            CreateDirectory(
                executionContext,
                description: "test results directory",
                path: Path.Combine(HostContext.GetDirectory(WellKnownDirectory.Work), newConfig.TestResultsDirectory),
                deleteExisting: true);
            CreateDirectory(
                executionContext,
                description: "binaries directory",
                path: Path.Combine(HostContext.GetDirectory(WellKnownDirectory.Work), newConfig.BuildDirectory, Constants.Build.Path.BinariesDirectory),
                deleteExisting: cleanOption == BuildCleanOption.Binary);

            var defaultSourceDirectory = Path.Combine(HostContext.GetDirectory(WellKnownDirectory.Work), newConfig.SourcesDirectory);
            CreateDirectory(
                executionContext,
                description: "source directory",
                path: defaultSourceDirectory,
                deleteExisting: cleanOption == BuildCleanOption.Source);

            // Set the default clone path for each repository (the Checkout task may override this later)
            foreach (var repository in repositories)
            {
                string repoPath = GetDefaultRepositoryPath(executionContext, repository, newConfig);

                if (!string.Equals(repoPath, defaultSourceDirectory, StringComparison.Ordinal))
                {
                    CreateDirectory(
                        executionContext,
                        description: "repository source directory",
                        path: repoPath,
                        deleteExisting: cleanOption == BuildCleanOption.Source);
                }

                Trace.Info($"Set repository path for repository {repository.Alias} to '{repoPath}'");
                repository.Properties.Set<string>(RepositoryPropertyNames.Path, repoPath);
            }

            return newConfig;
        }

        public TrackingConfig UpdateDirectory(
            IExecutionContext executionContext,
            RepositoryResource updatedRepository)
        {
            // Validate parameters.
            Trace.Entering();
            ArgUtil.NotNull(executionContext, nameof(executionContext));
            ArgUtil.NotNull(executionContext.Variables, nameof(executionContext.Variables));
            ArgUtil.NotNull(updatedRepository, nameof(updatedRepository));
            var trackingManager = HostContext.GetService<ITrackingManager>();

            // Determine new repository path
            var repoPath = updatedRepository.Properties.Get<string>(RepositoryPropertyNames.Path);
            ArgUtil.NotNullOrEmpty(repoPath, nameof(repoPath));
            Trace.Info($"Update repository path for repository {updatedRepository.Alias} to '{repoPath}'");

            // Get the config
            var trackingConfig = trackingManager.LoadExistingTrackingConfig(executionContext);

            // Update the repositoryInfo on the config
            string buildDirectory = Path.Combine(HostContext.GetDirectory(WellKnownDirectory.Work), trackingConfig.BuildDirectory);
            string relativeRepoPath = GetRelativeRepositoryPath(buildDirectory, repoPath);
            var effectedRepo = trackingConfig.RepositoryTrackingInfo.FirstOrDefault(r => string.Equals(r.Identifier, updatedRepository.Alias, StringComparison.OrdinalIgnoreCase));
            if (effectedRepo != null)
            {
                Trace.Info($"Found repository {updatedRepository.Alias}'");
                effectedRepo.SourcesDirectory = relativeRepoPath;
            }

            // Also update the SourcesDirectory on the tracking info if there is only one repo.
            if (trackingConfig.RepositoryTrackingInfo.Count == 1)
            {
                Trace.Info($"Updating SourcesDirectory to {updatedRepository.Alias}'");
                trackingConfig.SourcesDirectory = relativeRepoPath;
            }

            // Update the tracking config files.
            Trace.Verbose("Updating job run properties.");
            trackingManager.UpdateTrackingConfig(executionContext, trackingConfig);

            return trackingConfig;
        }

        public string GetRelativeRepositoryPath(
            string buildDirectory,
            string repositoryPath)
        {
            var maxRootDirectory = buildDirectory;
            var workDirectory = HostContext.GetDirectory(WellKnownDirectory.Work);
            var allowWorkDirectoryRepositories = HostContext.GetService<IConfigurationStore>().GetSettings().AllowWorkDirectoryRepositories;

            ArgUtil.NotNullOrEmpty(buildDirectory, nameof(buildDirectory));
            ArgUtil.NotNullOrEmpty(repositoryPath, nameof(repositoryPath));

            // resolve any potentially left over relative part of the path
            repositoryPath = Path.GetFullPath(repositoryPath);

            if (allowWorkDirectoryRepositories)
            {
                maxRootDirectory = workDirectory;
            }

            if (repositoryPath.StartsWith(maxRootDirectory + Path.DirectorySeparatorChar) || repositoryPath.StartsWith(maxRootDirectory + Path.AltDirectorySeparatorChar))
            {
                // The sourcesDirectory in tracking file is a relative path to agent's work folder.
                return repositoryPath.Substring(workDirectory.Length + 1).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            else
            {
                if (allowWorkDirectoryRepositories)
                {
                    throw new ArgumentException($"Repository path '{repositoryPath}' should be located under agent's work directory '{workDirectory}'.");
                }
                else
                {
                    throw new ArgumentException($"Repository path '{repositoryPath}' should be located under agent's build directory '{buildDirectory}'.");
                }
            }
        }

        private bool ShouldOverrideBuildDirectory(IList<RepositoryResource> repositories, AgentSettings settings)
        {
            if (repositories?.Count == 1 && repositories[0].Type == RepositoryTypes.Tfvc)
            {
                return settings.IsMSHosted;
            }
            else
            {
                return false;
            }
        }

        public void CreateDirectory(IExecutionContext executionContext, string description, string path, bool deleteExisting)
        {
            ArgUtil.NotNull(executionContext, nameof(executionContext));

            // Delete.
            if (deleteExisting)
            {
                executionContext.Debug($"Delete existing {description}: '{path}'");
                DeleteDirectory(executionContext, description, path);
            }

            // Create.
            if (!Directory.Exists(path))
            {
                executionContext.Debug($"Creating {description}: '{path}'");
                Trace.Info($"Creating {description}.");
                Directory.CreateDirectory(path);
            }
        }

        private void DeleteDirectory(IExecutionContext executionContext, string description, string path)
        {
            Trace.Info($"Checking if {description} exists: '{path}'");
            if (Directory.Exists(path))
            {
                executionContext.Debug($"Deleting {description}: '{path}'");
                IOUtil.DeleteDirectory(path, executionContext.CancellationToken);
            }
        }

        // Prefer variable over endpoint data when get build directory clean option.
        // Prefer agent.clean.builddirectory over build.clean when use variable
        // available value for build.clean or agent.clean.builddirectory:
        //      Delete entire build directory if build.clean=all is set.
        //      Recreate binaries dir if clean=binary is set.
        //      Recreate source dir if clean=src is set.
        private BuildCleanOption GetBuildDirectoryCleanOption(IExecutionContext executionContext, WorkspaceOptions workspace)
        {
            BuildCleanOption? cleanOption = executionContext.Variables.Build_Clean;
            if (cleanOption != null)
            {
                return cleanOption.Value;
            }

            if (workspace == null)
            {
                return BuildCleanOption.None;
            }
            else
            {
                Dictionary<string, string> workspaceClean = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                workspaceClean["clean"] = workspace.Clean;
                executionContext.Variables.ExpandValues(target: workspaceClean);
                VarUtil.ExpandEnvironmentVariables(HostContext, target: workspaceClean);
                string expandedClean = workspaceClean["clean"];
                if (string.Equals(expandedClean, PipelineConstants.WorkspaceCleanOptions.All, StringComparison.OrdinalIgnoreCase))
                {
                    return BuildCleanOption.All;
                }
                else if (string.Equals(expandedClean, PipelineConstants.WorkspaceCleanOptions.Resources, StringComparison.OrdinalIgnoreCase))
                {
                    return BuildCleanOption.Source;
                }
                else if (string.Equals(expandedClean, PipelineConstants.WorkspaceCleanOptions.Outputs, StringComparison.OrdinalIgnoreCase))
                {
                    return BuildCleanOption.Binary;
                }
                else
                {
                    return BuildCleanOption.None;
                }
            }
        }

        private string GetDefaultRepositoryPath(
            IExecutionContext executionContext,
            RepositoryResource repository,
            TrackingConfig newConfig
            )
        {
            string repoPath = String.Empty;
            string workDirectory = HostContext.GetDirectory(WellKnownDirectory.Work);

            if (RepositoryUtil.HasMultipleCheckouts(executionContext.JobSettings))
            {
                // If we have multiple checkouts they should all be rooted to the sources directory (_work/1/s/repo1)
                var repoSourceDirectory = newConfig?.RepositoryTrackingInfo.Where(item => string.Equals(item.Identifier, repository.Alias, StringComparison.OrdinalIgnoreCase)).Select(item => item.SourcesDirectory).FirstOrDefault();
                if (repoSourceDirectory != null)
                {
                    repoPath = Path.Combine(workDirectory, repoSourceDirectory);
                }
                else
                {
                    repoPath = Path.Combine(workDirectory, newConfig.SourcesDirectory, RepositoryUtil.GetCloneDirectory(repository));
                }
            }
            else
            {
                // For single checkouts, the repository is rooted to the sources folder (_work/1/s)
                repoPath = Path.Combine(workDirectory, newConfig.SourcesDirectory);
            }

            return repoPath;
        }
    }
}