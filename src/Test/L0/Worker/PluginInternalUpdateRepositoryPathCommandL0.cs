using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.TeamFoundation.DistributedTask.Pipelines;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent;
using Microsoft.VisualStudio.Services.Agent.Tests;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Moq;
using Xunit;

namespace Test.L0.Worker
{
    public class PluginInternalUpdateRepositoryPathCommandL0
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "PluginInternalCommandExtension")]
        public void Execute_should_throw_appropriately()
        {
            using (TestHostContext hc = CreateTestContext())
            {
                InitializeExecutionContext(hc);
                var updateRepoPath = new PluginInternalUpdateRepositoryPathCommand();
                var command = new Microsoft.VisualStudio.Services.Agent.Command("area", "event");

                // missing alias
                Assert.Throws<ArgumentNullException>(() => updateRepoPath.Execute(_ec.Object, command));

                // add alias, still missing matching repository                
                command.Properties.Add("alias", "repo1");
                Assert.Throws<ArgumentNullException>(() => updateRepoPath.Execute(_ec.Object, command));

                // add repository, still missing data
                _repositories.Add(new RepositoryResource() { Alias = "repo1" });
                Assert.Throws<ArgumentNullException>(() => updateRepoPath.Execute(_ec.Object, command));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "PluginInternalCommandExtension")]
        public void Execute_should_set_paths_appropriately_for_self_repo()
        {
            using (TestHostContext hc = CreateTestContext())
            {
                InitializeExecutionContext(hc);
                var updateRepoPath = new PluginInternalUpdateRepositoryPathCommand();
                var command = new Microsoft.VisualStudio.Services.Agent.Command("area", "event");
                command.Properties.Add("alias", "self");
                command.Data = "/1/newPath";

                updateRepoPath.Execute(_ec.Object, command);

                Assert.Equal("/1/newPath", _selfRepo.Properties.Get<String>(RepositoryPropertyNames.Path));
                Assert.Equal("/1/otherRepo", _otherRepo.Properties.Get<String>(RepositoryPropertyNames.Path));
                Assert.Equal("/1/newPath", _variables.Get(Constants.Variables.Build.SourcesDirectory));
                Assert.Equal("/1/newPath", _variables.Get(Constants.Variables.Build.RepoLocalPath));
                Assert.Equal("/1/newPath", _variables.Get(Constants.Variables.System.DefaultWorkingDirectory));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "PluginInternalCommandExtension")]
        public void Execute_should_set_paths_appropriately_for_nonSelf_repo()
        {
            using (TestHostContext hc = CreateTestContext())
            {
                InitializeExecutionContext(hc);
                var updateRepoPath = new PluginInternalUpdateRepositoryPathCommand();
                var command = new Microsoft.VisualStudio.Services.Agent.Command("area", "event");
                command.Properties.Add("alias", "repo2");
                command.Data = "/1/newPath";

                updateRepoPath.Execute(_ec.Object, command);

                Assert.Equal("/1/s", _selfRepo.Properties.Get<String>(RepositoryPropertyNames.Path));
                Assert.Equal("/1/newPath", _otherRepo.Properties.Get<String>(RepositoryPropertyNames.Path));
                Assert.Equal("/1/newPath", _variables.Get(Constants.Variables.Build.SourcesDirectory));
                Assert.Equal("/1/newPath", _variables.Get(Constants.Variables.Build.RepoLocalPath));
                Assert.Equal("/1/newPath", _variables.Get(Constants.Variables.System.DefaultWorkingDirectory));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "PluginInternalCommandExtension")]
        public void Execute_should_set_paths_appropriately_for_self_repo_with_multiple_checkouts()
        {
            using (TestHostContext hc = CreateTestContext())
            {
                InitializeExecutionContext(hc, true);
                var updateRepoPath = new PluginInternalUpdateRepositoryPathCommand();
                var command = new Microsoft.VisualStudio.Services.Agent.Command("area", "event");
                command.Properties.Add("alias", "self");
                command.Data = "/1/newPath";

                updateRepoPath.Execute(_ec.Object, command);

                Assert.Equal("/1/newPath", _selfRepo.Properties.Get<String>(RepositoryPropertyNames.Path));
                Assert.Equal("/1/otherRepo", _otherRepo.Properties.Get<String>(RepositoryPropertyNames.Path));
                Assert.Equal(null, _variables.Get(Constants.Variables.Build.SourcesDirectory));
                Assert.Equal("newPath", GetLastPathPart(_variables.Get(Constants.Variables.Build.RepoLocalPath)));
                Assert.Equal(null, _variables.Get(Constants.Variables.System.DefaultWorkingDirectory));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "PluginInternalCommandExtension")]
        public void Execute_should_set_paths_appropriately_for_nonSelf_repo_with_multiple_checkouts()
        {
            using (TestHostContext hc = CreateTestContext())
            {
                InitializeExecutionContext(hc, true);
                var updateRepoPath = new PluginInternalUpdateRepositoryPathCommand();
                var command = new Microsoft.VisualStudio.Services.Agent.Command("area", "event");
                command.Properties.Add("alias", "repo2");
                command.Data = "/1/newPath";

                updateRepoPath.Execute(_ec.Object, command);

                Assert.Equal("/1/s", _selfRepo.Properties.Get<String>(RepositoryPropertyNames.Path));
                Assert.Equal("/1/newPath", _otherRepo.Properties.Get<String>(RepositoryPropertyNames.Path));
                Assert.Equal(null, _variables.Get(Constants.Variables.Build.SourcesDirectory));
                Assert.Equal(null, GetLastPathPart(_variables.Get(Constants.Variables.Build.RepoLocalPath)));
                Assert.Equal(null, _variables.Get(Constants.Variables.System.DefaultWorkingDirectory));
            }
        }

        private TestHostContext CreateTestContext([CallerMemberName] String testName = "")
        {
            var hc = new TestHostContext(this, testName);
            _expressionManager = new ExpressionManager();
            _expressionManager.Initialize(hc);
            return hc;
        }

        private void InitializeExecutionContext(TestHostContext hc, bool isMultiCheckout = false)
        {
            List<string> warnings;
            _variables = new Variables(
                hostContext: hc,
                copy: new Dictionary<string, VariableValue>(),
                warnings: out warnings);
            _repositories = new List<RepositoryResource>();
            _selfRepo = new RepositoryResource()
            {
                Alias = "self",
                Id = Guid.NewGuid().ToString(),
                Name = "mainRepo",
                Type = "git",
            };
            _selfRepo.Properties.Set(RepositoryPropertyNames.Path, "/1/s");
            _otherRepo = new RepositoryResource()
            {
                Alias = "repo2",
                Id = Guid.NewGuid().ToString(),
                Name = "otherRepo",
                Type = "git",
            };
            _otherRepo.Properties.Set(RepositoryPropertyNames.Path, "/1/otherRepo");
            _repositories.Add(_selfRepo);
            _repositories.Add(_otherRepo);

            _ec = new Mock<IExecutionContext>();
            _ec.SetupAllProperties();
            _ec.Setup(x => x.Variables).Returns(_variables);
            _ec.Setup(x => x.Repositories).Returns(_repositories);
            _ec.Setup(x => x.GetHostContext()).Returns(hc);
            _ec.Setup(x => x.SetVariable(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .Callback<string, string, bool, bool, bool, bool, bool>((name, value, secret, b2, b3, readOnly, preserveCase) => _variables.Set(name, value, secret, readOnly, preserveCase));

            if (isMultiCheckout)
            {
                var jobSettings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                jobSettings.Add(Agent.Sdk.WellKnownJobSettings.HasMultipleCheckouts, Boolean.TrueString);
                _ec.Setup(x => x.JobSettings).Returns(jobSettings);
            }

            var directoryManager = new Mock<Microsoft.VisualStudio.Services.Agent.Worker.Build.IBuildDirectoryManager>();
            directoryManager.Setup(x => x.GetRelativeRepositoryPath(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IExecutionContext>()))
                .Returns<string, string, IExecutionContext>((bd, path, context) => GetLastPathPart(path));

            hc.SetSingleton(directoryManager.Object);
        }

        private string GetLastPathPart(string path)
        {
            return path?.Substring(path.LastIndexOfAny(new char[] { '/', '\\' }) + 1);
        }

        private RepositoryResource _selfRepo;
        private RepositoryResource _otherRepo;
        private Mock<IExecutionContext> _ec;
        private ExpressionManager _expressionManager;
        private Variables _variables;
        private List<RepositoryResource> _repositories;
    }
}
