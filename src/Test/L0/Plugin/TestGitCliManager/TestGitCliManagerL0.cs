using Microsoft.VisualStudio.Services.Agent.Tests;
using Microsoft.VisualStudio.Services.Agent.Util;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Test.L0.Plugin.TestGitCliManager
{
    public class TestGitCliManagerL0
    {
        private readonly string gitPath = Path.Combine("agenthomedirectory", "externals", "git", "cmd", "git.exe");
        private readonly string ffGitPath = Path.Combine("agenthomedirectory", "externals", "ff_git", "cmd", "git.exe");

        private Tuple<Mock<ArgUtilInstanced>, MockAgentTaskPluginExecutionContext> SetupMocksForGitLfsFetchTests(TestHostContext hostContext)
        {
            Mock<ArgUtilInstanced> argUtilInstanced = new Mock<ArgUtilInstanced>()
            {
                CallBase = true
            };

            argUtilInstanced.Setup(x => x.File(gitPath, "gitPath")).Callback(() => { });
            argUtilInstanced.Setup(x => x.File(ffGitPath, "gitPath")).Callback(() => { });
            argUtilInstanced.Setup(x => x.Directory("agentworkfolder", "agent.workfolder"));

            var context = new MockAgentTaskPluginExecutionContext(hostContext.GetTrace());
            context.Variables.Add("agent.homedirectory", "agenthomedirectory");
            context.Variables.Add("agent.workfolder", "agentworkfolder");

            return Tuple.Create(argUtilInstanced, context);
        }

        public static IEnumerable<object[]> UseNewGitVersionFeatureFlagsData => new List<object[]>
        {
            new object[] { true },
            new object[] { false },
        };

        [Theory]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        [Trait("SkipOn", "darwin")]
        [Trait("SkipOn", "linux")]
        [MemberData(nameof(UseNewGitVersionFeatureFlagsData))]
        public void TestGetInternalGitPaths()
        {
            using var hostContext = new TestHostContext(this);

            // Setup
            var originalArgUtilInstance = ArgUtil.ArgUtilInstance;
            var mocks = SetupMocksForGitLfsFetchTests(hostContext);
            var argUtilInstanced = mocks.Item1;
            var mockAgentTaskPluginExecutionContext = mocks.Item2;

            ArgUtil.ArgUtilInstance = argUtilInstanced.Object;
            MockGitCliManager gitCliManagerMock = new();
            var (resolvedGitPath, resolvedGitLfsPath) = gitCliManagerMock.GetInternalGitPaths(mockAgentTaskPluginExecutionContext);
            Assert.Equal(resolvedGitPath, gitPath);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task TestGitLfsFetchLfsConfigDoesNotExist()
        {
            using var hostContext = new TestHostContext(this);
            // Setup
            var originalArgUtilInstance = ArgUtil.ArgUtilInstance;
            var mocks = SetupMocksForGitLfsFetchTests(hostContext);
            var argUtilInstanced = mocks.Item1;
            var mockAgentTaskPluginExecutionContext = mocks.Item2;

            try
            {
                ArgUtil.ArgUtilInstance = argUtilInstanced.Object;

                var gitCliManagerMock = new MockGitCliManager()
                {
                    IsLfsConfigExistsing = false
                };

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
