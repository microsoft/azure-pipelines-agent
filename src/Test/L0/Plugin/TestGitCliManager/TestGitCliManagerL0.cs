using Agent.Plugins.Repository;
using Agent.Sdk;
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
        [Theory]
        [InlineData("https://example.com/project/_git/repo", 0, true)]
        [InlineData("https://user@dev.azure.com/org/Fen%C3%AAtre%20-%20Project/_git/repo", 0, true)]
        [InlineData("https://dev.azure.com/org/Fen%C3%AAtre%20-%20Project/_git/repo", 0, true)]
        [InlineData("relative/path", 0, false)]
        [InlineData("https://[invalid", 0, false)]
        [InlineData("", 0, false)]
        [InlineData("https://example.com/one\nhttps://example.com/two", 0, false)]
        [InlineData("https://example.com/project/_git/repo", 1, false)]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task GitGetFetchUrlParsesAbsoluteOrigins(string output, int exitCode, bool expectedValid)
        {
            using var hostContext = new TestHostContext(this);
            var context = new MockAgentTaskPluginExecutionContext(hostContext.GetTrace());
            var manager = new FetchUrlGitCliManager(output, exitCode);

            var actual = await manager.GitGetFetchUrl(context, "repositoryPath");

            if (expectedValid)
            {
                Assert.Equal(new Uri(output, UriKind.Absolute), actual);
            }
            else
            {
                Assert.Null(actual);
            }
        }

        private sealed class FetchUrlGitCliManager : GitCliManager
        {
            private readonly string _output;
            private readonly int _exitCode;

            public FetchUrlGitCliManager(string output, int exitCode)
            {
                _output = output;
                _exitCode = exitCode;
            }

            protected override Task<int> ExecuteGitCommandAsync(AgentTaskPluginExecutionContext context, string repoRoot, string command, string options, IList<string> lines)
            {
                Assert.Equal("config", command);
                Assert.Equal("--get remote.origin.url", options);
                foreach (string line in _output.Split('\n'))
                {
                    lines.Add(line);
                }
                return Task.FromResult(_exitCode);
            }
        }

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

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        [Trait("SkipOn", "darwin")]
        [Trait("SkipOn", "linux")]
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
