// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agent.Plugins.Repository;
using Agent.Sdk;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Plugin
{
    public sealed class RepositoryPluginL0
    {
        private CheckoutTask _checkoutTask;
        private AgentTaskPluginExecutionContext _executionContext;
        private Mock<ISourceProvider> _sourceProvider;
        private Mock<ISourceProviderFactory> _sourceProviderFactory;

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task RepositoryPlugin_CheckoutTask_MergesCheckoutOptions_Basic()
        {
            using (TestHostContext tc = new TestHostContext(this))
            {
                Setup(tc);
                var repository = _executionContext.Repositories.Single();
                repository.Properties.Set(
                    Pipelines.RepositoryPropertyNames.CheckoutOptions,
                    new JObject
                    {
                        { Pipelines.PipelineConstants.CheckoutTaskInputs.Clean, "clean value" },
                        { Pipelines.PipelineConstants.CheckoutTaskInputs.FetchDepth, "fetch depth value" },
                        { Pipelines.PipelineConstants.CheckoutTaskInputs.Lfs, "lfs value" },
                        { Pipelines.PipelineConstants.CheckoutTaskInputs.PersistCredentials, "persist credentials value" },
                        { Pipelines.PipelineConstants.CheckoutTaskInputs.Submodules, "submodules value" },
                    });

                await _checkoutTask.RunAsync(_executionContext, CancellationToken.None);

                Assert.Equal("clean value", _executionContext.Inputs[Pipelines.PipelineConstants.CheckoutTaskInputs.Clean]);
                Assert.Equal("fetch depth value", _executionContext.Inputs[Pipelines.PipelineConstants.CheckoutTaskInputs.FetchDepth]);
                Assert.Equal("lfs value", _executionContext.Inputs[Pipelines.PipelineConstants.CheckoutTaskInputs.Lfs]);
                Assert.Equal("persist credentials value", _executionContext.Inputs[Pipelines.PipelineConstants.CheckoutTaskInputs.PersistCredentials]);
                Assert.Equal("submodules value", _executionContext.Inputs[Pipelines.PipelineConstants.CheckoutTaskInputs.Submodules]);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task RepositoryPlugin_CheckoutTask_MergesCheckoutOptions_CaseInsensitive()
        {
            using (TestHostContext tc = new TestHostContext(this))
            {
                Setup(tc);
                var repository = _executionContext.Repositories.Single();
                repository.Properties.Set(
                    Pipelines.RepositoryPropertyNames.CheckoutOptions,
                    new JObject
                    {
                        { "CLean", "clean value" },
                        { "FETCHdepth", "fetch depth value" },
                        { "LFs", "lfs value" },
                        { "PERSISTcredentials", "persist credentials value" },
                        { "SUBmodules", "submodules value" },
                    });

                await _checkoutTask.RunAsync(_executionContext, CancellationToken.None);

                Assert.Equal("clean value", _executionContext.Inputs[Pipelines.PipelineConstants.CheckoutTaskInputs.Clean]);
                Assert.Equal("fetch depth value", _executionContext.Inputs[Pipelines.PipelineConstants.CheckoutTaskInputs.FetchDepth]);
                Assert.Equal("lfs value", _executionContext.Inputs[Pipelines.PipelineConstants.CheckoutTaskInputs.Lfs]);
                Assert.Equal("persist credentials value", _executionContext.Inputs[Pipelines.PipelineConstants.CheckoutTaskInputs.PersistCredentials]);
                Assert.Equal("submodules value", _executionContext.Inputs[Pipelines.PipelineConstants.CheckoutTaskInputs.Submodules]);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task RepositoryPlugin_CheckoutTask_MergesCheckoutOptions_DoesNotClobberExistingValue()
        {
            using (TestHostContext tc = new TestHostContext(this))
            {
                Setup(tc);
                var repository = _executionContext.Repositories.Single();
                repository.Properties.Set(
                    Pipelines.RepositoryPropertyNames.CheckoutOptions,
                    new JObject
                    {
                        { Pipelines.PipelineConstants.CheckoutTaskInputs.Clean, "clean value" },
                        { Pipelines.PipelineConstants.CheckoutTaskInputs.FetchDepth, "fetch depth value" },
                        { Pipelines.PipelineConstants.CheckoutTaskInputs.Lfs, "lfs value" },
                        { Pipelines.PipelineConstants.CheckoutTaskInputs.PersistCredentials, "persist credentials value" },
                        { Pipelines.PipelineConstants.CheckoutTaskInputs.Submodules, "submodules value" },
                    });
                _executionContext.Inputs[Pipelines.PipelineConstants.CheckoutTaskInputs.Clean] = "existing clean value";
                _executionContext.Inputs["FETCHdepth"] = "existing fetch depth value";
                _executionContext.Inputs[Pipelines.PipelineConstants.CheckoutTaskInputs.Lfs] = string.Empty;
                _executionContext.Inputs[Pipelines.PipelineConstants.CheckoutTaskInputs.PersistCredentials] = null;

                await _checkoutTask.RunAsync(_executionContext, CancellationToken.None);

                Assert.Equal("existing clean value", _executionContext.Inputs[Pipelines.PipelineConstants.CheckoutTaskInputs.Clean]);
                Assert.Equal("existing fetch depth value", _executionContext.Inputs[Pipelines.PipelineConstants.CheckoutTaskInputs.FetchDepth]);
                Assert.Equal("lfs value", _executionContext.Inputs[Pipelines.PipelineConstants.CheckoutTaskInputs.Lfs]);
                Assert.Equal("persist credentials value", _executionContext.Inputs[Pipelines.PipelineConstants.CheckoutTaskInputs.PersistCredentials]);
                Assert.Equal("submodules value", _executionContext.Inputs[Pipelines.PipelineConstants.CheckoutTaskInputs.Submodules]);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task RepositoryPlugin_CheckoutTask_MergesCheckoutOptions_FeatureFlagOff()
        {
            using (TestHostContext tc = new TestHostContext(this))
            {
                Setup(tc);
                var repository = _executionContext.Repositories.Single();
                repository.Properties.Set(
                    Pipelines.RepositoryPropertyNames.CheckoutOptions,
                    new JObject
                    {
                        { Pipelines.PipelineConstants.CheckoutTaskInputs.Clean, "clean value" },
                        { Pipelines.PipelineConstants.CheckoutTaskInputs.FetchDepth, "fetch depth value" },
                        { Pipelines.PipelineConstants.CheckoutTaskInputs.Lfs, "lfs value" },
                        { Pipelines.PipelineConstants.CheckoutTaskInputs.PersistCredentials, "persist credentials value" },
                        { Pipelines.PipelineConstants.CheckoutTaskInputs.Submodules, "submodules value" },
                    });
                _executionContext.Variables["MERGE_CHECKOUT_OPTIONS"] = "FALse";

                await _checkoutTask.RunAsync(_executionContext, CancellationToken.None);

                Assert.False(_executionContext.Inputs.ContainsKey(Pipelines.PipelineConstants.CheckoutTaskInputs.Clean));
                Assert.False(_executionContext.Inputs.ContainsKey(Pipelines.PipelineConstants.CheckoutTaskInputs.FetchDepth));
                Assert.False(_executionContext.Inputs.ContainsKey(Pipelines.PipelineConstants.CheckoutTaskInputs.Lfs));
                Assert.False(_executionContext.Inputs.ContainsKey(Pipelines.PipelineConstants.CheckoutTaskInputs.PersistCredentials));
                Assert.False(_executionContext.Inputs.ContainsKey(Pipelines.PipelineConstants.CheckoutTaskInputs.Submodules));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task RepositoryPlugin_CheckoutTask_MergesCheckoutOptions_UnexpectedCheckoutOption()
        {
            using (TestHostContext tc = new TestHostContext(this))
            {
                Setup(tc);
                var repository = _executionContext.Repositories.Single();
                repository.Properties.Set(
                    Pipelines.RepositoryPropertyNames.CheckoutOptions,
                    new JObject
                    {
                        { Pipelines.PipelineConstants.CheckoutTaskInputs.Clean, "clean value" },
                        { Pipelines.PipelineConstants.CheckoutTaskInputs.FetchDepth, "fetch depth value" },
                        { Pipelines.PipelineConstants.CheckoutTaskInputs.Lfs, "lfs value" },
                        { Pipelines.PipelineConstants.CheckoutTaskInputs.PersistCredentials, "persist credentials value" },
                        { Pipelines.PipelineConstants.CheckoutTaskInputs.Submodules, "submodules value" },
                        { "unexpected", "unexpected value" },
                    });

                await _checkoutTask.RunAsync(_executionContext, CancellationToken.None);

                Assert.Equal("clean value", _executionContext.Inputs[Pipelines.PipelineConstants.CheckoutTaskInputs.Clean]);
                Assert.Equal("fetch depth value", _executionContext.Inputs[Pipelines.PipelineConstants.CheckoutTaskInputs.FetchDepth]);
                Assert.Equal("lfs value", _executionContext.Inputs[Pipelines.PipelineConstants.CheckoutTaskInputs.Lfs]);
                Assert.Equal("persist credentials value", _executionContext.Inputs[Pipelines.PipelineConstants.CheckoutTaskInputs.PersistCredentials]);
                Assert.Equal("submodules value", _executionContext.Inputs[Pipelines.PipelineConstants.CheckoutTaskInputs.Submodules]);
                Assert.False(_executionContext.Inputs.ContainsKey("unexpected"));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task RepositoryPlugin_CleanupTask_MergesCheckoutOptions()
        {
            using (TestHostContext tc = new TestHostContext(this))
            {
                Setup(tc);
                var repository = _executionContext.Repositories.Single();
                repository.Properties.Set(
                    Pipelines.RepositoryPropertyNames.CheckoutOptions,
                    new JObject
                    {
                        { Pipelines.PipelineConstants.CheckoutTaskInputs.Clean, "clean value" },
                        { Pipelines.PipelineConstants.CheckoutTaskInputs.FetchDepth, "fetch depth value" },
                        { Pipelines.PipelineConstants.CheckoutTaskInputs.Lfs, "lfs value" },
                        { Pipelines.PipelineConstants.CheckoutTaskInputs.PersistCredentials, "persist credentials value" },
                        { Pipelines.PipelineConstants.CheckoutTaskInputs.Submodules, "submodules value" },
                    });

                await _checkoutTask.RunAsync(_executionContext, CancellationToken.None);

                Assert.Equal("clean value", _executionContext.Inputs[Pipelines.PipelineConstants.CheckoutTaskInputs.Clean]);
                Assert.Equal("fetch depth value", _executionContext.Inputs[Pipelines.PipelineConstants.CheckoutTaskInputs.FetchDepth]);
                Assert.Equal("lfs value", _executionContext.Inputs[Pipelines.PipelineConstants.CheckoutTaskInputs.Lfs]);
                Assert.Equal("persist credentials value", _executionContext.Inputs[Pipelines.PipelineConstants.CheckoutTaskInputs.PersistCredentials]);
                Assert.Equal("submodules value", _executionContext.Inputs[Pipelines.PipelineConstants.CheckoutTaskInputs.Submodules]);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task RepositoryPlugin_NoPathInput()
        {
            using (TestHostContext tc = new TestHostContext(this))
            {
                Setup(tc);
                var repository = _executionContext.Repositories.Single();
                var currentPath = repository.Properties.Get<string>(Pipelines.RepositoryPropertyNames.Path);
                Directory.CreateDirectory(currentPath);

                await _checkoutTask.RunAsync(_executionContext, CancellationToken.None);

                var actualPath = repository.Properties.Get<string>(Pipelines.RepositoryPropertyNames.Path);

                Assert.Equal(actualPath, currentPath);

                var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                File.Copy(tc.TraceFileName, temp);
                Assert.True(File.ReadAllText(temp).Contains($"##vso[plugininternal.updaterepositorypath alias=myRepo;]{actualPath}"));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task RepositoryPlugin_PathInputMoveFolder()
        {
            using (TestHostContext tc = new TestHostContext(this))
            {
                Setup(tc);
                var repository = _executionContext.Repositories.Single();
                var currentPath = repository.Properties.Get<string>(Pipelines.RepositoryPropertyNames.Path);
                Directory.CreateDirectory(currentPath);

                _executionContext.Inputs["Path"] = "test";

                await _checkoutTask.RunAsync(_executionContext, CancellationToken.None);

                var actualPath = repository.Properties.Get<string>(Pipelines.RepositoryPropertyNames.Path);

                Assert.NotEqual(actualPath, currentPath);
                Assert.Equal(actualPath, Path.Combine(tc.GetDirectory(WellKnownDirectory.Work), "1", "test"));
                Assert.True(Directory.Exists(actualPath));
                Assert.False(Directory.Exists(currentPath));

                var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                File.Copy(tc.TraceFileName, temp);
                Assert.True(File.ReadAllText(temp).Contains($"##vso[plugininternal.updaterepositorypath alias=myRepo;]{actualPath}"));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void RepositoryPlugin_HandleProxyConfig()
        {
            using TestHostContext tc = new TestHostContext(this);
            var proxyUrl = "http://example.com:80";
            var proxyUser = "proxy_user";
            var proxyPassword = "proxy_password";

            AgentTaskPluginExecutionContext hostContext = new AgentTaskPluginExecutionContext()
            {
                Endpoints = new List<ServiceEndpoint>(),
                Inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                   
                },
                Repositories = new List<Pipelines.RepositoryResource>(),
                Variables = new Dictionary<string, VariableValue>(StringComparer.OrdinalIgnoreCase)
                {
                    { AgentWebProxySettings.AgentProxyUrlKey, proxyUrl },
                    { AgentWebProxySettings.AgentProxyUsernameKey, proxyUser },
                    { AgentWebProxySettings.AgentProxyPasswordKey, proxyPassword },

                }
            };
            var systemConnection = new ServiceEndpoint()
            {
                Name = WellKnownServiceEndpointNames.SystemVssConnection,
                Id = Guid.NewGuid(),
                Url = new Uri("https://dev.azure.com/test"),
                Authorization = new EndpointAuthorization()
                {
                    Scheme = EndpointAuthorizationSchemes.OAuth,
                    Parameters = { { EndpointAuthorizationParameters.AccessToken, "Test" } }
                }
            };

            hostContext.Endpoints.Add(systemConnection);
            Assert.NotNull(hostContext.VssConnection);
            Assert.Equal(hostContext.WebProxySettings.ProxyAddress, proxyUrl);
            Assert.Equal(hostContext.WebProxySettings.ProxyUsername, proxyUser);
            Assert.Equal(hostContext.WebProxySettings.ProxyPassword, proxyPassword);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task RepositoryPlugin_NoPathInputMoveBackToDefault()
        {
            using (TestHostContext tc = new TestHostContext(this))
            {
                var trace = tc.GetTrace();
                Setup(tc);
                var repository = _executionContext.Repositories.Single();
                repository.Properties.Set(Pipelines.RepositoryPropertyNames.Path, Path.Combine(tc.GetDirectory(WellKnownDirectory.Work), "1", "test"));
                var currentPath = repository.Properties.Get<string>(Pipelines.RepositoryPropertyNames.Path);
                Directory.CreateDirectory(currentPath);

                await _checkoutTask.RunAsync(_executionContext, CancellationToken.None);
                var actualPath = repository.Properties.Get<string>(Pipelines.RepositoryPropertyNames.Path);

                Assert.Equal(actualPath, Path.Combine(tc.GetDirectory(WellKnownDirectory.Work), "1", "s"));
                Assert.True(Directory.Exists(actualPath));
                Assert.False(Directory.Exists(currentPath));

                var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                File.Copy(tc.TraceFileName, temp);
                Assert.True(File.ReadAllText(temp).Contains($"##vso[plugininternal.updaterepositorypath alias=myRepo;]{actualPath}"));
            }
        }

        public async Task RepositoryPlugin_InvalidPathInputDirectlyToBuildDirectory_DontAllowWorkingDirectoryRepository()
        {
            using (TestHostContext tc = new TestHostContext(this))
            {
                var trace = tc.GetTrace();
                Setup(tc);
                var repository = _executionContext.Repositories.Single();
                var currentPath = repository.Properties.Get<string>(Pipelines.RepositoryPropertyNames.Path);
                Directory.CreateDirectory(currentPath);
                _executionContext.Inputs["Path"] = $"..{Path.DirectorySeparatorChar}1";

                var ex = await Assert.ThrowsAsync<ArgumentException>(async () => await _checkoutTask.RunAsync(_executionContext, CancellationToken.None));
                Assert.True(ex.Message.Contains("should resolve to a directory under"));

                var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                File.Copy(tc.TraceFileName, temp);
                Assert.False(File.ReadAllText(temp).Contains($"##vso[plugininternal.updaterepositorypath alias=myRepo;]"));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task RepositoryPlugin_InvalidPathInputDirectlyToWorkingDirectory_AllowWorkingDirectoryRepositorie()
        {
            using (TestHostContext tc = new TestHostContext(this))
            {
                var trace = tc.GetTrace();
                Setup(tc, allowWorkDirectory: "true");
                var repository = _executionContext.Repositories.Single();
                var currentPath = repository.Properties.Get<string>(Pipelines.RepositoryPropertyNames.Path);
                Directory.CreateDirectory(currentPath);
                _executionContext.Inputs["Path"] = $"..";

                var ex = await Assert.ThrowsAsync<ArgumentException>(async () => await _checkoutTask.RunAsync(_executionContext, CancellationToken.None));
                Assert.True(ex.Message.Contains("should resolve to a directory under"));

                var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                File.Copy(tc.TraceFileName, temp);
                Assert.False(File.ReadAllText(temp).Contains($"##vso[plugininternal.updaterepositorypath alias=myRepo;]"));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task RepositoryPlugin_InvalidPathInput_DontAllowWorkingDirectoryRepositorie()
        {
            using (TestHostContext tc = new TestHostContext(this))
            {
                var trace = tc.GetTrace();
                Setup(tc);
                var repository = _executionContext.Repositories.Single();
                var currentPath = repository.Properties.Get<string>(Pipelines.RepositoryPropertyNames.Path);
                Directory.CreateDirectory(currentPath);
                _executionContext.Inputs["Path"] = $"..{Path.DirectorySeparatorChar}test{Path.DirectorySeparatorChar}foo";

                var ex = await Assert.ThrowsAsync<ArgumentException>(async () => await _checkoutTask.RunAsync(_executionContext, CancellationToken.None));
                Assert.True(ex.Message.Contains("should resolve to a directory under"));

                var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                File.Copy(tc.TraceFileName, temp);
                Assert.False(File.ReadAllText(temp).Contains($"##vso[plugininternal.updaterepositorypath alias=myRepo;]"));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task RepositoryPlugin_ValidPathInput_AllowWorkingDirectoryRepositorie()
        {
            using (TestHostContext tc = new TestHostContext(this))
            {
                var trace = tc.GetTrace();
                Setup(tc, allowWorkDirectory: "true");
                var repository = _executionContext.Repositories.Single();
                var currentPath = repository.Properties.Get<string>(Pipelines.RepositoryPropertyNames.Path);
                Directory.CreateDirectory(currentPath);
                _executionContext.Inputs["Path"] = $"..{Path.DirectorySeparatorChar}test{Path.DirectorySeparatorChar}foo";


                await _checkoutTask.RunAsync(_executionContext, CancellationToken.None);

                var actualPath = repository.Properties.Get<string>(Pipelines.RepositoryPropertyNames.Path);

                Assert.NotEqual(actualPath, currentPath);
                Assert.Equal(actualPath, Path.Combine(tc.GetDirectory(WellKnownDirectory.Work), "test", "foo"));
                Assert.True(Directory.Exists(actualPath));
                Assert.False(Directory.Exists(currentPath));

                var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                File.Copy(tc.TraceFileName, temp);
                Assert.True(File.ReadAllText(temp).Contains($"##vso[plugininternal.updaterepositorypath alias=myRepo;]{actualPath}"));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task RepositoryPlugin_InvalidPathInput_AllowWorkingDirectoryRepositorie()
        {
            using (TestHostContext tc = new TestHostContext(this))
            {
                var trace = tc.GetTrace();
                Setup(tc, allowWorkDirectory: "true");
                var repository = _executionContext.Repositories.Single();
                var currentPath = repository.Properties.Get<string>(Pipelines.RepositoryPropertyNames.Path);
                Directory.CreateDirectory(currentPath);
                _executionContext.Inputs["Path"] = $"..{Path.DirectorySeparatorChar}..{Path.DirectorySeparatorChar}test{Path.DirectorySeparatorChar}foo";

                var ex = await Assert.ThrowsAsync<ArgumentException>(async () => await _checkoutTask.RunAsync(_executionContext, CancellationToken.None));
                Assert.True(ex.Message.Contains("should resolve to a directory under"));
                var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                File.Copy(tc.TraceFileName, temp);
                Assert.False(File.ReadAllText(temp).Contains($"##vso[plugininternal.updaterepositorypath alias=myRepo;]"));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task RepositoryPlugin_UpdatePathEvenCheckoutFail()
        {
            using (TestHostContext tc = new TestHostContext(this))
            {
                var trace = tc.GetTrace();
                Setup(tc);

                _sourceProvider.Setup(x => x.GetSourceAsync(It.IsAny<AgentTaskPluginExecutionContext>(), It.IsAny<Pipelines.RepositoryResource>(), It.IsAny<CancellationToken>()))
                               .Throws(new InvalidOperationException("RIGHT"));

                var repository = _executionContext.Repositories.Single();
                var currentPath = repository.Properties.Get<string>(Pipelines.RepositoryPropertyNames.Path);
                Directory.CreateDirectory(currentPath);

                var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await _checkoutTask.RunAsync(_executionContext, CancellationToken.None));
                Assert.True(ex.Message.Contains("RIGHT"));

                var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                File.Copy(tc.TraceFileName, temp);
                Assert.True(File.ReadAllText(temp).Contains($"##vso[plugininternal.updaterepositorypath alias=myRepo;]{currentPath}"));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task RepositoryPlugin_MultiCheckout_UpdatePathForAllRepos()
        {
            using (TestHostContext tc = new TestHostContext(this))
            {
                var trace = tc.GetTrace();
                var repos = new List<Pipelines.RepositoryResource>()
                {
                    GetRepository(tc, "self", "self"),
                    GetRepository(tc, "repo2", "repo2"),
                    GetRepository(tc, "repo3", "repo3"),
                };

                Setup(tc, repos);

                foreach (var repository in _executionContext.Repositories)
                {
                    var currentPath = repository.Properties.Get<string>(Pipelines.RepositoryPropertyNames.Path);
                    Directory.CreateDirectory(currentPath);

                    _executionContext.Inputs[Pipelines.PipelineConstants.CheckoutTaskInputs.Repository] = repository.Alias;
                    _executionContext.Inputs["Path"] = Path.Combine("test", repository.Alias);
                    await _checkoutTask.RunAsync(_executionContext, CancellationToken.None);

                    var actualPath = repository.Properties.Get<string>(Pipelines.RepositoryPropertyNames.Path);

                    Assert.NotEqual(actualPath, currentPath);
                    Assert.Equal(actualPath, Path.Combine(tc.GetDirectory(WellKnownDirectory.Work), "1", Path.Combine("test", repository.Alias)));
                    Assert.True(Directory.Exists(actualPath));
                    Assert.False(Directory.Exists(currentPath));

                    var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                    File.Copy(tc.TraceFileName, temp);
                    Assert.True(File.ReadAllText(temp).Contains($"##vso[plugininternal.updaterepositorypath alias={repository.Alias};]{actualPath}"), $"Repo {repository.Alias} did not get updated to {actualPath}. CurrentPath = {currentPath}");
                }
            }
        }

        private Pipelines.RepositoryResource GetRepository(TestHostContext hostContext, String alias, String relativePath)
        {
            var workFolder = hostContext.GetDirectory(WellKnownDirectory.Work);
            var repo = new Pipelines.RepositoryResource()
            {
                Alias = alias,
                Type = Pipelines.RepositoryTypes.Git,
            };
            repo.Properties.Set<string>(Pipelines.RepositoryPropertyNames.Path, Path.Combine(workFolder, "1", relativePath));

            return repo;
        }

        private void Setup(TestHostContext hostContext, string allowWorkDirectory = "false")
        {
            Setup(hostContext, new List<Pipelines.RepositoryResource>() { GetRepository(hostContext, "myRepo", "s") }, allowWorkDirectory);
        }

        private void Setup(TestHostContext hostContext, List<Pipelines.RepositoryResource> repos, string allowWorkDirectory = "false")
        {
            _executionContext = new AgentTaskPluginExecutionContext(hostContext.GetTrace())
            {
                Endpoints = new List<ServiceEndpoint>(),
                Inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { Pipelines.PipelineConstants.CheckoutTaskInputs.Repository, "myRepo" },
                },
                Repositories = repos,
                Variables = new Dictionary<string, VariableValue>(StringComparer.OrdinalIgnoreCase)
                {
                    {
                        "agent.builddirectory",
                         Path.Combine(hostContext.GetDirectory(WellKnownDirectory.Work), "1")
                    },
                    {
                        "agent.workfolder",
                        hostContext.GetDirectory(WellKnownDirectory.Work)
                    },
                    {
                        "agent.tempdirectory",
                        hostContext.GetDirectory(WellKnownDirectory.Temp)
                    },
                    {
                        "AZP_AGENT_ALLOW_WORK_DIRECTORY_REPOSITORIES",
                        allowWorkDirectory
                    }
                },
                JobSettings = new Dictionary<string, string>()
                {
                    // Set HasMultipleCheckouts to true if the number of repos is greater than 1
                    { WellKnownJobSettings.HasMultipleCheckouts, (repos.Count > 1).ToString() }
                },
            };

            _sourceProvider = new Mock<ISourceProvider>();

            _sourceProviderFactory = new Mock<ISourceProviderFactory>();
            _sourceProviderFactory
                .Setup(x => x.GetSourceProvider(It.IsAny<String>()))
                .Returns(_sourceProvider.Object);

            _checkoutTask = new CheckoutTask(_sourceProviderFactory.Object);
        }
    }
}
