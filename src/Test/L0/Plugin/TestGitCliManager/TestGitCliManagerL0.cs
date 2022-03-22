using Agent.Plugins.Repository;
using Agent.Sdk;
using Microsoft.VisualStudio.Services.Agent.Tests;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Test.L0.Plugin.TestGitCliManager
{
    public class TestGitCliManagerL0
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task TestPublishArtifactAsync()
        {
            using (var hostContext = new TestHostContext(this))
            {
                var context = new AgentTaskPluginExecutionContext(hostContext.GetTrace());
                context.Variables.Add("agent.homedirectory", "agenthomedirectory");
                var gitCliManager = new GitCliManager();
                await gitCliManager.LoadGitExecutionInfo(context, true);

                await gitCliManager.GitLFSFetch(context, "repositoryPath", "remoteName", "refSpec", "additionalCmdLine", CancellationToken.None);
            }
        }
    }
}
