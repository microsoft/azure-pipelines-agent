using Agent.Plugins.Repository;
using Agent.Sdk;
using Microsoft.VisualStudio.Services.Agent.Tests;
using Microsoft.VisualStudio.Services.Agent.Util;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
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
                var originalArgUtilInstance = ArgUtil.ArgUtilInstance;
                Mock<ArgUtilInstanced> argUtilInstanced = new Mock<ArgUtilInstanced>();
                argUtilInstanced.CallBase = true;
                argUtilInstanced.Setup(x => x.File(Path.Combine("agenthomedirectory", "externals", "git", "cmd", $"git.exe"), "gitPath")).Callback(() => { });
                argUtilInstanced.Setup(x => x.Directory("agentworkfolder", "agent.workfolder")).Callback(() => { });

                ArgUtil.ArgUtilInstance = argUtilInstanced.Object;
                try
                {
                    var context = new MockAgentTaskPluginExecutionContext(hostContext.GetTrace());
                    context.Variables.Add("agent.homedirectory", "agenthomedirectory");
                    context.Variables.Add("agent.workfolder", "agentworkfolder");

                    var gitCliManagerMock = new Mock<GitCliManager>();
                    gitCliManagerMock.CallBase = true;
                    gitCliManagerMock.Setup(x => x.ExecuteGitCommandAsync(It.IsAny<AgentTaskPluginExecutionContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .Returns(Task.FromResult(0));
                    var gitCliManager = gitCliManagerMock.Object;

                    await gitCliManager.LoadGitExecutionInfo(context, true);

                    ArgUtil.NotNull(gitCliManager, "");
                    await gitCliManager.GitLFSFetch(context, "repositoryPath", "remoteName", "refSpec", "additionalCmdLine", CancellationToken.None);
                }
                finally
                {
                    ArgUtil.ArgUtilInstance = originalArgUtilInstance;
                }
            }
        }
    }
}
