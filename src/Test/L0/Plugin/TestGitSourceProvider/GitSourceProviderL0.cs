// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.Services.Agent;
using Microsoft.VisualStudio.Services.Agent.Tests;
using Xunit;
using System.IO;
using System;
using Moq;
using Agent.Plugins.Repository;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agent.Sdk;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Test.L0.Plugin.TestGitSourceProvider;

public sealed class TestPluginGitSourceProviderL0
{
    private readonly Func<TestHostContext, string> getWorkFolder = hc => hc.GetDirectory(WellKnownDirectory.Work);
    private readonly string gitPath = Path.Combine("agenthomedirectory", "externals", "git", "cmd", "git.exe");
    private readonly string ffGitPath = Path.Combine("agenthomedirectory", "externals", "ff_git", "cmd", "git.exe");
    public static IEnumerable<object[]> FeatureFlagsStatusData => new List<object[]>
    {
        new object[] { true },
        new object[] { false },
    };

    public static IEnumerable<object[]> FetchByCommitData
    {
        get
        {
            const string commit = "0123456789012345678901234567890123456789";
            var scenarios = new[]
            {
                // Depth, full-clone knob, server support, disabled knob, version, fetch count, initial commit ref.
                new object[] { 1, false, true, false, commit, 1, true },
                new object[] { 0, true, true, false, commit, 1, true },
                new object[] { 0, false, true, false, commit, 2, false },
                new object[] { 1, false, true, true, commit, 1, false },
                new object[] { 1, false, false, false, commit, 1, false },
                new object[] { 1, false, true, false, null, 1, false },
                new object[] { 1, false, true, false, string.Empty, 1, false },
            };

            foreach (string branch in new[] { "refs/heads/main", "refs/pull/123/merge" })
            {
                foreach (object[] scenario in scenarios)
                {
                    yield return new object[] { branch }.Concat(scenario).ToArray();
                }
            }
        }
    }

    [Theory]
    [Trait("Level", "L0")]
    [Trait("Category", "Plugin")]
    [MemberData(nameof(FetchByCommitData))]
    public async Task GetSourceAsync_FetchesCommitOnlyWhenNeeded(
        string branch, int fetchDepth, bool fetchByCommitForFullClone, bool supportsFetchByCommit,
        bool disableFetchByCommit, string sourceVersion, int expectedFetchCount, bool expectedCommitRef)
    {
        using TestHostContext hc = new(this);
        var tc = new MockAgentTaskPluginExecutionContext(hc.GetTrace());
        var target = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var repository = GetRepository(hc, "myrepo", "myrepo");
            repository.Properties.Set<string>(Pipelines.RepositoryPropertyNames.Path, target);
            repository.Properties.Set<string>(Pipelines.RepositoryPropertyNames.Ref, branch);
            repository.Version = sourceVersion;
            tc.Repositories.Add(repository);
            tc.Endpoints.Add(new ServiceEndpoint
            {
                Name = WellKnownServiceEndpointNames.SystemVssConnection,
                Url = new Uri("https://dev.azure.com/test/")
            });
            tc.Inputs[Pipelines.PipelineConstants.CheckoutTaskInputs.FetchDepth] = fetchDepth.ToString();
            tc.Variables.Add("VSTS.FetchByCommitForFullClone", fetchByCommitForFullClone.ToString());
            tc.Variables.Add("VSTS.DisableFetchByCommit", disableFetchByCommit.ToString());
            tc.Variables.Add("system.selfmanagegitcreds", "true");

            var git = new FetchGitCliManager();
            var provider = new FetchGitSourceProvider(git, supportsFetchByCommit);
            await provider.GetSourceAsync(tc, repository, CancellationToken.None);

            var fetches = git.GitCommandCallsOptions
                .Where(call => call.StartsWith($"{target},fetch,", StringComparison.Ordinal))
                .Select(call => call.Split(',')[2].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .ToList();
            Assert.Equal(expectedFetchCount, fetches.Count);

            bool isPullRequest = branch.StartsWith("refs/pull/", StringComparison.Ordinal);
            string remoteBranch = isPullRequest ? "refs/remotes/pull/123/merge" : "refs/remotes/origin/main";
            string commitRef = $"refs/remotes/origin/{sourceVersion}";
            string[] expectedRefSpecs = expectedCommitRef
                ? new[] { $"+{sourceVersion}:{commitRef}" }
                : isPullRequest
                    ? new[] { "+refs/heads/*:refs/remotes/origin/*", $"+{branch}:{remoteBranch}" }
                    : Array.Empty<string>();
            Assert.Equal(expectedRefSpecs, fetches[0].Where(arg => arg.StartsWith("+")));
            if (expectedFetchCount == 2)
            {
                Assert.Equal(new[] { $"+{sourceVersion}" }, fetches[1].Where(arg => arg.StartsWith("+")));
            }

            foreach (var fetch in fetches)
            {
                Assert.Contains("origin", fetch);
                Assert.Equal(fetchDepth > 0, fetch.Contains($"--depth={fetchDepth}"));
            }

            string expectedCheckout = expectedCommitRef ? commitRef
                : isPullRequest || string.IsNullOrEmpty(sourceVersion) ? remoteBranch : sourceVersion;
            string checkout = Assert.Single(git.GitCommandCallsOptions
                .Where(call => call.StartsWith($"{target},checkout,", StringComparison.Ordinal)));
            Assert.Equal($"--progress --force {expectedCheckout}", checkout.Split(',')[2]);
        }
        finally
        {
            if (Directory.Exists(target))
            {
                Directory.Delete(target, true);
            }
        }
    }

    private sealed class FetchGitCliManager : MockGitCliManager
    {
        public override async Task LoadGitExecutionInfo(AgentTaskPluginExecutionContext context, bool useBuiltInGit)
        {
            gitPath = "git";
            gitVersion = await GitVersion(context);
        }
    }

    private sealed class FetchGitSourceProvider : MockGitSoureProvider
    {
        private readonly GitCliManager git;
        private readonly bool supportsFetchByCommit;

        public FetchGitSourceProvider(GitCliManager git, bool supportsFetchByCommit)
        {
            this.git = git;
            this.supportsFetchByCommit = supportsFetchByCommit;
        }

        protected override GitCliManager GetCliManager(Dictionary<string, string> gitEnv = null) => git;

        public override bool GitSupportsFetchingCommitBySha1Hash(GitCliManager gitCommandManager) => supportsFetchByCommit;
    }

    [Theory]
    [Trait("Level", "L0")]
    [Trait("Category", "Plugin")]
    [Trait("SkipOn", "darwin")]
    [Trait("SkipOn", "linux")]
    [MemberData(nameof(FeatureFlagsStatusData))]
    public async Task TestSetGitConfiguration(bool featureFlagsStatus)
    {
        using TestHostContext hc = new(this, $"FeatureFlagsStatus_{featureFlagsStatus}");
        MockAgentTaskPluginExecutionContext tc = new(hc.GetTrace());
        var gitCliManagerMock = new Mock<IGitCliManager>();

        var repositoryPath = Path.Combine(getWorkFolder(hc), "1", "testrepo");
        var featureFlagStatusString = featureFlagsStatus.ToString();
        var invocation = featureFlagsStatus ? Times.Once() : Times.Never();

        tc.Variables.Add("USE_GIT_SINGLE_THREAD", featureFlagStatusString);
        tc.Variables.Add("USE_GIT_LONG_PATHS", featureFlagStatusString);
        tc.Variables.Add("FIX_POSSIBLE_GIT_OUT_OF_MEMORY_PROBLEM", featureFlagStatusString);

        Agent.Plugins.Repository.GitSourceProvider gitSourceProvider = new Agent.Plugins.Repository.ExternalGitSourceProvider();
        await gitSourceProvider.SetGitFeatureFlagsConfiguration(tc, gitCliManagerMock.Object, repositoryPath);

        // Assert.
        gitCliManagerMock.Verify(x => x.GitConfig(tc, repositoryPath, "pack.threads", "1"), invocation);
        gitCliManagerMock.Verify(x => x.GitConfig(tc, repositoryPath, "core.longpaths", "true"), invocation);
        gitCliManagerMock.Verify(x => x.GitConfig(tc, repositoryPath, "http.postBuffer", "524288000"), invocation);
    }

    [Fact]
    [Trait("Level", "L0")]
    [Trait("Category", "Plugin")]
    public void TestSetWSICConnection()
    {
        using TestHostContext hc = new(this);
        MockAgentTaskPluginExecutionContext tc = new(hc.GetTrace());

        Mock<ArgUtilInstanced> argUtilInstanced = new Mock<ArgUtilInstanced>()
        {
            CallBase = true
        };

        argUtilInstanced.Setup(x => x.File(gitPath, "gitPath")).Callback(() => { });
        argUtilInstanced.Setup(x => x.File(ffGitPath, "gitPath")).Callback(() => { });
        argUtilInstanced.Setup(x => x.Directory("agentworkfolder", "agent.workfolder"));
        ArgUtil.ArgUtilInstance = argUtilInstanced.Object;

        var endpoint = new ServiceEndpoint()
        {
            Name = EndpointAuthorizationSchemes.WorkloadIdentityFederation,
            Id = Guid.NewGuid(),
            Authorization = new EndpointAuthorization()
            {
                Scheme = EndpointAuthorizationSchemes.WorkloadIdentityFederation,
                Parameters = {
                        { EndpointAuthorizationParameters.TenantId, "TestTenant"},
                        { EndpointAuthorizationParameters.ServicePrincipalId, "TestClientId"}
                    }
            }
        };
        var systemConnectionEndpoint = new ServiceEndpoint()
        {
            Name = WellKnownServiceEndpointNames.SystemVssConnection,
            Id = Guid.NewGuid(),
            Url = new Uri("https://dev.azure.com"),
            Authorization = new EndpointAuthorization()
            {
                Scheme = EndpointAuthorizationSchemes.OAuth,
                Parameters = { { EndpointAuthorizationParameters.AccessToken, "Test" } }
            }
        };

        var repoEndpoint = new Pipelines.ServiceEndpointReference();
        repoEndpoint.Id = endpoint.Id;
        tc.Endpoints.Add(endpoint);
        tc.Endpoints.Add(systemConnectionEndpoint);
        tc.Repositories.Add(GetRepository(hc, "myrepo", "myrepo"));
        tc.Repositories[0].Endpoint = repoEndpoint;
        tc.Variables.Add("agent.workfolder", "agentworkfolder");
        tc.Variables.Add("agent.homedirectory", "agenthomedirectory");
        var gitSourceProvider = new MockGitSoureProvider();
        gitSourceProvider.GetSourceAsync(tc, tc.Repositories[0], System.Threading.CancellationToken.None).GetAwaiter().GetResult();
        Assert.Contains("WorkloadIdentityFederation:WSICToken", tc.TaskVariables.GetValueOrDefault("repoUrlWithCred").Value);
        Assert.Contains("dev.azure.com/test/_git/myrepo", tc.TaskVariables.GetValueOrDefault("repoUrlWithCred").Value);
    }

    private Pipelines.RepositoryResource GetRepository(TestHostContext hostContext, String alias, String relativePath)
    {
        var workFolder = hostContext.GetDirectory(WellKnownDirectory.Work);
        var repo = new Pipelines.RepositoryResource()
        {
            Alias = alias,
            Type = Pipelines.RepositoryTypes.Git,
            Url = new Uri($"https://dev.azure.com/test/_git/{alias}")
        };
        repo.Properties.Set<string>(Pipelines.RepositoryPropertyNames.Path, Path.Combine(workFolder, "1", relativePath));

        return repo;
    }
}
