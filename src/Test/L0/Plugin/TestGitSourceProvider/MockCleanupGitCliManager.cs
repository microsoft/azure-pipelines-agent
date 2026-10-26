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

        public override Task LoadGitExecutionInfo(AgentTaskPluginExecutionContext context, bool useBuiltInGit)
        {
            return Task.CompletedTask;
        }

        public override Task<Version> GitVersion(AgentTaskPluginExecutionContext context)
            => Task.FromResult(new Version("2.30.2"));

        public override Task<Version> GitLfsVersion(AgentTaskPluginExecutionContext context)
            => Task.FromResult(new Version("2.30.2"));

        private Task<int> Record(string repoRoot, string command, string options)
        {
            ExecutedRepoRoots.Add(repoRoot);
            ExecutedCommands.Add($"{command} {options}");
            return Task.FromResult(0);
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
}