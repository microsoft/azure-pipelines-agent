// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using Agent.Sdk.Util;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.Handlers;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker.Handlers
{
    public sealed class HandlerEnvironmentL0
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker.Handlers")]
        public void GeneratedWritesCancelTaskEnvironmentTombstones()
        {
            using var hostContext = new TestHostContext(this);
            hostContext.SetSingleton<IWorkerCommandManager>(new Mock<IWorkerCommandManager>().Object);
            var environment = new TaskEnvironment();
            environment.Remove("GENERATED");
            var handler = new TestHandler
            {
                Environment = environment,
            };
            handler.Initialize(hostContext);

            handler.SetEnvironmentVariable("GENERATED", "value");

            Assert.Equal("value", environment["GENERATED"]);
            Assert.DoesNotContain("GENERATED", environment.RemovedEnvironmentVariables);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker.Handlers")]
        public void GeneratedWritesRetainLegacyDictionaryBehavior()
        {
            using var hostContext = new TestHostContext(this);
            hostContext.SetSingleton<IWorkerCommandManager>(new Mock<IWorkerCommandManager>().Object);
            var environment = new Dictionary<string, string>(VarUtil.EnvironmentVariableKeyComparer);
            var handler = new TestHandler
            {
                Environment = environment,
            };
            handler.Initialize(hostContext);

            handler.SetEnvironmentVariable("GENERATED", null);

            Assert.Same(environment, handler.Environment);
            Assert.Equal(string.Empty, environment["GENERATED"]);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker.Handlers")]
        public void PathPrependDoesNotRestoreRemovedWorkerPath()
        {
            const string prepend = "C:\\task-tool";
            string originalPath = Environment.GetEnvironmentVariable(Constants.PathVariable);
            try
            {
                Environment.SetEnvironmentVariable(Constants.PathVariable, "C:\\worker-tool");
                using var hostContext = new TestHostContext(this);
                hostContext.SetSingleton<IWorkerCommandManager>(new Mock<IWorkerCommandManager>().Object);
                var environment = new TaskEnvironment();
                environment.Remove(Constants.PathVariable);
                var executionContext = new Mock<IExecutionContext>();
                executionContext.SetupGet(x => x.PrependPath).Returns(new List<string> { prepend });
                var handler = new TestHandler
                {
                    Environment = environment,
                    ExecutionContext = executionContext.Object,
                    RuntimeVariables = new Variables(
                        hostContext,
                        new Dictionary<string, VariableValue>(),
                        out _),
                    StepHost = new Mock<IDefaultStepHost>().Object,
                };
                handler.Initialize(hostContext);

                handler.PrependPath();

                Assert.Equal(prepend, environment[Constants.PathVariable]);
                Assert.DoesNotContain("worker-tool", environment[Constants.PathVariable]);
                Assert.DoesNotContain(Constants.PathVariable, environment.RemovedEnvironmentVariables);
            }
            finally
            {
                Environment.SetEnvironmentVariable(Constants.PathVariable, originalPath);
            }
        }

        [Theory]
        [InlineData(false, "task-path")]
        [InlineData(true, "runtime-path")]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker.Handlers")]
        public void PathPrependUsesExplicitOrRuntimePathAboveRemoval(bool useRuntimePath, string expectedOriginalPath)
        {
            const string prepend = "prepend";
            using var hostContext = new TestHostContext(this);
            hostContext.SetSingleton<IWorkerCommandManager>(new Mock<IWorkerCommandManager>().Object);
            var explicitMappings = new Dictionary<string, string>(VarUtil.EnvironmentVariableKeyComparer);
            var runtimeMappings = new Dictionary<string, VariableValue>();
            if (useRuntimePath)
            {
                runtimeMappings[Constants.PathVariable] = expectedOriginalPath;
            }
            else
            {
                explicitMappings[Constants.PathVariable] = expectedOriginalPath;
            }

            var state = new TaskEnvironmentState();
            state.Remove(Constants.PathVariable);
            var environment = new TaskEnvironment(explicitMappings);
            environment.Reset(state.GetSnapshot());
            var executionContext = new Mock<IExecutionContext>();
            executionContext.SetupGet(x => x.PrependPath).Returns(new List<string> { prepend });
            var handler = new TestHandler
            {
                Environment = environment,
                ExecutionContext = executionContext.Object,
                RuntimeVariables = new Variables(hostContext, runtimeMappings, out _),
                StepHost = new Mock<IDefaultStepHost>().Object,
            };
            handler.Initialize(hostContext);

            handler.PrependPath();

            Assert.Equal(
                PathUtil.PrependPath(prepend, expectedOriginalPath),
                environment[Constants.PathVariable]);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker.Handlers")]
        [Trait("SkipOn", "linux")]
        [Trait("SkipOn", "darwin")]
        public void PsModulePathRemovalDoesNotFallBackToWorkerEnvironment()
        {
            const string variableName = "PSModulePath";
            string originalValue = Environment.GetEnvironmentVariable(variableName);
            try
            {
                string powershellCoreModules = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "PowerShell",
                    "Modules");
                Environment.SetEnvironmentVariable(variableName, powershellCoreModules);
                using var hostContext = new TestHostContext(this);
                hostContext.SetSingleton<IWorkerCommandManager>(new Mock<IWorkerCommandManager>().Object);
                var executionContext = new Mock<IExecutionContext>();
                executionContext.Setup(x => x.GetScopedEnvironment()).Returns(new LocalEnvironment());
                executionContext
                    .Setup(x => x.GetVariableValueOrDefault("DistributedTask.Agent.CheckPsModulesLocations"))
                    .Returns("true");
                var handler = new TestHandler
                {
                    ExecutionContext = executionContext.Object,
                    Inputs = new Dictionary<string, string>(),
                    Environment = new Dictionary<string, string>(VarUtil.EnvironmentVariableKeyComparer),
                };
                handler.Initialize(hostContext);

                Assert.True(handler.ContainsPowershellCoreLocations());

                var taskEnvironment = new TaskEnvironment();
                taskEnvironment.Remove(variableName);
                handler.Environment = taskEnvironment;

                Assert.False(handler.ContainsPowershellCoreLocations());
            }
            finally
            {
                Environment.SetEnvironmentVariable(variableName, originalValue);
            }
        }

        private sealed class TestHandler : Handler
        {
            public void SetEnvironmentVariable(string key, string value)
            {
                AddEnvironmentVariable(key, value);
            }

            public void PrependPath()
            {
                AddPrependPathToEnvironment();
            }

            public bool ContainsPowershellCoreLocations()
            {
                return PsModulePathContainsPowershellCoreLocations();
            }
        }
    }
}
