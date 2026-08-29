// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Agent.Plugins.Repository;
using Agent.Sdk;

namespace Test.L0.Plugin.TestGitSourceProvider
{
    public class MockSubmoduleFlowGitCliManager : GitCliManager
    {
        public List<string> SubmodulePaths = new List<string>();
        public Dictionary<string, string> RemoteUrlByRepoRoot =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public override async Task LoadGitExecutionInfo(AgentTaskPluginExecutionContext context, bool useBuiltInGit)
        {
            gitPath = "path/to/git";
            gitVersion = await GitVersion(context);
            gitLfsPath = "path/to/gitlfs";
            gitLfsVersion = await GitLfsVersion(context);
        }

        public override Task<Version> GitVersion(AgentTaskPluginExecutionContext context)
            => Task.FromResult(new Version(2, 99999));

        public override Task<Version> GitLfsVersion(AgentTaskPluginExecutionContext context)
            => Task.FromResult(new Version(2, 99999));

        protected override Task<int> ExecuteGitCommandAsync(AgentTaskPluginExecutionContext context, string repoRoot, string command, string options, IList<string> output)
        {
            if (command == "submodule" && options.StartsWith("foreach", StringComparison.Ordinal))
            {
                foreach (string p in SubmodulePaths)
                {
                    output.Add(p);
                }
            }
            else if (command == "config" && options == "--get remote.origin.url")
            {
                if (RemoteUrlByRepoRoot.TryGetValue(repoRoot, out string url))
                {
                    output.Add(url);
                }
            }

            return Task.FromResult(0);
        }

        protected override Task<int> ExecuteGitCommandAsync(AgentTaskPluginExecutionContext context, string repoRoot, string command, string options, string additionalCommandLine, CancellationToken cancellationToken)
            => Task.FromResult(0);

        protected override Task<int> ExecuteGitCommandAsync(AgentTaskPluginExecutionContext context, string repoRoot, string command, string options, CancellationToken cancellationToken = default(CancellationToken))
            => Task.FromResult(0);
    }

    public class MockSubmoduleFlowGitSourceProvider : MockGitSoureProvider
    {
        public MockSubmoduleFlowGitCliManager CliManager = new MockSubmoduleFlowGitCliManager();

        protected override GitCliManager GetCliManager(Dictionary<string, string> gitEnv = null)
        {
            return CliManager;
        }

        public override bool GitSupportUseAuthHeader(AgentTaskPluginExecutionContext executionContext, GitCliManager gitCommandManager)
        {
            return true;
        }
    }
}