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
        private Tuple<Mock<ArgUtilInstanced>, MockAgentTaskPluginExecutionContext> setupMocksForGitLfsFetchTests(TestHostContext hostContext)
        {
            Mock<ArgUtilInstanced> argUtilInstanced = new Mock<ArgUtilInstanced>();
            argUtilInstanced.CallBase = true;
            argUtilInstanced.Setup(x => x.File(Path.Combine("agenthomedirectory", "externals", "git", "cmd", $"git.exe"), "gitPath")).Callback(() => { });
            argUtilInstanced.Setup(x => x.File(Path.Combine("agenthomedirectory", "externals", "ff_git", "cmd", $"git.exe"), "gitPath")).Callback(() => { });
            argUtilInstanced.Setup(x => x.Directory("agentworkfolder", "agent.workfolder")).Callback(() => { });
            var context = new MockAgentTaskPluginExecutionContext(hostContext.GetTrace());
            context.Variables.Add("agent.homedirectory", "agenthomedirectory");
            context.Variables.Add("agent.workfolder", "agentworkfolder");

            return Tuple.Create(argUtilInstanced, context);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task TestGitLfsFetchLfsConfigExistsAsync()
        {
            using (var hostContext = new TestHostContext(this))
            {
                // Setup
                var originalArgUtilInstance = ArgUtil.ArgUtilInstance;
                var mocks = this.setupMocksForGitLfsFetchTests(hostContext);
                var argUtilInstanced = mocks.Item1;
                var mockAgentTaskPluginExecutionContext = mocks.Item2;

                try
                {
                    ArgUtil.ArgUtilInstance = argUtilInstanced.Object;

                    var gitCliManagerMock = new MockGitCliManager();

                    gitCliManagerMock.IsLfsConfigExistsing = true;
                    await gitCliManagerMock.LoadGitExecutionInfo(mockAgentTaskPluginExecutionContext, true);

                    ArgUtil.NotNull(gitCliManagerMock, "");

                    // Action
                    await gitCliManagerMock.GitLFSFetch(mockAgentTaskPluginExecutionContext, "repositoryPath", "remoteName", "refSpec", "additionalCmdLine", CancellationToken.None);

                    // Assert
                    Assert.Equal(2, gitCliManagerMock.GitCommandCallsOptions.Count);

                    Assert.True(gitCliManagerMock.GitCommandCallsOptions.Contains("repositoryPath,checkout,refSpec -- .lfsconfig,additionalCmdLine"), "ExecuteGitCommandAsync should pass arguments properly to 'git checkout .lfsconfig' command");
                    Assert.True(gitCliManagerMock.GitCommandCallsOptions.Contains("repositoryPath,lfs,fetch origin refSpec,additionalCmdLine"), "ExecuteGitCommandAsync should pass arguments properly to 'git lfs fetch' command");

                }
                finally
                {
                    ArgUtil.ArgUtilInstance = originalArgUtilInstance;
                }
            }
        }


        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task TestGitLfsFetchLfsConfigDoesNotExist()
        {
            using (var hostContext = new TestHostContext(this))
            {
                // Setup
                var originalArgUtilInstance = ArgUtil.ArgUtilInstance;
                var mocks = this.setupMocksForGitLfsFetchTests(hostContext);
                var argUtilInstanced = mocks.Item1;
                var mockAgentTaskPluginExecutionContext = mocks.Item2;

                try
                {
                    ArgUtil.ArgUtilInstance = argUtilInstanced.Object;

                    var gitCliManagerMock = new MockGitCliManager();

                    gitCliManagerMock.IsLfsConfigExistsing = false;
                    await gitCliManagerMock.LoadGitExecutionInfo(mockAgentTaskPluginExecutionContext, true);

                    ArgUtil.NotNull(gitCliManagerMock, "");

                    // Action
                    await gitCliManagerMock.GitLFSFetch(mockAgentTaskPluginExecutionContext, "repositoryPath", "remoteName", "refSpec", "additionalCmdLine", CancellationToken.None);

                    // Assert
                    Assert.Equal(2, gitCliManagerMock.GitCommandCallsOptions.Count);

                    Assert.True(gitCliManagerMock.GitCommandCallsOptions.Contains("repositoryPath,checkout,refSpec -- .lfsconfig,additionalCmdLine"), "ExecuteGitCommandAsync should pass arguments properly to 'git checkout .lfsconfig' command");
                    Assert.True(gitCliManagerMock.GitCommandCallsOptions.Contains("repositoryPath,lfs,fetch origin refSpec,additionalCmdLine"), "ExecuteGitCommandAsync should pass arguments properly to 'git lfs fetch' command");

                }
                finally
                {
                    ArgUtil.ArgUtilInstance = originalArgUtilInstance;
                }
            }
        }
    }
}
