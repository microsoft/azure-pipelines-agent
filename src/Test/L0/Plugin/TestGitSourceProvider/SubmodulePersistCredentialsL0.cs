// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Agent.Plugins.Repository;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Tests;
using Microsoft.VisualStudio.Services.WebApi;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;
using Xunit;

namespace Test.L0.Plugin.TestGitSourceProvider
{
    public sealed class SubmodulePersistCredentialsL0
    {
        private static MockAgentTaskPluginExecutionContext CreateContext(TestHostContext hc, string collectionUri)
        {
            var tc = new MockAgentTaskPluginExecutionContext(hc.GetTrace());
            if (collectionUri != null)
            {
                tc.Variables.Add("system.collectionuri", collectionUri);
            }
            return tc;
        }

        [Theory]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        [InlineData("https://dev.azure.com/fabrikam/",
                    "https://fabrikam@dev.azure.com/fabrikam/myproj/_git/super",
                    "https://fabrikam@dev.azure.com/fabrikam/myproj/_git/sub")]
        [InlineData("https://dev.azure.com/contoso/",
                    "https://dev.azure.com/contoso/projA/_git/super",
                    "https://dev.azure.com/contoso/projB/_git/sub")]
        [InlineData("https://dev.azure.com/contoso/",
                    "https://contoso@dev.azure.com/contoso/proj/_git/super",
                    "https://dev.azure.com/contoso/proj/_git/sub")]
        [InlineData("https://dev.azure.com/Contoso/",
                    "https://dev.azure.com/Contoso/proj/_git/super",
                    "https://dev.azure.com/contoso/proj/_git/sub")]
        [InlineData("https://dev.azure.com/contoso/",
                    "https://dev.azure.com/contoso/proj/_git/super",
                    "https://DEV.AZURE.COM/contoso/proj/_git/sub")]
        [InlineData("https://contoso.visualstudio.com/",
                    "https://contoso.visualstudio.com/proj/_git/super",
                    "https://contoso.visualstudio.com/other/_git/sub")]
        [InlineData("https://tfsserver/tfs/DefaultCollection/",
                    "https://tfsserver/tfs/DefaultCollection/proj/_git/super",
                    "https://tfsserver/tfs/DefaultCollection/proj/_git/sub")]
        public void IsSameOrganization_ReturnsTrue(string collectionUri, string repoUrl, string submoduleUrl)
        {
            using TestHostContext hc = new(this);
            var tc = CreateContext(hc, collectionUri);

            Assert.True(GitSourceProvider.IsSameOrganization(tc, new Uri(repoUrl), new Uri(submoduleUrl)));
        }

        [Theory]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        [InlineData("https://dev.azure.com/contoso/",
                    "https://dev.azure.com/contoso/proj/_git/super",
                    "https://dev.azure.com/othercorp/proj/_git/sub")]
        [InlineData("https://dev.azure.com/contoso/",
                    "https://dev.azure.com/contoso/proj/_git/super",
                    "https://dev.azure.com/contoso-other/proj/_git/sub")]
        [InlineData("https://dev.azure.com/contoso/",
                    "https://dev.azure.com/contoso/proj/_git/super",
                    "https://github.com/contoso/sub")]
        [InlineData("https://dev.azure.com/contoso/",
                    "https://dev.azure.com/contoso/proj/_git/super",
                    "http://dev.azure.com/contoso/proj/_git/sub")]
        [InlineData("https://tfsserver/tfs/DefaultCollection/",
                    "https://tfsserver/tfs/DefaultCollection/proj/_git/super",
                    "https://tfsserver:8443/tfs/DefaultCollection/proj/_git/sub")]
        [InlineData("https://tfsserver/tfs/CollectionA/",
                    "https://tfsserver/tfs/CollectionA/proj/_git/super",
                    "https://tfsserver/tfs/CollectionB/proj/_git/sub")]
        [InlineData("https://contoso.visualstudio.com/",
                    "https://contoso.visualstudio.com/proj/_git/super",
                    "https://fabrikam.visualstudio.com/proj/_git/sub")]
        public void IsSameOrganization_ReturnsFalse(string collectionUri, string repoUrl, string submoduleUrl)
        {
            using TestHostContext hc = new(this);
            var tc = CreateContext(hc, collectionUri);

            Assert.False(GitSourceProvider.IsSameOrganization(tc, new Uri(repoUrl), new Uri(submoduleUrl)));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void IsSameOrganization_NoCollectionUri_FallsBackToFirstSegment()
        {
            using TestHostContext hc = new(this);
            var tc = CreateContext(hc, collectionUri: null);

            Assert.True(GitSourceProvider.IsSameOrganization(tc,
                new Uri("https://dev.azure.com/contoso/projA/_git/super"),
                new Uri("https://dev.azure.com/contoso/projB/_git/sub")));

            Assert.False(GitSourceProvider.IsSameOrganization(tc,
                new Uri("https://dev.azure.com/contoso/projA/_git/super"),
                new Uri("https://dev.azure.com/othercorp/projB/_git/sub")));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void IsSameOrganization_CollectionUriDifferentHost_FallsBackToFirstSegment()
        {
            using TestHostContext hc = new(this);
            var tc = CreateContext(hc, "https://other.example.com/someorg/");

            Assert.True(GitSourceProvider.IsSameOrganization(tc,
                new Uri("https://dev.azure.com/contoso/projA/_git/super"),
                new Uri("https://dev.azure.com/contoso/projB/_git/sub")));

            Assert.False(GitSourceProvider.IsSameOrganization(tc,
                new Uri("https://dev.azure.com/contoso/projA/_git/super"),
                new Uri("https://dev.azure.com/othercorp/projB/_git/sub")));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void IsSameOrganization_MalformedCollectionUri_FallsBackToFirstSegment()
        {
            using TestHostContext hc = new(this);
            var tc = CreateContext(hc, "not a uri");

            Assert.True(GitSourceProvider.IsSameOrganization(tc,
                new Uri("https://dev.azure.com/contoso/projA/_git/super"),
                new Uri("https://dev.azure.com/contoso/projB/_git/sub")));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void IsSameOrganization_RepositoryUrlHasNoPathSegments_ReturnsFalse()
        {
            using TestHostContext hc = new(this);
            var tc = CreateContext(hc, collectionUri: null);

            Assert.False(GitSourceProvider.IsSameOrganization(tc,
                new Uri("https://dev.azure.com/"),
                new Uri("https://dev.azure.com/contoso/proj/_git/sub")));
        }

        [Theory]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        [InlineData("/contoso/proj/_git/repo", "/contoso/", true)]
        [InlineData("/contoso/proj/_git/repo", "/contoso", true)]
        [InlineData("/contoso", "/contoso/", true)]
        [InlineData("/CONTOSO/proj", "/contoso/", true)]
        [InlineData("/contoso-other/proj", "/contoso/", false)]
        [InlineData("/contosox/proj", "/contoso", false)]
        [InlineData("/othercorp/proj", "/contoso/", false)]
        [InlineData("/", "/contoso/", false)]
        public void IsPathPrefix_Works(string path, string prefix, bool expected)
        {
            Assert.Equal(expected, GitSourceProvider.IsPathPrefix(path, prefix));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void ResolveGitConfigPath_NormalRepository_UsesDotGitDirectory()
        {
            string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                Directory.CreateDirectory(Path.Combine(root, ".git"));

                Assert.Equal(
                    Path.Combine(root, ".git", "config"),
                    GitSourceProvider.ResolveGitConfigPath(root));
            }
            finally
            {
                if (Directory.Exists(root)) { Directory.Delete(root, true); }
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void ResolveGitConfigPath_DotGitMissing_StillReturnsDotGitConfig()
        {
            string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                Directory.CreateDirectory(root);

                Assert.Equal(
                    Path.Combine(root, ".git", "config"),
                    GitSourceProvider.ResolveGitConfigPath(root));
            }
            finally
            {
                if (Directory.Exists(root)) { Directory.Delete(root, true); }
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void ResolveGitConfigPath_Submodule_FollowsRelativeGitdirPointer()
        {
            string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                string realGitDir = Path.Combine(root, ".git", "modules", "lib");
                Directory.CreateDirectory(realGitDir);

                string submodulePath = Path.Combine(root, "lib");
                Directory.CreateDirectory(submodulePath);
                File.WriteAllText(Path.Combine(submodulePath, ".git"), "gitdir: ../.git/modules/lib\n");

                string resolved = GitSourceProvider.ResolveGitConfigPath(submodulePath);

                Assert.Equal(
                    Path.GetFullPath(Path.Combine(realGitDir, "config")),
                    Path.GetFullPath(resolved));
            }
            finally
            {
                if (Directory.Exists(root)) { Directory.Delete(root, true); }
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void ResolveGitConfigPath_Submodule_FollowsAbsoluteGitdirPointer()
        {
            string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                string realGitDir = Path.Combine(root, "elsewhere", "modules", "lib");
                Directory.CreateDirectory(realGitDir);

                string submodulePath = Path.Combine(root, "lib");
                Directory.CreateDirectory(submodulePath);
                File.WriteAllText(Path.Combine(submodulePath, ".git"), $"gitdir: {realGitDir}");

                Assert.Equal(
                    Path.Combine(realGitDir, "config"),
                    GitSourceProvider.ResolveGitConfigPath(submodulePath));
            }
            finally
            {
                if (Directory.Exists(root)) { Directory.Delete(root, true); }
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void ResolveGitConfigPath_DotGitFileWithoutGitdirPrefix_FallsBack()
        {
            string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllText(Path.Combine(root, ".git"), "something unexpected");

                Assert.Equal(
                    Path.Combine(root, ".git", "config"),
                    GitSourceProvider.ResolveGitConfigPath(root));
            }
            finally
            {
                if (Directory.Exists(root)) { Directory.Delete(root, true); }
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task GitSubmodulePaths_ReturnsPathsRootedAtRepository()
        {
            using TestHostContext hc = new(this);
            var tc = new MockAgentTaskPluginExecutionContext(hc.GetTrace());

            var git = new MockSubmodulePathsGitCliManager();
            git.CommandOutput = new List<string> { "lib", "vendor/tools", string.Empty, "  " };

            string repoRoot = Path.Combine("some", "repo");
            List<string> paths = await git.GitSubmodulePaths(tc, repoRoot, recursive: false, CancellationToken.None);

            Assert.Equal(2, paths.Count);
            Assert.Equal(Path.Combine(repoRoot, "lib"), paths[0]);
            Assert.Equal(Path.Combine(repoRoot, "vendor", "tools"), paths[1]);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task GitSubmodulePaths_RepeatedSpacesInPath_PreservesSpacing()
        {
            using TestHostContext hc = new(this);
            var tc = new MockAgentTaskPluginExecutionContext(hc.GetTrace());

            var git = new MockSubmodulePathsGitCliManager();
            git.CommandOutput = new List<string> { "my  lib", "vendor/two  words" };

            string repoRoot = Path.Combine("some", "repo");
            List<string> paths = await git.GitSubmodulePaths(tc, repoRoot, recursive: false, CancellationToken.None);

            Assert.Equal(2, paths.Count);
            Assert.Equal(Path.Combine(repoRoot, "my  lib"), paths[0]);
            Assert.Equal(Path.Combine(repoRoot, "vendor", "two  words"), paths[1]);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task GitSubmodulePaths_NonRecursive_DoesNotPassRecursiveFlag()
        {
            using TestHostContext hc = new(this);
            var tc = new MockAgentTaskPluginExecutionContext(hc.GetTrace());

            var git = new MockSubmodulePathsGitCliManager();
            await git.GitSubmodulePaths(tc, "repo", recursive: false, CancellationToken.None);

            Assert.Equal("submodule", git.LastCommand);
            Assert.DoesNotContain("--recursive", git.LastOptions);
            Assert.Contains("--quiet \"printf '%s\\n' \\\"$displaypath\\\"\"", git.LastOptions);
            Assert.DoesNotContain("echo", git.LastOptions);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task GitSubmodulePaths_Recursive_PassesRecursiveFlag()
        {
            using TestHostContext hc = new(this);
            var tc = new MockAgentTaskPluginExecutionContext(hc.GetTrace());

            var git = new MockSubmodulePathsGitCliManager();
            await git.GitSubmodulePaths(tc, "repo", recursive: true, CancellationToken.None);

            Assert.Contains("--recursive", git.LastOptions);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task GitSubmodulePaths_CommandFails_ReturnsEmptyListAndDoesNotThrow()
        {
            using TestHostContext hc = new(this);
            var tc = new MockAgentTaskPluginExecutionContext(hc.GetTrace());

            var git = new MockSubmodulePathsGitCliManager();
            git.CommandExitCode = 1;
            git.CommandOutput = new List<string> { "lib" };

            List<string> paths = await git.GitSubmodulePaths(tc, "repo", recursive: false, CancellationToken.None);

            Assert.Empty(paths);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task GitSubmodulePaths_NoSubmodules_ReturnsEmptyList()
        {
            using TestHostContext hc = new(this);
            var tc = new MockAgentTaskPluginExecutionContext(hc.GetTrace());

            var git = new MockSubmodulePathsGitCliManager();

            List<string> paths = await git.GitSubmodulePaths(tc, "repo", recursive: false, CancellationToken.None);

            Assert.Empty(paths);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task PostJobCleanup_SubmoduleWorktreeGone_UnsetsHeaderThroughResolvedConfigPath()
        {
            using TestHostContext hc = new(this);
            var tc = new MockAgentTaskPluginExecutionContext(hc.GetTrace());

            string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                // Lay out .git/modules/lib/config the way git does for a submodule.
                string moduleGitDir = Path.Combine(root, ".git", "modules", "lib");
                Directory.CreateDirectory(moduleGitDir);
                string submoduleConfig = Path.Combine(moduleGitDir, "config");

                const string header = "AUTHORIZATION: bearer TOKEN";
                const string configKey = "http.https://dev.azure.com/contoso/proj/_git/sub/.extraheader";
                File.WriteAllText(submoduleConfig,
                    "[http \"https://dev.azure.com/contoso/proj/_git/sub/\"]\n" +
                    $"\textraheader = {header}\n");

                // The submodule worktree is gone (deleted, or 'git submodule deinit -f').
                Assert.False(Directory.Exists(Path.Combine(root, "lib")));

                var modifications = new Dictionary<string, Dictionary<string, string>>
                {
                    { submoduleConfig, new Dictionary<string, string> { { configKey, header } } }
                };

                tc.TaskVariables["cleanupcreds"] = "true";
                tc.TaskVariables["repoUrlWithCred"] = "https://dev.azure.com/contoso/proj/_git/super";
                tc.TaskVariables["modifiedsubmodulegitconfig"] = JsonUtility.ToString(modifications);

                var repository = new Pipelines.RepositoryResource
                {
                    Alias = "super",
                    Type = Pipelines.RepositoryTypes.Git,
                    Url = new Uri("https://dev.azure.com/contoso/proj/_git/super")
                };
                repository.Properties.Set<string>(Pipelines.RepositoryPropertyNames.Path, root);

                var provider = new MockCleanupGitSourceProvider();
                await provider.PostJobCleanupAsync(tc, repository);

                // The unset targets the stable module config file...
                Assert.Contains(provider.CliManager.ExecutedCommands,
                    c => c.Contains("--unset-all") && c.Contains(submoduleConfig) && c.Contains(configKey));

                // ...and never uses the missing submodule worktree as cwd.
                Assert.DoesNotContain(provider.CliManager.ExecutedRepoRoots,
                    r => r.Equals(Path.Combine(root, "lib"), StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                if (Directory.Exists(root)) { Directory.Delete(root, true); }
            }
        }

        [Theory]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        [InlineData(true)]   // secure placeholder path
        [InlineData(false)]  // ordinary GitConfig path
        public async Task GetSourceAsync_PersistsHeaderForSameOrgSubmoduleOnly(bool useSecureParameterPassing)
        {
            using TestHostContext hc = new(this, $"Secure_{useSecureParameterPassing}");
            var tc = new MockAgentTaskPluginExecutionContext(hc.GetTrace());

            string target = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                // Superproject, plus two submodules pointing into .git/modules/<name>.
                Directory.CreateDirectory(Path.Combine(target, ".git"));
                CreateSubmodule(target, "lib");
                CreateSubmodule(target, "other");

                var endpoint = new ServiceEndpoint()
                {
                    Name = WellKnownServiceEndpointNames.SystemVssConnection,
                    Id = Guid.NewGuid(),
                    Url = new Uri("https://dev.azure.com/contoso/"),
                    Authorization = new EndpointAuthorization()
                    {
                        Scheme = EndpointAuthorizationSchemes.OAuth,
                        Parameters = { { EndpointAuthorizationParameters.AccessToken, "TOKEN" } }
                    }
                };
                tc.Endpoints.Add(endpoint);

                var repository = new Pipelines.RepositoryResource()
                {
                    Alias = "super",
                    Type = Pipelines.RepositoryTypes.Git,
                    Url = new Uri("https://dev.azure.com/contoso/proj/_git/super"),
                    Endpoint = new Pipelines.ServiceEndpointReference() { Id = endpoint.Id }
                };
                repository.Properties.Set<string>(Pipelines.RepositoryPropertyNames.Path, target);
                tc.Repositories.Add(repository);

                tc.Variables.Add("system.collectionuri", "https://dev.azure.com/contoso/");
                tc.Variables.Add("agent.GitUseSecureParameterPassing", useSecureParameterPassing.ToString());

                tc.Inputs[Pipelines.PipelineConstants.CheckoutTaskInputs.PersistCredentials] = "true";
                tc.Inputs[Pipelines.PipelineConstants.CheckoutTaskInputs.Submodules] = "true";

                var provider = new MockSubmoduleFlowGitSourceProvider();
                provider.CliManager.SubmodulePaths = new List<string> { "lib", "other" };
                provider.CliManager.RemoteUrlByRepoRoot[target] =
                    "https://dev.azure.com/contoso/proj/_git/super";
                provider.CliManager.RemoteUrlByRepoRoot[Path.Combine(target, "lib")] =
                    "https://dev.azure.com/contoso/proj/_git/sub";
                provider.CliManager.RemoteUrlByRepoRoot[Path.Combine(target, "other")] =
                    "https://dev.azure.com/othercorp/proj/_git/sub";

                await provider.GetSourceAsync(tc, repository, CancellationToken.None);

                var written = JsonUtility.FromString<Dictionary<string, Dictionary<string, string>>>(
                    tc.TaskVariables.GetValueOrDefault("modifiedsubmodulegitconfig")?.Value);

                Assert.NotNull(written);

                // Only the same-organization submodule is persisted.
                var entry = Assert.Single(written);

                // ...and it is keyed by the resolved gitdir config path, not the worktree.
                Assert.Equal(Path.Combine(target, ".git", "modules", "lib", "config"),
                             Path.GetFullPath(entry.Key));

                var config = Assert.Single(entry.Value);
                Assert.Equal("http.https://dev.azure.com/contoso/proj/_git/sub.extraheader", config.Key);
                Assert.StartsWith("AUTHORIZATION: ", config.Value);

                Assert.DoesNotContain(written.Keys, k => k.Contains("other"));
            }
            finally
            {
                if (Directory.Exists(target)) { Directory.Delete(target, true); }
            }
        }

        private static void CreateSubmodule(string target, string name)
        {
            Directory.CreateDirectory(Path.Combine(target, ".git", "modules", name));
            Directory.CreateDirectory(Path.Combine(target, name));
            File.WriteAllText(Path.Combine(target, name, ".git"), $"gitdir: ../.git/modules/{name}\n");
            File.WriteAllText(Path.Combine(target, ".git", "modules", name, "config"), "[core]\n");
        }
    }
}