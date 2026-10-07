// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Services.Agent;
using Microsoft.VisualStudio.Services.Agent.Tests;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;
using Xunit;

namespace Test.L0.Plugin.TestGitSourceProvider;

public sealed class ExternalGitCredentialCleanupL0
{
    [Fact]
    [Trait("Level", "L0")]
    [Trait("Category", "Plugin")]
    public async Task PostJobCleanup_RemovesCredentialFromRemote()
    {
        using TestHostContext hostContext = new(this);
        var executionContext = new MockAgentTaskPluginExecutionContext(hostContext.GetTrace());
        string repositoryPath = Path.Combine(hostContext.GetDirectory(WellKnownDirectory.Work), "1", "testrepo");
        const string repositoryUrl = "https://example.invalid/repo.git";

        executionContext.TaskVariables["cleanupcreds"] = "true";
        executionContext.TaskVariables["repoUrlWithCred"] = "https://user:secret@example.invalid/repo.git";

        var repository = new Pipelines.RepositoryResource
        {
            Alias = "testrepo",
            Type = Pipelines.RepositoryTypes.ExternalGit,
            Url = new Uri(repositoryUrl)
        };
        repository.Properties.Set<string>(Pipelines.RepositoryPropertyNames.Path, repositoryPath);

        var provider = new MockCleanupGitSourceProvider();
        await provider.PostJobCleanupAsync(executionContext, repository);

        Assert.Contains($"remote set-url origin {repositoryUrl}", provider.CliManager.ExecutedCommands);
        Assert.Contains($"remote set-url --push origin {repositoryUrl}", provider.CliManager.ExecutedCommands);
    }
}
