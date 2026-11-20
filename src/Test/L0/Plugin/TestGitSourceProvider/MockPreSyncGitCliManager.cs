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
    public class MockPreSyncGitCliManager : GitCliManager
    {
        public sealed class ExecutedCommand
        {
            public string Command { get; set; }
            public string Options { get; set; }
            public string AdditionalCommandLine { get; set; }
        }

        public List<ExecutedCommand> ExecutedCommands = new List<ExecutedCommand>();

        // Values returned by `git config --get-all <key>`. A key that is absent makes the command exit 1, the way
        // git does for an unset key.
        public Dictionary<string, List<string>> Config = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        public string RemoteUrl;

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

        private int Record(string command, string options, string additionalCommandLine)
        {
            ExecutedCommands.Add(new ExecutedCommand { Command = command, Options = options, AdditionalCommandLine = additionalCommandLine });
            return 0;
        }

        protected override Task<int> ExecuteGitCommandAsync(AgentTaskPluginExecutionContext context, string repoRoot, string command, string options, IList<string> output)
        {
            Record(command, options, null);

            if (command == "config" && options == "--get remote.origin.url")
            {
                output.Add(RemoteUrl);
                return Task.FromResult(0);
            }

            const string getAll = "--get-all ";
            if (command == "config" && options.StartsWith(getAll, StringComparison.Ordinal))
            {
                if (!Config.TryGetValue(options.Substring(getAll.Length), out List<string> values))
                {
                    return Task.FromResult(1);
                }

                foreach (string value in values)
                {
                    output?.Add(value);
                }
            }

            return Task.FromResult(0);
        }

        protected override Task<int> ExecuteGitCommandAsync(AgentTaskPluginExecutionContext context, string repoRoot, string command, string options, string additionalCommandLine, CancellationToken cancellationToken)
        {
            if (command == "config" && options.StartsWith("--unset-all ", StringComparison.Ordinal))
            {
                Config.Remove(options.Substring("--unset-all ".Length).Split(' ')[0]);
            }

            return Task.FromResult(Record(command, options, additionalCommandLine));
        }

        protected override Task<int> ExecuteGitCommandAsync(AgentTaskPluginExecutionContext context, string repoRoot, string command, string options, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (command == "config" && options.StartsWith("--unset-all ", StringComparison.Ordinal))
            {
                Config.Remove(options.Substring("--unset-all ".Length).Split(' ')[0]);
            }

            return Task.FromResult(Record(command, options, null));
        }
    }

    public class MockPreSyncGitSourceProvider : MockGitSoureProvider
    {
        public MockPreSyncGitCliManager CliManager = new MockPreSyncGitCliManager();

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
