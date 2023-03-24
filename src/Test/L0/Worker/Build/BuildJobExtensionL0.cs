// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.Build;
using Microsoft.VisualStudio.Services.Agent.Worker.Release;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;
using Moq;
using Xunit;
using Agent.Sdk;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker.Build
{
    public sealed class BuildJobExtensionL0
    {
        private Mock<IExecutionContext> _ec;
        private Mock<IExtensionManager> _extensionManager;
        private Mock<ISourceProvider> _sourceProvider;
        private Mock<IBuildDirectoryManager> _buildDirectoryManager;
        private Mock<IConfigurationStore> _configurationStore;
        private Variables _variables;
        private string stubWorkFolder;
        private BuildJobExtension buildJobExtension;
        private List<Pipelines.JobStep> steps;
        private List<Pipelines.RepositoryResource> repositories { get; set; }
        private Dictionary<string, string> jobSettings { get; set; }

        private const string CollectionId = "31ffacb8-b468-4e60-b2f9-c50ce437da92";
        private const string DefinitionId = "1234";
        private Pipelines.WorkspaceOptions _workspaceOptions;
        private char directorySeparator = Path.DirectorySeparatorChar;

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void CheckSingleRepoWithoutPathInput()
        {
            using (TestHostContext tc = Setup(createWorkDirectory: false, checkOutConfig: CheckoutConfigType.SingleCheckoutDefaultPath))
            {
                buildJobExtension.InitializeJobExtension(_ec.Object, steps, _workspaceOptions);
                var repoLocalPath = _ec.Object.Variables.Get(Constants.Variables.Build.RepoLocalPath);
                Assert.NotNull(repoLocalPath);
                Assert.Equal(Path.Combine(stubWorkFolder, $"1{directorySeparator}s"), repoLocalPath);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void CheckSingleRepoWithCustomPaths()
        {
            using (TestHostContext tc = Setup(createWorkDirectory: false, checkOutConfig: CheckoutConfigType.SingleCheckoutCustomPath, pathToSelfRepo: "s/CustomApplicationFolder"))
            {
                buildJobExtension.InitializeJobExtension(_ec.Object, steps, _workspaceOptions);
                var repoLocalPath = _ec.Object.Variables.Get(Constants.Variables.Build.RepoLocalPath);
                Assert.NotNull(repoLocalPath);
                Assert.Equal(Path.Combine(stubWorkFolder, $"1{directorySeparator}s"), repoLocalPath);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void CheckMultiRepoSelfIsFirstCheckoutWithoutPathInput()
        {
            using (TestHostContext tc = Setup(createWorkDirectory: false, checkOutConfig: CheckoutConfigType.MultiCheckoutDefaultPath))
            {
                buildJobExtension.InitializeJobExtension(_ec.Object, steps, _workspaceOptions);
                var repoLocalPath = _ec.Object.Variables.Get(Constants.Variables.Build.RepoLocalPath);
                Assert.NotNull(repoLocalPath);
                Assert.Equal(Path.Combine(stubWorkFolder, $"1{directorySeparator}s"), repoLocalPath);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void CheckMultiRepoSelfIsFirstCheckoutWithPathInputToCustomPath()
        {
            using (TestHostContext tc = Setup(createWorkDirectory: false, checkOutConfig: CheckoutConfigType.MultiCheckoutCustomPath, pathToSelfRepo: "s/CustomApplicationFolder"))
            {
                buildJobExtension.InitializeJobExtension(_ec.Object, steps, _workspaceOptions);
                var repoLocalPath = _ec.Object.Variables.Get(Constants.Variables.Build.RepoLocalPath);
                Assert.NotNull(repoLocalPath);
                Assert.Equal(Path.Combine(stubWorkFolder, $"1{directorySeparator}s{directorySeparator}App"), repoLocalPath);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void CheckMultiRepoSelfIsFirstCheckoutWithPathInputToDefaultPath()
        {
            using (TestHostContext tc = Setup(createWorkDirectory: false, checkOutConfig: CheckoutConfigType.MultiCheckoutCustomPath, pathToSelfRepo: "s/App"))
            {
                buildJobExtension.InitializeJobExtension(_ec.Object, steps, _workspaceOptions);
                var repoLocalPath = _ec.Object.Variables.Get(Constants.Variables.Build.RepoLocalPath);
                Assert.NotNull(repoLocalPath);
                Assert.Equal(Path.Combine(stubWorkFolder, $"1{directorySeparator}s"), repoLocalPath);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void CheckMultiRepoSelfIsSecondCheckoutWithoutPathInput()
        {
            using (TestHostContext tc = Setup(createWorkDirectory: false, checkOutConfig: CheckoutConfigType.MultiCheckoutDefaultPath, selfRepoIsFirstCheckout: false))
            {
                buildJobExtension.InitializeJobExtension(_ec.Object, steps, _workspaceOptions);
                var repoLocalPath = _ec.Object.Variables.Get(Constants.Variables.Build.RepoLocalPath);
                Assert.NotNull(repoLocalPath);
                Assert.Equal(Path.Combine(stubWorkFolder, $"1{directorySeparator}s"), repoLocalPath);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void CheckMultiRepoSelfIsSecondCheckoutWithPathInputToCustomPath()
        {
            using (TestHostContext tc = Setup(createWorkDirectory: false, checkOutConfig: CheckoutConfigType.MultiCheckoutCustomPath, pathToSelfRepo: "s/CustomApplicationFolder", selfRepoIsFirstCheckout: false))
            {
                buildJobExtension.InitializeJobExtension(_ec.Object, steps, _workspaceOptions);
                var repoLocalPath = _ec.Object.Variables.Get(Constants.Variables.Build.RepoLocalPath);
                Assert.NotNull(repoLocalPath);
                Assert.Equal(Path.Combine(stubWorkFolder, $"1{directorySeparator}s{directorySeparator}App"), repoLocalPath);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void CheckMultiRepoSelfIsSecondCheckoutWithPathInputToDefaultPath()
        {
            using (TestHostContext tc = Setup(createWorkDirectory: false, checkOutConfig: CheckoutConfigType.MultiCheckoutCustomPath, pathToSelfRepo: "s/App", selfRepoIsFirstCheckout: false))
            {
                buildJobExtension.InitializeJobExtension(_ec.Object, steps, _workspaceOptions);
                var repoLocalPath = _ec.Object.Variables.Get(Constants.Variables.Build.RepoLocalPath);
                Assert.NotNull(repoLocalPath);
                Assert.Equal(Path.Combine(stubWorkFolder, $"1{directorySeparator}s"), repoLocalPath);
            }
        }

        private TestHostContext Setup([CallerMemberName] string name = "",
            bool createWorkDirectory = true,
            CheckoutConfigType checkOutConfig = CheckoutConfigType.SingleCheckoutDefaultPath,
            string pathToSelfRepo = "",
            bool selfRepoIsFirstCheckout = true)
        {
            bool isMulticheckoutScenario = checkOutConfig == CheckoutConfigType.MultiCheckoutCustomPath || checkOutConfig == CheckoutConfigType.MultiCheckoutDefaultPath;
            bool isCustomPathScenario = checkOutConfig == CheckoutConfigType.SingleCheckoutCustomPath || checkOutConfig == CheckoutConfigType.MultiCheckoutCustomPath;

            TestHostContext hc = new TestHostContext(this, name);
            this.stubWorkFolder = hc.GetDirectory(WellKnownDirectory.Work);
            if (createWorkDirectory)
            {
                Directory.CreateDirectory(this.stubWorkFolder);
            }

            _ec = new Mock<IExecutionContext>();

            _extensionManager = new Mock<IExtensionManager>();
            _sourceProvider = new Mock<ISourceProvider>();
            _buildDirectoryManager = new Mock<IBuildDirectoryManager>();
            _workspaceOptions = new Pipelines.WorkspaceOptions();
            _configurationStore = new Mock<IConfigurationStore>();
            _configurationStore.Setup(store => store.GetSettings()).Returns(new AgentSettings { WorkFolder = this.stubWorkFolder });

            steps = new List<Pipelines.JobStep>();
            var selfCheckoutTask = new Pipelines.TaskStep()
            {
                Reference = new Pipelines.TaskStepDefinitionReference()
                {
                    Id = Guid.Parse("6d15af64-176c-496d-b583-fd2ae21d4df4"),
                    Name = "Checkout",
                    Version = "1.0.0"
                }
            };
            selfCheckoutTask.Inputs.Add("repository", "self");
            if (isCustomPathScenario)
            {
                selfCheckoutTask.Inputs.Add("path", pathToSelfRepo);
            }

            if(selfRepoIsFirstCheckout)
            {
                steps.Add(selfCheckoutTask);
            }

            // Setup second checkout only for multicheckout jobs
            if (isMulticheckoutScenario)
            {
                var anotherCheckoutTask = new Pipelines.TaskStep()
                {
                    Reference = new Pipelines.TaskStepDefinitionReference()
                    {
                        Id = Guid.Parse("6d15af64-176c-496d-b583-fd2ae21d4df4"),
                        Name = "Checkout",
                        Version = "1.0.0"
                    }
                };
                anotherCheckoutTask.Inputs.Add("repository", "BuildRepo");
                anotherCheckoutTask.Inputs.Add("path", "s/BuildRepo");
                steps.Add(anotherCheckoutTask);
            }

            if (!selfRepoIsFirstCheckout)
            {
                steps.Add(selfCheckoutTask);
            }

            hc.SetSingleton(_buildDirectoryManager.Object);
            hc.SetSingleton(_extensionManager.Object);
            hc.SetSingleton(_configurationStore.Object);

            var buildVariables = GetBuildVariables();
            _variables = new Variables(hc, buildVariables, out _);
            _ec.Setup(x => x.Variables).Returns(_variables);

            repositories = new List<Pipelines.RepositoryResource>();
            repositories.Add(GetRepository(hc, "self", "App", "App"));
            repositories.Add(GetRepository(hc, "repo2", "BuildRepo", "BuildRepo"));
            _ec.Setup(x => x.Repositories).Returns(repositories);

            jobSettings = new Dictionary<string, string>();
            jobSettings.Add(WellKnownJobSettings.HasMultipleCheckouts, isMulticheckoutScenario.ToString());
            jobSettings.Add(WellKnownJobSettings.FirstRepositoryCheckedOut, selfRepoIsFirstCheckout ? "self" : "BuildRepo");
            _ec.Setup(x => x.JobSettings).Returns(jobSettings);

            _ec.Setup(x =>
                x.SetVariable(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .Callback((string varName, string varValue, bool isSecret, bool isOutput, bool isFilePath, bool isReadOnly) => { _variables.Set(varName, varValue, false); });

            _extensionManager.Setup(x => x.GetExtensions<ISourceProvider>())
                .Returns(new List<ISourceProvider> { _sourceProvider.Object });

            _sourceProvider.Setup(x => x.RepositoryType)
                .Returns(Pipelines.RepositoryTypes.ExternalGit);

            _buildDirectoryManager.Setup(x => x.PrepareDirectory(_ec.Object, repositories, _workspaceOptions))
                 .Returns(new TrackingConfig(_ec.Object, repositories, 1));

            buildJobExtension = new BuildJobExtension();
            buildJobExtension.Initialize(hc);
            return hc;
        }

        private Dictionary<string, VariableValue> GetBuildVariables()
        {
            var buildVariables = new Dictionary<string, VariableValue>();
            buildVariables.Add(Constants.Variables.Build.SyncSources, Boolean.TrueString);
            buildVariables.Add(Constants.Variables.System.CollectionId, CollectionId);
            buildVariables.Add(Constants.Variables.System.DefinitionId, DefinitionId);

            return buildVariables;
        }

        private Pipelines.RepositoryResource GetRepository(TestHostContext hostContext, String alias, String relativePath, String Name)
        {
            var workFolder = hostContext.GetDirectory(WellKnownDirectory.Work);
            var repo = new Pipelines.RepositoryResource()
            {
                Alias = alias,
                Type = Pipelines.RepositoryTypes.ExternalGit,
                Id = alias,
                Url = new Uri($"http://contoso.visualstudio.com/{Name}"),
                Name = Name,
            };
            repo.Properties.Set<string>(Pipelines.RepositoryPropertyNames.Path, Path.Combine(workFolder, "1", relativePath));
            if(alias == "self")
            {
                repo.Properties.Set<bool>(Agent.Util.RepositoryUtil.IsPrimaryRepository, true);
            }

            return repo;
        }

        private enum CheckoutConfigType
        {
            MultiCheckoutDefaultPath = 0,
            MultiCheckoutCustomPath = 1,
            SingleCheckoutDefaultPath = 2,
            SingleCheckoutCustomPath = 3,
        }
    }
}
