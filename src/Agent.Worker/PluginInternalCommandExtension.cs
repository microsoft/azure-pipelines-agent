// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.DistributedTask.Pipelines;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker.Build;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    public sealed class PluginInternalCommandExtension : BaseWorkerCommandExtension
    {
        public PluginInternalCommandExtension()
        {
            CommandArea = "plugininternal";
            SupportedHostTypes = HostTypes.Build;
            InstallWorkerCommand(new PluginInternalUpdateRepositoryPathCommand());
        }
    }

    public sealed class PluginInternalUpdateRepositoryPathCommand : IWorkerCommand
    {
        public string Name => "updaterepositorypath";
        public List<string> Aliases => null;
        public void Execute(IExecutionContext context, Command command)
        {
            ArgUtil.NotNull(context, nameof(context));
            ArgUtil.NotNull(command, nameof(command));

            var eventProperties = command.Properties;
            var data = command.Data;

            String alias;
            if (!eventProperties.TryGetValue(PluginInternalUpdateRepositoryEventProperties.Alias, out alias) || String.IsNullOrEmpty(alias))
            {
                throw new ArgumentNullException(StringUtil.Loc("MissingRepositoryAlias"));
            }

            var repository = context.Repositories.FirstOrDefault(x => string.Equals(x.Alias, alias, StringComparison.OrdinalIgnoreCase));
            if (repository == null)
            {
                throw new ArgumentNullException(StringUtil.Loc("RepositoryNotExist"));
            }

            if (string.IsNullOrEmpty(data))
            {
                throw new ArgumentNullException(StringUtil.Loc("MissingRepositoryPath"));
            }

            var currentPath = repository.Properties.Get<string>(RepositoryPropertyNames.Path);
            if (!string.Equals(data.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), currentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), IOUtil.FilePathStringComparison))
            {
                string repositoryPath = data.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                repository.Properties.Set<string>(RepositoryPropertyNames.Path, repositoryPath);

                bool isSelfRepo = RepositoryUtil.IsPrimaryRepositoryName(repository.Alias);
                bool hasMultipleCheckouts = RepositoryUtil.HasMultipleCheckouts(context.JobSettings);
                bool isDefaultWorkingDirectoryRepo = RepositoryUtil.IsWellKnownRepository(repository, RepositoryUtil.IsDefaultWorkingDirectoryRepository);

                var directoryManager = context.GetHostContext().GetService<IBuildDirectoryManager>();
                string _workDirectory = context.GetHostContext().GetDirectory(WellKnownDirectory.Work);
                var trackingConfig = directoryManager.UpdateDirectory(context, repository);

                if (isSelfRepo || !hasMultipleCheckouts)
                {
                    if (hasMultipleCheckouts)
                    {
                        // In Multi-checkout, we don't want to reset sources dir or default working dir.
                        // So, we will just reset the repo local path
                        string buildDirectory = context.Variables.Get(Constants.Variables.Pipeline.Workspace);
                        string repoRelativePath = directoryManager.GetRelativeRepositoryPath(buildDirectory, repositoryPath, context);

                        string sourcesDirectory = context.Variables.Get(Constants.Variables.Build.SourcesDirectory);
                        string repoLocalPath = context.Variables.Get(Constants.Variables.Build.RepoLocalPath);
                        string newRepoLocation = Path.Combine(_workDirectory, repoRelativePath);
                        // For saving backward compatibility with the behavior of the Build.RepoLocalPath that was before this PR https://github.com/microsoft/azure-pipelines-agent/pull/3237
                        // we need to deny updating of the variable in case the new path is the default location for the repository that is equal to sourcesDirectory/repository.Name
                        // since the variable already has the right value in this case and pointing to the default sources location
                        if (repoLocalPath == null
                            || !string.Equals(newRepoLocation, Path.Combine(sourcesDirectory, repository.Name), IOUtil.FilePathStringComparison))
                        {
                            context?.SetVariable(Constants.Variables.Build.RepoLocalPath, newRepoLocation, isFilePath: true);
                        }
                    }
                    else
                    {
                        // If we only have a single repository, then update all the paths to point to it.
                        context.SetVariable(Constants.Variables.Build.SourcesDirectory, repositoryPath, isFilePath: true);
                        context.SetVariable(Constants.Variables.Build.RepoLocalPath, repositoryPath, isFilePath: true);
                        context.SetVariable(Constants.Variables.System.DefaultWorkingDirectory, repositoryPath, isFilePath: true);
                    }
                }

                // Set the default working directory to the new location of this repo if this repo was marked as the one being the default working directory
                if (isDefaultWorkingDirectoryRepo)
                {
                    string buildDirectory = context.Variables.Get(Constants.Variables.Pipeline.Workspace);
                    string repoRelativePath = directoryManager.GetRelativeRepositoryPath(buildDirectory, repositoryPath, context);
                    string repoLocation = Path.Combine(_workDirectory, repoRelativePath);

                    context.SetVariable(Constants.Variables.System.DefaultWorkingDirectory, repoLocation, isFilePath: true);
                }
            }

            repository.Properties.Set("__AZP_READY", bool.TrueString);
        }
    }

    internal static class PluginInternalUpdateRepositoryEventProperties
    {
        public static readonly String Alias = "alias";
    }
}