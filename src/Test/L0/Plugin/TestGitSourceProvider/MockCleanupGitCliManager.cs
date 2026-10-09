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
    public class MockCleanupGitCliManager : GitCliManager
    {
        public List<string> ExecutedCommands = new List<string>();
        public List<string> ExecutedRepoRoots = new List<string>();
        public int GitFetchExitCode;
        public string ThrowOnCommand;

        public override Task LoadGitExecutionInfo(AgentTaskPluginExecutionContext context, bool useBuiltInGit)
        {
            gitPath = "git";
            gitVersion = new Version("2.30.2");
            gitLfsPath = "git-lfs";
            gitLfsVersion = new Version("2.30.2");
            return Task.CompletedTask;
        }

        public override Task<Version> GitVersion(AgentTaskPluginExecutionContext context)
            => Task.FromResult(new Version("2.30.2"));

        public override Task<Version> GitLfsVersion(AgentTaskPluginExecutionContext context)
            => Task.FromResult(new Version("2.30.2"));

        private Task<int> Record(string repoRoot, string command, string options)
        {
            ExecutedRepoRoots.Add(repoRoot);
            string commandLine = $"{command} {options}";
            ExecutedCommands.Add(commandLine);
            if (string.Equals(commandLine, ThrowOnCommand, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Configured Git command failure.");
            }

            return Task.FromResult(command == "fetch" ? GitFetchExitCode : 0);
        }

        protected override Task<int> ExecuteGitCommandAsync(AgentTaskPluginExecutionContext context, string repoRoot, string command, string options, CancellationToken cancellationToken = default(CancellationToken))
            => Record(repoRoot, command, options);

        protected override Task<int> ExecuteGitCommandAsync(AgentTaskPluginExecutionContext context, string repoRoot, string command, string options, IList<string> output)
            => Record(repoRoot, command, options);

        protected override Task<int> ExecuteGitCommandAsync(AgentTaskPluginExecutionContext context, string repoRoot, string command, string options, string additionalCommandLine, CancellationToken cancellationToken)
            => Record(repoRoot, command, options);
    }

    public class MockCleanupGitSourceProvider : MockGitSoureProvider
    {
        public MockCleanupGitCliManager CliManager = new MockCleanupGitCliManager();

        protected override GitCliManager GetCliManager(Dictionary<string, string> gitEnv = null)
        {
            return CliManager;
        }
    }

    public class MockCleanupExternalGitSourceProvider : ExternalGitSourceProvider
    {
        public MockCleanupGitCliManager CliManager = new MockCleanupGitCliManager();

        protected override GitCliManager GetCliManager(Dictionary<string, string> gitEnv = null)
        {
            return CliManager;
        }
    }
}