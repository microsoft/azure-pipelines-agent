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
    public class MockSubmodulePathsGitCliManager : GitCliManager
    {
        public List<string> CommandOutput = new List<string>();
        public int CommandExitCode = 0;
        public string LastRepoRoot;
        public string LastCommand;
        public string LastOptions;

        protected override Task<int> ExecuteGitCommandAsync(AgentTaskPluginExecutionContext context, string repoRoot, string command, string options, IList<string> output)
        {
            LastRepoRoot = repoRoot;
            LastCommand = command;
            LastOptions = options;

            foreach (string line in CommandOutput)
            {
                output.Add(line);
            }

            return Task.FromResult(CommandExitCode);
        }

        protected override Task<int> ExecuteGitCommandAsync(AgentTaskPluginExecutionContext context, string repoRoot, string command, string options, string additionalCommandLine, CancellationToken cancellationToken)
        {
            return Task.FromResult(0);
        }

        protected override Task<int> ExecuteGitCommandAsync(AgentTaskPluginExecutionContext context, string repoRoot, string command, string options, CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.FromResult(0);
        }

        public override Task<Version> GitVersion(AgentTaskPluginExecutionContext context)
        {
            return Task.FromResult(new Version("2.30.2"));
        }

        public override Task<Version> GitLfsVersion(AgentTaskPluginExecutionContext context)
        {
            return Task.FromResult(new Version("2.30.2"));
        }
    }
}