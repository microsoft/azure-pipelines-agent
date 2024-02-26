// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using Agent.Sdk.Knob;
using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.IO;

namespace Microsoft.VisualStudio.Services.Agent.Worker.Build
{
    [ServiceLocator(Default = typeof(GitCommandManager))]
    public interface IGitCommandManager : IAgentService
    {
        bool EnsureGitVersion(Version requiredVersion, bool throwOnNotMatch);

        bool EnsureGitLFSVersion(Version requiredVersion, bool throwOnNotMatch);

        (string resolvedGitPath, string resolvedGitLfsPath) GetInternalGitPaths(IExecutionContext context, bool useLatestGitVersion);

        // setup git execution info, git location, version, useragent, execpath
        Task LoadGitExecutionInfo(IExecutionContext context, bool useBuiltInGit, Dictionary<string, string> gitEnv = null);

        // git init <LocalDir>
        Task<int> GitInit(IExecutionContext context, string repositoryPath);

        // git fetch --tags --prune --progress --no-recurse-submodules [--depth=15] origin [+refs/pull/*:refs/remote/pull/*]
        Task<int> GitFetch(IExecutionContext context, string repositoryPath, string remoteName, int fetchDepth, bool fetchTags, List<string> refSpec, string additionalCommandLine, CancellationToken cancellationToken);

        // git lfs fetch origin [ref]
        Task<int> GitLFSFetch(IExecutionContext context, string repositoryPath, string remoteName, string refSpec, string additionalCommandLine, CancellationToken cancellationToken);

        // git checkout -f --progress <commitId/branch>
        Task<int> GitCheckout(IExecutionContext context, string repositoryPath, string committishOrBranchSpec, CancellationToken cancellationToken);

        // git clean -ffdx
        Task<int> GitClean(IExecutionContext context, string repositoryPath);

        // git reset --hard HEAD
        Task<int> GitReset(IExecutionContext context, string repositoryPath);

        // get remote add <origin> <url>
        Task<int> GitRemoteAdd(IExecutionContext context, string repositoryPath, string remoteName, string remoteUrl);

        // get remote set-url <origin> <url>
        Task<int> GitRemoteSetUrl(IExecutionContext context, string repositoryPath, string remoteName, string remoteUrl);

        // get remote set-url --push <origin> <url>
        Task<int> GitRemoteSetPushUrl(IExecutionContext context, string repositoryPath, string remoteName, string remoteUrl);

        // git submodule foreach --recursive "git clean -ffdx"
        Task<int> GitSubmoduleClean(IExecutionContext context, string repositoryPath);

        // git submodule foreach --recursive "git reset --hard HEAD"
        Task<int> GitSubmoduleReset(IExecutionContext context, string repositoryPath);

        // git submodule update --init --force [--depth=15] [--recursive]
        Task<int> GitSubmoduleUpdate(IExecutionContext context, string repositoryPath, int fetchDepth, string additionalCommandLine, bool recursive, CancellationToken cancellationToken);

        // git submodule sync [--recursive]
        Task<int> GitSubmoduleSync(IExecutionContext context, string repositoryPath, bool recursive, CancellationToken cancellationToken);

        // git config --get remote.origin.url
        Task<Uri> GitGetFetchUrl(IExecutionContext context, string repositoryPath);

        // git config <key> <value>
        Task<int> GitConfig(IExecutionContext context, string repositoryPath, string configKey, string configValue);

        // git config --get-all <key>
        Task<bool> GitConfigExist(IExecutionContext context, string repositoryPath, string configKey);

        // git config --unset-all <key>
        Task<int> GitConfigUnset(IExecutionContext context, string repositoryPath, string configKey);

        // git config gc.auto 0
        Task<int> GitDisableAutoGC(IExecutionContext context, string repositoryPath);

        // git lfs version
        Task<Version> GitLfsVersion(IExecutionContext context);

        // git lfs install --local
        Task<int> GitLFSInstall(IExecutionContext context, string repositoryPath);

        // git lfs logs last
        Task<int> GitLFSLogs(IExecutionContext context, string repositoryPath);

        // git repack -adfl
        Task<int> GitRepack(IExecutionContext context, string repositoryPath);

        // git prune
        Task<int> GitPrune(IExecutionContext context, string repositoryPath);

        // git lfs prune
        Task<int> GitLFSPrune(IExecutionContext context, string repositoryPath);

        // git count-objects -v -H
        Task<int> GitCountObjects(IExecutionContext context, string repositoryPath);

        // git version
        Task<Version> GitVersion(IExecutionContext context);

        // git status
        Task<int> GitStatus(IExecutionContext context, string repositoryPath);
    }

    public class GitCommandManager : AgentService, IGitCommandManager
    {
        private static Encoding _encoding
        {
            get => PlatformUtil.RunningOnWindows
                ? Encoding.UTF8
                : null;
        }
        private string _gitHttpUserAgentEnv = null;
        private string _gitPath = null;
        private Version _gitVersion = null;
        private string _gitLfsPath = null;
        private Version _gitLfsVersion = null;

        private Dictionary<string, string> _gitEnv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "GIT_TERMINAL_PROMPT", "0" },
        };

        public bool EnsureGitVersion(Version requiredVersion, bool throwOnNotMatch)
        {
            ArgUtil.NotNull(_gitVersion, nameof(_gitVersion));

            if (_gitPath == null)
            {
                throw new InvalidOperationException("Could not find Git installed on the system. Please make sure GIT is installed and available in the PATH.");
            }

            if (_gitVersion < requiredVersion && throwOnNotMatch)
            {
                throw new NotSupportedException(StringUtil.Loc("MinRequiredGitVersion", requiredVersion, _gitPath, _gitVersion));
            }

            return _gitVersion >= requiredVersion;
        }

        public bool EnsureGitLFSVersion(Version requiredVersion, bool throwOnNotMatch)
        {
            ArgUtil.NotNull(_gitLfsVersion, nameof(_gitLfsVersion));

            if (_gitLfsPath == null)
            {
                throw new InvalidOperationException("Could not find Git LFS installed on the system. Please make sure GIT LFS is installed and available in the PATH.");
            }

            if (_gitLfsVersion < requiredVersion && throwOnNotMatch)
            {
                throw new NotSupportedException(StringUtil.Loc("MinRequiredGitLfsVersion", requiredVersion, _gitLfsPath, _gitLfsVersion));
            }

            return _gitLfsVersion >= requiredVersion;
        }

        public (string resolvedGitPath, string resolvedGitLfsPath) GetInternalGitPaths(IExecutionContext context, bool useLatestGitVersion)
        {
            string externalsDirectoryPath = HostContext.GetDirectory(WellKnownDirectory.Externals);
            ArgUtil.NotNullOrEmpty(externalsDirectoryPath, nameof(WellKnownDirectory.Externals));

            string gitPath;

            if (useLatestGitVersion)
            {
                gitPath = Path.Combine(externalsDirectoryPath, "ff_git", "cmd", $"git.exe");
            }
            else
            {
                gitPath = Path.Combine(externalsDirectoryPath, "git", "cmd", $"git.exe");
            }

            context.Debug($@"The useLatestGitVersion property is set to ""{useLatestGitVersion}"" therefore the Git path is ""{gitPath}""");

            string gitLfsPath;

            if (PlatformUtil.BuiltOnX86)
            {
                gitLfsPath = Path.Combine(externalsDirectoryPath, "git", "mingw32", "bin", $"git-lfs.exe");
            }
            else
            {
                gitLfsPath = Path.Combine(externalsDirectoryPath, "git", "mingw64", "bin", $"git-lfs.exe");
            }

            return (gitPath, gitLfsPath);
        }

        public async Task LoadGitExecutionInfo(IExecutionContext context, bool useBuiltInGit, Dictionary<string, string> gitEnv = null)
        {
            if (gitEnv != null)
            {
                foreach (var env in gitEnv)
                {
                    if (!string.IsNullOrEmpty(env.Key))
                    {
                        _gitEnv[env.Key] = env.Value ?? string.Empty;
                    }
                }
            }

            // Resolve the location of git.
            if (useBuiltInGit)
            {
                _gitPath = null;

                // The Windows agent ships a copy of Git
                if (PlatformUtil.RunningOnWindows)
                {
                    context.Debug("Git paths are resolving from internal dependencies");

                    var (resolvedGitPath, resolvedGitLfsPath) = GetInternalGitPaths(
                        context,
                        AgentKnobs.UseLatestGitVersion.GetValue(context).AsBoolean());

                    _gitPath = resolvedGitPath;
                    _gitLfsPath = resolvedGitLfsPath;

                    // Prepend the PATH.
                    context.Output(StringUtil.Loc("Prepending0WithDirectoryContaining1", Constants.PathVariable, Path.GetFileName(_gitPath)));
                    // We need to prepend git-lfs path first so that we call
                    // externals/git/cmd/git.exe instead of externals/git/mingw**/bin/git.exe
                    PathUtil.PrependPath(Path.GetDirectoryName(_gitLfsPath));
                    PathUtil.PrependPath(Path.GetDirectoryName(_gitPath));
                    context.Debug($"{Constants.PathVariable}: '{Environment.GetEnvironmentVariable(Constants.PathVariable)}'");
                }
            }
            else
            {
                _gitPath = WhichUtil.Which("git", require: true, trace: Trace);
                _gitLfsPath = WhichUtil.Which("git-lfs", require: false, trace: Trace);
            }

            ArgUtil.File(_gitPath, nameof(_gitPath));

            // Get the Git version.
            _gitVersion = await GitVersion(context);
            ArgUtil.NotNull(_gitVersion, nameof(_gitVersion));
            context.Debug($"Detect git version: {_gitVersion.ToString()}.");

            // Get the Git-LFS version if git-lfs exist in %PATH%.
            if (!string.IsNullOrEmpty(_gitLfsPath))
            {
                _gitLfsVersion = await GitLfsVersion(context);
                context.Debug($"Detect git-lfs version: '{_gitLfsVersion?.ToString() ?? string.Empty}'.");
            }

            // required 2.0, all git operation commandline args need min git version 2.0
            Version minRequiredGitVersion = new Version(2, 0);
            EnsureGitVersion(minRequiredGitVersion, throwOnNotMatch: true);

            // suggest user upgrade to 2.17 for better git experience
            Version recommendGitVersion = new Version(2, 17);
            if (!EnsureGitVersion(recommendGitVersion, throwOnNotMatch: false))
            {
                context.Output(StringUtil.Loc("UpgradeToLatestGit", recommendGitVersion, _gitVersion));
            }

            // Set the user agent.
            _gitHttpUserAgentEnv = $"git/{_gitVersion.ToString()} (vsts-agent-git/{BuildConstants.AgentPackage.Version})";
            context.Debug($"Set git useragent to: {_gitHttpUserAgentEnv}.");
        }

        // git init <LocalDir>
        public async Task<int> GitInit(IExecutionContext context, string repositoryPath)
        {
            ArgUtil.NotNull(context, nameof(context));
            ArgUtil.NotNull(repositoryPath, nameof(repositoryPath));

            context.Debug($"Init git repository at: {repositoryPath}.");
            string repoRootEscapeSpace = StringUtil.Format(@"""{0}""", repositoryPath.Replace(@"""", @"\"""));
            return await ExecuteGitCommandAsync(context, repositoryPath, "init", StringUtil.Format($"{repoRootEscapeSpace}"));
        }

        // git fetch --tags --prune --progress --no-recurse-submodules [--depth=15] origin [+refs/pull/*:refs/remote/pull/*]
        public async Task<int> GitFetch(IExecutionContext context, string repositoryPath, string remoteName, int fetchDepth, bool fetchTags, List<string> refSpec, string additionalCommandLine, CancellationToken cancellationToken)
        {
            ArgUtil.NotNull(context, nameof(context));

            context.Debug($"Fetch git repository at: {repositoryPath} remote: {remoteName}.");
            if (refSpec != null && refSpec.Count > 0)
            {
                refSpec = refSpec.Where(r => !string.IsNullOrEmpty(r)).ToList();
            }

            string tags = "--tags";
            if (!fetchTags)
            {
                tags = "--no-tags";
            }

            // insert prune-tags if DisableFetchPruneTags knob is false and Git version is above 2.17
            string pruneTags = string.Empty;
            if (EnsureGitVersion(new Version(2, 17), throwOnNotMatch: false) && !AgentKnobs.DisableFetchPruneTags.GetValue(context).AsBoolean())
            {
                pruneTags = "--prune-tags";
            }

            // If shallow fetch add --depth arg
            // If the local repository is shallowed but there is no fetch depth provide for this build,
            // add --unshallow to convert the shallow repository to a complete repository
            string depth = fetchDepth > 0 ? $"--depth={fetchDepth}" : (File.Exists(Path.Combine(repositoryPath, ".git", "shallow")) ? "--unshallow" : string.Empty);

            //define options for fetch
            string options = $"{tags} --prune {pruneTags} --progress --no-recurse-submodules {remoteName} {depth} {string.Join(" ", refSpec)}";

            return await ExecuteGitCommandAsync(context, repositoryPath, "fetch", options, additionalCommandLine, cancellationToken);
        }

        // git lfs fetch origin [ref]
        public async Task<int> GitLFSFetch(IExecutionContext context, string repositoryPath, string remoteName, string refSpec, string additionalCommandLine, CancellationToken cancellationToken)
        {
            ArgUtil.NotNull(context, nameof(context));

            string lfsconfig = ".lfsconfig";
            context.Debug($"Checkout {lfsconfig} for git repository at: {repositoryPath} remote: {remoteName}.");

            // default options for git checkout .lfsconfig
            string options = StringUtil.Format($"{refSpec} -- {lfsconfig}");
            int exitCodeLfsConfigCheckout = await ExecuteGitCommandAsync(context, repositoryPath, "checkout", options, additionalCommandLine, cancellationToken);
            if (exitCodeLfsConfigCheckout != 0)
            {
                context.Debug("There were some issues while checkout of .lfsconfig - probably because this file does not exist (see message above for more details). Continue fetching.");
            }

            context.Debug($"Fetch LFS objects for git repository at: {repositoryPath} remote: {remoteName}.");

            // default options for git lfs fetch.
            options = StringUtil.Format($"fetch origin {refSpec}");
            return await ExecuteGitCommandAsync(context, repositoryPath, "lfs", options, additionalCommandLine, cancellationToken);
        }

        // git checkout -f --progress <commitId/branch>
        public async Task<int> GitCheckout(IExecutionContext context, string repositoryPath, string committishOrBranchSpec, CancellationToken cancellationToken)
        {
            ArgUtil.NotNull(context, nameof(context));

            context.Debug($"Checkout {committishOrBranchSpec}.");

            // Git 2.7 support report checkout progress to stderr during stdout/err redirect.
            string options;
            if (_gitVersion >= new Version(2, 7))
            {
                options = StringUtil.Format("--progress --force {0}", committishOrBranchSpec);
            }
            else
            {
                options = StringUtil.Format("--force {0}", committishOrBranchSpec);
            }

            return await ExecuteGitCommandAsync(context, repositoryPath, "checkout", options, cancellationToken);
        }

        // git clean -ffdx
        public async Task<int> GitClean(IExecutionContext context, string repositoryPath)
        {
            ArgUtil.NotNull(context, nameof(context));

            context.Debug($"Delete untracked files/folders for repository at {repositoryPath}.");

            // Git 2.4 support git clean -ffdx.
            string options;
            if (_gitVersion >= new Version(2, 4))
            {
                options = "-ffdx";
            }
            else
            {
                options = "-fdx";
            }

            return await ExecuteGitCommandAsync(context, repositoryPath, "clean", options);
        }

        // git reset --hard HEAD
        public async Task<int> GitReset(IExecutionContext context, string repositoryPath)
        {
            ArgUtil.NotNull(context, nameof(context));

            context.Debug($"Undo any changes to tracked files in the working tree for repository at {repositoryPath}.");
            return await ExecuteGitCommandAsync(context, repositoryPath, "reset", "--hard HEAD");
        }

        // git status
        public async Task<int> GitStatus(IExecutionContext context, string repositoryPath)
        {
            ArgUtil.NotNull(context, nameof(context));

            context.Debug($"Show the working tree status for repository at {repositoryPath}.");
            return await ExecuteGitCommandAsync(context, repositoryPath, "status", string.Empty);
        }

        // get remote set-url <origin> <url>
        public async Task<int> GitRemoteAdd(IExecutionContext context, string repositoryPath, string remoteName, string remoteUrl)
        {
            ArgUtil.NotNull(context, nameof(context));

            context.Debug($"Add git remote: {remoteName} to url: {remoteUrl} for repository under: {repositoryPath}.");
            return await ExecuteGitCommandAsync(context, repositoryPath, "remote", StringUtil.Format($"add {remoteName} {remoteUrl}"));
        }

        // get remote set-url <origin> <url>
        public async Task<int> GitRemoteSetUrl(IExecutionContext context, string repositoryPath, string remoteName, string remoteUrl)
        {
            ArgUtil.NotNull(context, nameof(context));

            context.Debug($"Set git fetch url to: {remoteUrl} for remote: {remoteName}.");
            return await ExecuteGitCommandAsync(context, repositoryPath, "remote", StringUtil.Format($"set-url {remoteName} {remoteUrl}"));
        }

        // get remote set-url --push <origin> <url>
        public async Task<int> GitRemoteSetPushUrl(IExecutionContext context, string repositoryPath, string remoteName, string remoteUrl)
        {
            ArgUtil.NotNull(context, nameof(context));

            context.Debug($"Set git push url to: {remoteUrl} for remote: {remoteName}.");
            return await ExecuteGitCommandAsync(context, repositoryPath, "remote", StringUtil.Format($"set-url --push {remoteName} {remoteUrl}"));
        }

        // git submodule foreach --recursive "git clean -ffdx"
        public async Task<int> GitSubmoduleClean(IExecutionContext context, string repositoryPath)
        {
            ArgUtil.NotNull(context, nameof(context));

            context.Debug($"Delete untracked files/folders for submodules at {repositoryPath}.");

            // Git 2.4 support git clean -ffdx.
            string options;
            if (_gitVersion >= new Version(2, 4))
            {
                options = "-ffdx";
            }
            else
            {
                options = "-fdx";
            }

            return await ExecuteGitCommandAsync(context, repositoryPath, "submodule", $"foreach --recursive \"git clean {options}\"");
        }

        // git submodule foreach --recursive "git reset --hard HEAD"
        public async Task<int> GitSubmoduleReset(IExecutionContext context, string repositoryPath)
        {
            ArgUtil.NotNull(context, nameof(context));

            context.Debug($"Undo any changes to tracked files in the working tree for submodules at {repositoryPath}.");
            return await ExecuteGitCommandAsync(context, repositoryPath, "submodule", "foreach --recursive \"git reset --hard HEAD\"");
        }

        // git submodule update --init --force [--depth=15] [--recursive]
        public async Task<int> GitSubmoduleUpdate(IExecutionContext context, string repositoryPath, int fetchDepth, string additionalCommandLine, bool recursive, CancellationToken cancellationToken)
        {
            ArgUtil.NotNull(context, nameof(context));

            context.Debug("Update the registered git submodules.");
            string options = "update --init --force";
            if (fetchDepth > 0)
            {
                options = options + $" --depth={fetchDepth}";
            }
            if (recursive)
            {
                options = options + " --recursive";
            }

            return await ExecuteGitCommandAsync(context, repositoryPath, "submodule", options, additionalCommandLine, cancellationToken);
        }

        // git submodule sync [--recursive]
        public async Task<int> GitSubmoduleSync(IExecutionContext context, string repositoryPath, bool recursive, CancellationToken cancellationToken)
        {
            ArgUtil.NotNull(context, nameof(context));

            context.Debug("Synchronizes submodules' remote URL configuration setting.");
            string options = "sync";
            if (recursive)
            {
                options = options + " --recursive";
            }

            return await ExecuteGitCommandAsync(context, repositoryPath, "submodule", options, cancellationToken);
        }

        // git config --get remote.origin.url
        public async Task<Uri> GitGetFetchUrl(IExecutionContext context, string repositoryPath)
        {
            ArgUtil.NotNull(context, nameof(context));

            context.Debug($"Inspect remote.origin.url for repository under {repositoryPath}");
            Uri fetchUrl = null;

            List<string> outputStrings = new List<string>();
            int exitCode = await ExecuteGitCommandAsync(context, repositoryPath, "config", "--get remote.origin.url", outputStrings);

            if (exitCode != 0)
            {
                context.Warning($"'git config --get remote.origin.url' failed with exit code: {exitCode}, output: '{string.Join(Environment.NewLine, outputStrings)}'");
            }
            else
            {
                // remove empty strings
                outputStrings = outputStrings.Where(o => !string.IsNullOrEmpty(o)).ToList();
                if (outputStrings.Count == 1 && !string.IsNullOrEmpty(outputStrings.First()))
                {
                    string remoteFetchUrl = outputStrings.First();
                    if (Uri.IsWellFormedUriString(remoteFetchUrl, UriKind.Absolute))
                    {
                        context.Debug($"Get remote origin fetch url from git config: {remoteFetchUrl}");
                        fetchUrl = new Uri(remoteFetchUrl);
                    }
                    else
                    {
                        context.Debug($"The Origin fetch url from git config: {remoteFetchUrl} is not a absolute well formed url.");
                    }
                }
                else
                {
                    context.Debug($"Unable capture git remote fetch uri from 'git config --get remote.origin.url' command's output, the command's output is not expected: {string.Join(Environment.NewLine, outputStrings)}.");
                }
            }

            return fetchUrl;
        }

        // git config <key> <value>
        public async Task<int> GitConfig(IExecutionContext context, string repositoryPath, string configKey, string configValue)
        {
            ArgUtil.NotNull(context, nameof(context));

            context.Debug($"Set git config {configKey} {configValue}");
            return await ExecuteGitCommandAsync(context, repositoryPath, "config", StringUtil.Format($"{configKey} {configValue}"));
        }

        // git config --get-all <key>
        public async Task<bool> GitConfigExist(IExecutionContext context, string repositoryPath, string configKey)
        {
            ArgUtil.NotNull(context, nameof(context));

            // git config --get-all {configKey} will return 0 and print the value if the config exist.
            context.Debug($"Checking git config {configKey} exist or not");

            // ignore any outputs by redirect them into a string list, since the output might contains secrets.
            List<string> outputStrings = new List<string>();
            int exitcode = await ExecuteGitCommandAsync(context, repositoryPath, "config", StringUtil.Format($"--get-all {configKey}"), outputStrings);

            return exitcode == 0;
        }

        // git config --unset-all <key>
        public async Task<int> GitConfigUnset(IExecutionContext context, string repositoryPath, string configKey)
        {
            ArgUtil.NotNull(context, nameof(context));

            context.Debug($"Unset git config --unset-all {configKey}");
            return await ExecuteGitCommandAsync(context, repositoryPath, "config", StringUtil.Format($"--unset-all {configKey}"));
        }

        // git config gc.auto 0
        public async Task<int> GitDisableAutoGC(IExecutionContext context, string repositoryPath)
        {
            ArgUtil.NotNull(context, nameof(context));

            context.Debug("Disable git auto garbage collection.");
            return await ExecuteGitCommandAsync(context, repositoryPath, "config", "gc.auto 0");
        }

        // git repack -adfl
        public async Task<int> GitRepack(IExecutionContext context, string repositoryPath)
        {
            ArgUtil.NotNull(context, nameof(context));

            context.Debug("Compress .git directory.");
            return await ExecuteGitCommandAsync(context, repositoryPath, "repack", "-adfl");
        }

        // git prune
        public async Task<int> GitPrune(IExecutionContext context, string repositoryPath)
        {
            ArgUtil.NotNull(context, nameof(context));

            context.Debug("Delete unreachable objects under .git directory.");
            return await ExecuteGitCommandAsync(context, repositoryPath, "prune", "-v");
        }

        // git lfs prune
        public async Task<int> GitLFSPrune(IExecutionContext context, string repositoryPath)
        {
            ArgUtil.NotNull(context, nameof(context));

            context.Debug("Deletes local copies of LFS files which are old, thus freeing up disk space. Prune operates by enumerating all the locally stored objects, and then deleting any which are not referenced by at least ONE of the following:");
            return await ExecuteGitCommandAsync(context, repositoryPath, "lfs", "prune");
        }

        // git count-objects -v -H
        public async Task<int> GitCountObjects(IExecutionContext context, string repositoryPath)
        {
            ArgUtil.NotNull(context, nameof(context));

            context.Debug("Inspect .git directory.");
            return await ExecuteGitCommandAsync(context, repositoryPath, "count-objects", "-v -H");
        }

        // git lfs install --local
        public async Task<int> GitLFSInstall(IExecutionContext context, string repositoryPath)
        {
            ArgUtil.NotNull(context, nameof(context));

            context.Debug("Ensure git-lfs installed.");
            return await ExecuteGitCommandAsync(context, repositoryPath, "lfs", "install --local");
        }

        // git lfs logs last
        public async Task<int> GitLFSLogs(IExecutionContext context, string repositoryPath)
        {
            ArgUtil.NotNull(context, nameof(context));

            context.Debug("Get git-lfs logs.");
            return await ExecuteGitCommandAsync(context, repositoryPath, "lfs", "logs last");
        }

        // git version
        public async Task<Version> GitVersion(IExecutionContext context)
        {
            ArgUtil.NotNull(context, nameof(context));

            context.Debug("Get git version.");
            Version version = null;
            List<string> outputStrings = new List<string>();
            int exitCode = await ExecuteGitCommandAsync(context, HostContext.GetDirectory(WellKnownDirectory.Work), "version", null, outputStrings);
            context.Output($"{string.Join(Environment.NewLine, outputStrings)}");
            if (exitCode == 0)
            {
                // remove any empty line.
                outputStrings = outputStrings.Where(o => !string.IsNullOrEmpty(o)).ToList();
                if (outputStrings.Count == 1 && !string.IsNullOrEmpty(outputStrings.First()))
                {
                    string verString = outputStrings.First();
                    // we interested about major.minor.patch version
                    Regex verRegex = new Regex("\\d+\\.\\d+(\\.\\d+)?", RegexOptions.IgnoreCase);
                    var matchResult = verRegex.Match(verString);
                    if (matchResult.Success && !string.IsNullOrEmpty(matchResult.Value))
                    {
                        if (!Version.TryParse(matchResult.Value, out version))
                        {
                            version = null;
                        }
                    }
                }
            }

            return version;
        }

        // git lfs version
        public async Task<Version> GitLfsVersion(IExecutionContext context)
        {
            ArgUtil.NotNull(context, nameof(context));

            context.Debug("Get git-lfs version.");
            Version version = null;
            List<string> outputStrings = new List<string>();
            int exitCode = await ExecuteGitCommandAsync(context, HostContext.GetDirectory(WellKnownDirectory.Work), "lfs version", null, outputStrings);
            context.Output($"{string.Join(Environment.NewLine, outputStrings)}");
            if (exitCode == 0)
            {
                // remove any empty line.
                outputStrings = outputStrings.Where(o => !string.IsNullOrEmpty(o)).ToList();
                if (outputStrings.Count == 1 && !string.IsNullOrEmpty(outputStrings.First()))
                {
                    string verString = outputStrings.First();
                    // we interested about major.minor.patch version
                    Regex verRegex = new Regex("\\d+\\.\\d+(\\.\\d+)?", RegexOptions.IgnoreCase);
                    var matchResult = verRegex.Match(verString);
                    if (matchResult.Success && !string.IsNullOrEmpty(matchResult.Value))
                    {
                        if (!Version.TryParse(matchResult.Value, out version))
                        {
                            version = null;
                        }
                    }
                }
            }

            return version;
        }
        private async Task<int> ExecuteGitCommandAsync(IExecutionContext context, string repoRoot, string command, string options, CancellationToken cancellationToken = default(CancellationToken))
        {
            string arg = StringUtil.Format($"{command} {options}").Trim();
            context.Command($"git {arg}");

            var processInvoker = HostContext.CreateService<IProcessInvoker>();
            processInvoker.OutputDataReceived += delegate (object sender, ProcessDataReceivedEventArgs message)
            {
                context.Output(message.Data);
            };

            processInvoker.ErrorDataReceived += delegate (object sender, ProcessDataReceivedEventArgs message)
            {
                context.Output(message.Data);
            };

            return await processInvoker.ExecuteAsync(
                workingDirectory: repoRoot,
                fileName: _gitPath,
                arguments: arg,
                environment: GetGitEnvironmentVariables(context),
                requireExitCodeZero: false,
                outputEncoding: _encoding,
                cancellationToken: cancellationToken);
        }

        private async Task<int> ExecuteGitCommandAsync(IExecutionContext context, string repoRoot, string command, string options, IList<string> output)
        {
            string arg = StringUtil.Format($"{command} {options}").Trim();
            context.Command($"git {arg}");

            if (output == null)
            {
                output = new List<string>();
            }

            object outputLock = new object();
            var processInvoker = HostContext.CreateService<IProcessInvoker>();
            processInvoker.OutputDataReceived += delegate (object sender, ProcessDataReceivedEventArgs message)
            {
                lock (outputLock)
                {
                    output.Add(message.Data);
                }
            };

            processInvoker.ErrorDataReceived += delegate (object sender, ProcessDataReceivedEventArgs message)
            {
                lock (outputLock)
                {
                    output.Add(message.Data);
                }
            };

            return await processInvoker.ExecuteAsync(
                workingDirectory: repoRoot,
                fileName: _gitPath,
                arguments: arg,
                environment: GetGitEnvironmentVariables(context),
                requireExitCodeZero: false,
                outputEncoding: _encoding,
                cancellationToken: default(CancellationToken));
        }

        private async Task<int> ExecuteGitCommandAsync(IExecutionContext context, string repoRoot, string command, string options, string additionalCommandLine, CancellationToken cancellationToken)
        {
            string arg = StringUtil.Format($"{additionalCommandLine} {command} {options}").Trim();
            context.Command($"git {arg}");

            var processInvoker = HostContext.CreateService<IProcessInvoker>();
            processInvoker.OutputDataReceived += delegate (object sender, ProcessDataReceivedEventArgs message)
            {
                context.Output(message.Data);
            };

            processInvoker.ErrorDataReceived += delegate (object sender, ProcessDataReceivedEventArgs message)
            {
                context.Output(message.Data);
            };

            return await processInvoker.ExecuteAsync(
                workingDirectory: repoRoot,
                fileName: _gitPath,
                arguments: arg,
                environment: GetGitEnvironmentVariables(context),
                requireExitCodeZero: false,
                outputEncoding: _encoding,
                cancellationToken: cancellationToken);
        }

        private IDictionary<string, string> GetGitEnvironmentVariables(IExecutionContext context)
        {
            if (!string.IsNullOrEmpty(_gitHttpUserAgentEnv))
            {
                _gitEnv["GIT_HTTP_USER_AGENT"] = _gitHttpUserAgentEnv;
            }

            // Add the public variables.
            foreach (Variable variable in context.Variables.Public)
            {
                // Add the variable using the formatted name.
                string formattedName = VarUtil.ConvertToEnvVariableFormat(variable.Name, variable.PreserveCase);

                // Skip any GIT_TRACE variable since GIT_TRACE will affect ouput from every git command.
                // This will fail the parse logic for detect git version, remote url, etc.
                // Ex.
                //      SET GIT_TRACE=true
                //      git version
                //      11:39:58.295959 git.c:371               trace: built-in: git 'version'
                //      git version 2.11.1.windows.1
                if (formattedName == "GIT_TRACE" || formattedName.StartsWith("GIT_TRACE_"))
                {
                    continue;
                }

                _gitEnv[formattedName] = variable.Value ?? string.Empty;
            }

            return _gitEnv;
        }
    }
}
