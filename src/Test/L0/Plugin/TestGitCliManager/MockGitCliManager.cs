using Agent.Plugins.Repository;
using Agent.Sdk;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Test.L0.Plugin.TestGitCliManager
{
    public class MockGitCliManager : GitCliManager
    {
        public List<string> GitCommandCallsOptions = new List<string>();
        public bool IsLfsConfigExistsing = false;
        protected override Task<int> ExecuteGitCommandAsync(AgentTaskPluginExecutionContext context, string repoRoot, string command, string options, IList<string> output)
        {
            return Task.FromResult(0);
        }

        protected override Task<int> ExecuteGitCommandAsync(AgentTaskPluginExecutionContext context, string repoRoot, string command, string options, string additionalCommandLine, CancellationToken cancellationToken)
        {
            GitCommandCallsOptions.Add($"{repoRoot},{command},{options},{additionalCommandLine}");
            if (command == "checkout" && options == "" && this.IsLfsConfigExistsing)
            {
                int returnCode = this.IsLfsConfigExistsing ? 0 : 1;
                return Task.FromResult(returnCode);
            }

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
