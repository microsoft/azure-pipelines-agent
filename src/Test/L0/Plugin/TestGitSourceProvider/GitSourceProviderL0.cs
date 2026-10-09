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
using System.Threading.Tasks;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Level", "L0")]
    [Trait("Category", "Plugin")]
    public async Task FailedExternalGitFetch_RunsCredentialCleanup(bool cleanupThrows)
    {
        using TestHostContext hc = new(this);
        MockAgentTaskPluginExecutionContext tc = new(hc.GetTrace());
        string repositoryPath = Path.Combine(getWorkFolder(hc), "1", "testrepo");
        const string repositoryUrl = "https://example.invalid/repo.git";
        const string credential = "fake-token";

        var endpoint = new ServiceEndpoint
        {
            Name = "ExternalGit",
            Id = Guid.NewGuid(),
            Authorization = new EndpointAuthorization
            {
                Scheme = EndpointAuthorizationSchemes.UsernamePassword,
                Parameters =
                {
                    { EndpointAuthorizationParameters.Username, "test-user" },
                    { EndpointAuthorizationParameters.Password, credential }
                }
            }
        };
        var systemConnectionEndpoint = new ServiceEndpoint
        {
            Name = WellKnownServiceEndpointNames.SystemVssConnection,
            Id = Guid.NewGuid(),
            Url = new Uri("https://dev.azure.com"),
            Authorization = new EndpointAuthorization
            {
                Scheme = EndpointAuthorizationSchemes.OAuth,
                Parameters = { { EndpointAuthorizationParameters.AccessToken, "Test" } }
            }
        };
        var repository = new Pipelines.RepositoryResource
        {
            Alias = "testrepo",
            Type = Pipelines.RepositoryTypes.ExternalGit,
            Url = new Uri(repositoryUrl),
            Endpoint = new Pipelines.ServiceEndpointReference { Id = endpoint.Id }
        };
        repository.Properties.Set<string>(Pipelines.RepositoryPropertyNames.Path, repositoryPath);

        tc.Endpoints.Add(endpoint);
        tc.Endpoints.Add(systemConnectionEndpoint);
        tc.Repositories.Add(repository);
        tc.Variables.Add("agent.workfolder", getWorkFolder(hc));
        tc.Variables.Add("agent.homedirectory", hc.GetDirectory(WellKnownDirectory.Root));

        var provider = new MockCleanupExternalGitSourceProvider();
        provider.CliManager.GitFetchExitCode = 1;
        if (cleanupThrows)
        {
            provider.CliManager.ThrowOnCommand = $"remote set-url origin {repositoryUrl}";
        }

        InvalidOperationException sourceFailure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetSourceAsync(tc, repository, System.Threading.CancellationToken.None));

        Assert.Contains("Git fetch failed", sourceFailure.Message, StringComparison.Ordinal);

        int credentialInjectionIndex = provider.CliManager.ExecutedCommands.FindIndex(
            command => command.Contains(credential, StringComparison.Ordinal));
        int failedFetchIndex = provider.CliManager.ExecutedCommands.FindIndex(
            command => command.StartsWith("fetch ", StringComparison.Ordinal));
        int fetchUrlCleanupIndex = provider.CliManager.ExecutedCommands.IndexOf($"remote set-url origin {repositoryUrl}");
        int pushUrlCleanupIndex = provider.CliManager.ExecutedCommands.IndexOf($"remote set-url --push origin {repositoryUrl}");

        Assert.True(credentialInjectionIndex >= 0);
        Assert.True(failedFetchIndex > credentialInjectionIndex);
        Assert.True(fetchUrlCleanupIndex > failedFetchIndex);
        if (cleanupThrows)
        {
            Assert.Equal(-1, pushUrlCleanupIndex);
        }
        else
        {
            Assert.True(pushUrlCleanupIndex > failedFetchIndex);
        }
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
