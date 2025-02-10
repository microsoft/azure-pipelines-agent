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
    public void TestSetGitConfiguration(bool featureFlagsStatus)
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
        gitSourceProvider.SetGitFeatureFlagsConfiguration(tc, gitCliManagerMock.Object, repositoryPath);

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
