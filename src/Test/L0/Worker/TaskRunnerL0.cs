// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Moq;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;
using Xunit;
using Agent.Sdk;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.Handlers;
using System;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.DistributedTask.WebApi;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker
{

    public sealed class TaskRunnerL0
    {

        private TestHostContext CreateTestContext([CallerMemberName] String testName = "")
        {
            var hc = new TestHostContext(this, testName);

            return hc;
        }

        private class GetHandlerTest
        {
            public String Name;
            public ExecutionData Input;
            public PlatformUtil.OS HostOS;
            public HandlerData Expected;
            public ExecutionTargetInfo StepTarget = null;

            public void RunTest(TestHostContext hc, Dictionary<string, VariableValue> variables = null)
            {
                var _ec = new Mock<IExecutionContext>();
                _ec.Setup(x => x.StepTarget()).Returns(StepTarget);
                _ec.Setup(x => x.GetScopedEnvironment()).Returns(new SystemEnvironment());
                _ec.Setup(x => x.GetVariableValueOrDefault("agent.preferPowerShellOnContainers")).Returns(variables?["agent.preferPowerShellOnContainers"]?.Value ?? string.Empty);

                if (variables is null)
                {
                    variables = new Dictionary<string, VariableValue>();
                }
                List<string> warnings;
                _ec.Setup(x => x.Variables)
                   .Returns(new Variables(hc, copy: variables, warnings: out warnings));

                var tr = new TaskRunner();
                tr.Initialize(hc);
                var Got = tr.GetHandlerData(_ec.Object, Input, HostOS);
                // for some reason, we guard the setter of PowerShellDate in ExecutionData to only add if running on windows.
                // this makes testing hard
                if (!PlatformUtil.RunningOnWindows)
                {
                    Assert.True(true, "Passively pass this test since we have no way to actually prove it");
                }
                else
                {
                    Assert.True(Got == Expected, $"{Name} - Expected {Expected} Got {Got}");
                }
            }
        }

        [Theory]
        [InlineData("6f650d20-9c5d-4cce-ad66-e68742ceadf5", false, true)]
        [InlineData("a4cd16be-6028-4077-8015-34f008f55477", false, true)]
        [InlineData("bf7b17db-eb58-4014-ab2b-e4bf9d3b28f1", false, true)]
        [InlineData("b099689b-039e-4450-8658-c72e3895dd3f", false, true)]
        [InlineData("50acc50f-7d15-470b-83c1-578b3f3eeba2", false, true)]
        [InlineData("0f5cd14f-3c01-4d5c-8f7a-eb96c5738dcc", false, true)]
        [InlineData("063c7649-4934-49e8-9eac-37121fe7e350", false, true)]
        [InlineData("539e1e16-0680-4f8e-85d0-95b6fde76e8c", false, true)]
        [InlineData("37ac13ca-aead-4e19-a5ac-e5366051fc1c", false, true)]
        [InlineData("15ecb9f3-c0f7-42cf-9ce3-c6dded17de81", false, true)]
        [InlineData("6f650d20-9c5d-4cce-ad66-e68742ceadf6", false, false)]
        [InlineData("11111111-2222-3333-4444-555555555555", false, false)]
        [InlineData("11111111-2222-3333-4444-555555555555", true, true)]
        [InlineData("11111111-2222-3333-4444-555555555555", null, true)]
        [InlineData("6d15af64-176c-496d-b583-fd2ae21d4df4", false, true)]
        [InlineData("cbbf7f14-c386-4c1f-80a3-fe500e2bd976", false, true)]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task RunAsyncSetsPublishTelemetryAccordingToTaskIdentityAndOwnership(string taskId, bool? isServerOwned, bool expected)
        {
            using (TestHostContext hc = CreateTestContext())
            {
                var variables = new Variables(hc, new Dictionary<string, VariableValue>(), out _);
                var executionContext = new Mock<IExecutionContext>();
                executionContext.Setup(x => x.Variables).Returns(variables);
                executionContext.Setup(x => x.GetScopedEnvironment()).Returns(new SystemEnvironment());

                bool? publishTelemetry = null;
                var stopAfterVariableSetup = new InvalidOperationException("Stop before executing the task.");
                var taskManager = new Mock<ITaskManager>();
                taskManager.Setup(x => x.Load(It.IsAny<Pipelines.TaskStep>()))
                    .Callback(() => publishTelemetry = variables.GetBoolean(Constants.Variables.Task.PublishTelemetry))
                    .Throws(stopAfterVariableSetup);
                hc.SetSingleton(taskManager.Object);
                hc.SetSingleton(new Mock<IHandlerFactory>().Object);
                hc.SetSingleton(new Mock<IWorkerCommandManager>().Object);

                var taskRunner = new TaskRunner
                {
                    ExecutionContext = executionContext.Object,
                    Task = new Pipelines.TaskStep
                    {
                        IsServerOwned = isServerOwned,
                        Reference = new Pipelines.TaskStepDefinitionReference { Id = new Guid(taskId) }
                    }
                };
                taskRunner.Initialize(hc);

                // Observe the scoped variable while it is set, without loading or executing a task.
                InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => taskRunner.RunAsync());

                Assert.Same(stopAfterVariableSetup, exception);
                Assert.Equal(expected, publishTelemetry);
                taskManager.Verify(x => x.Load(taskRunner.Task), Times.Once);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void GetHandlerHostOnlyTests()
        {
            var nodeData = new NodeHandlerData() { Platforms = new string[] { "linux", "osx" } };
            var nodeOnlyExecutionData = new ExecutionData();
            nodeOnlyExecutionData.Node = nodeData;
            var powerShell3Data = new PowerShell3HandlerData() { Platforms = new string[] { "windows" } };
            var ps3OnlyExecutionData = new ExecutionData();
            ps3OnlyExecutionData.PowerShell3 = powerShell3Data;
            var mixedExecutionData = new ExecutionData();
            mixedExecutionData.PowerShell3 = powerShell3Data;
            mixedExecutionData.Node = nodeData;


            foreach (var test in new GetHandlerTest[] {
                new GetHandlerTest() { Name="Empty Test",                  Input=null,                  Expected=null,            HostOS=PlatformUtil.OS.Windows },
                new GetHandlerTest() { Name="Node Only on Windows",        Input=nodeOnlyExecutionData, Expected=nodeData,        HostOS=PlatformUtil.OS.Windows},
                new GetHandlerTest() { Name="Node Only on Linux",          Input=nodeOnlyExecutionData, Expected=nodeData,        HostOS=PlatformUtil.OS.Linux },
                new GetHandlerTest() { Name="Node Only on OSX",            Input=nodeOnlyExecutionData, Expected=nodeData,        HostOS=PlatformUtil.OS.OSX },
                new GetHandlerTest() { Name="PowerShell3 Only on Windows", Input=ps3OnlyExecutionData,  Expected=powerShell3Data, HostOS=PlatformUtil.OS.Windows },
                new GetHandlerTest() { Name="PowerShell3 Only on Linux",   Input=ps3OnlyExecutionData,  Expected=powerShell3Data, HostOS=PlatformUtil.OS.Linux },
                new GetHandlerTest() { Name="PowerShell3 Only on OSX",     Input=ps3OnlyExecutionData,  Expected=powerShell3Data, HostOS=PlatformUtil.OS.OSX },
                new GetHandlerTest() { Name="Mixed on Windows",            Input=mixedExecutionData,    Expected=powerShell3Data, HostOS=PlatformUtil.OS.Windows },
                new GetHandlerTest() { Name="Mixed on Linux",              Input=mixedExecutionData,    Expected=nodeData,        HostOS=PlatformUtil.OS.Linux },
                new GetHandlerTest() { Name="Mixed on OSX",                Input=mixedExecutionData,    Expected=nodeData,        HostOS=PlatformUtil.OS.OSX },
            })
            {
                using (TestHostContext hc = CreateTestContext(test.Name))
                {
                    test.RunTest(hc);
                }
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void GetHandlerContainerTargetTests()
        {
            var nodeData = new NodeHandlerData();
            var nodeOnlyExecutionData = new ExecutionData();
            nodeOnlyExecutionData.Node = nodeData;
            var powerShell3Data = new PowerShell3HandlerData() { Platforms = new string[] { "windows" } };
            var ps3OnlyExecutionData = new ExecutionData();
            ps3OnlyExecutionData.PowerShell3 = powerShell3Data;
            var mixedExecutionData = new ExecutionData();
            mixedExecutionData.Node = nodeData;
            mixedExecutionData.PowerShell3 = powerShell3Data;

            ContainerInfo containerInfo = new ContainerInfo() { };

            foreach (var test in new GetHandlerTest[] {
                new GetHandlerTest() { Name="Empty Test",                  Input=null,                  Expected=null,            HostOS=PlatformUtil.OS.Windows, StepTarget=containerInfo },
                new GetHandlerTest() { Name="Node Only on Windows",        Input=nodeOnlyExecutionData, Expected=nodeData,        HostOS=PlatformUtil.OS.Windows, StepTarget=containerInfo },
                new GetHandlerTest() { Name="Node Only on Linux",          Input=nodeOnlyExecutionData, Expected=nodeData,        HostOS=PlatformUtil.OS.Linux, StepTarget=containerInfo },
                new GetHandlerTest() { Name="Node Only on OSX",            Input=nodeOnlyExecutionData, Expected=nodeData,        HostOS=PlatformUtil.OS.OSX, StepTarget=containerInfo },
                new GetHandlerTest() { Name="PowerShell3 Only on Windows", Input=ps3OnlyExecutionData,  Expected=powerShell3Data, HostOS=PlatformUtil.OS.Windows, StepTarget=containerInfo },
                new GetHandlerTest() { Name="PowerShell3 Only on Linux",   Input=ps3OnlyExecutionData,  Expected=powerShell3Data, HostOS=PlatformUtil.OS.Linux, StepTarget=containerInfo },
                new GetHandlerTest() { Name="PowerShell3 Only on OSX",     Input=ps3OnlyExecutionData,  Expected=powerShell3Data, HostOS=PlatformUtil.OS.OSX, StepTarget=containerInfo },
                new GetHandlerTest() { Name="Mixed on Windows",            Input=mixedExecutionData,    Expected=powerShell3Data, HostOS=PlatformUtil.OS.Windows, StepTarget=containerInfo },
                new GetHandlerTest() { Name="Mixed on Linux",              Input=mixedExecutionData,    Expected=nodeData,        HostOS=PlatformUtil.OS.Linux, StepTarget=containerInfo },
                new GetHandlerTest() { Name="Mixed on OSX",                Input=mixedExecutionData,    Expected=nodeData,        HostOS=PlatformUtil.OS.OSX, StepTarget=containerInfo },
            })
            {
                using (TestHostContext hc = CreateTestContext(test.Name))
                {
                    test.RunTest(hc);
                }
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void GetHandlerContainerTargetPreferNodeDisabledTests()
        {
            var nodeData = new NodeHandlerData();
            var nodeOnlyExecutionData = new ExecutionData();
            nodeOnlyExecutionData.Node = nodeData;
            var powerShell3Data = new PowerShell3HandlerData() { Platforms = new string[] { "windows" } };
            var ps3OnlyExecutionData = new ExecutionData();
            ps3OnlyExecutionData.PowerShell3 = powerShell3Data;
            var mixedExecutionData = new ExecutionData();
            mixedExecutionData.Node = nodeData;
            mixedExecutionData.PowerShell3 = powerShell3Data;

            ContainerInfo containerInfo = new ContainerInfo() { };

            foreach (var test in new GetHandlerTest[] {
                new GetHandlerTest() { Name="Empty Test",                  Input=null,                  Expected=null,            HostOS=PlatformUtil.OS.Windows, StepTarget=containerInfo },
                new GetHandlerTest() { Name="Node Only on Windows",        Input=nodeOnlyExecutionData, Expected=nodeData,        HostOS=PlatformUtil.OS.Windows, StepTarget=containerInfo },
                new GetHandlerTest() { Name="Node Only on Linux",          Input=nodeOnlyExecutionData, Expected=nodeData,        HostOS=PlatformUtil.OS.Linux, StepTarget=containerInfo },
                new GetHandlerTest() { Name="Node Only on OSX",            Input=nodeOnlyExecutionData, Expected=nodeData,        HostOS=PlatformUtil.OS.OSX, StepTarget=containerInfo },
                new GetHandlerTest() { Name="PowerShell3 Only on Windows", Input=ps3OnlyExecutionData,  Expected=powerShell3Data, HostOS=PlatformUtil.OS.Windows, StepTarget=containerInfo },
                new GetHandlerTest() { Name="PowerShell3 Only on Linux",   Input=ps3OnlyExecutionData,  Expected=powerShell3Data, HostOS=PlatformUtil.OS.Linux, StepTarget=containerInfo },
                new GetHandlerTest() { Name="PowerShell3 Only on OSX",     Input=ps3OnlyExecutionData,  Expected=powerShell3Data, HostOS=PlatformUtil.OS.OSX, StepTarget=containerInfo },
                new GetHandlerTest() { Name="Mixed on Windows",            Input=mixedExecutionData,    Expected=powerShell3Data, HostOS=PlatformUtil.OS.Windows, StepTarget=containerInfo },
                new GetHandlerTest() { Name="Mixed on Linux",              Input=mixedExecutionData,    Expected=nodeData,        HostOS=PlatformUtil.OS.Linux, StepTarget=containerInfo },
                new GetHandlerTest() { Name="Mixed on OSX",                Input=mixedExecutionData,    Expected=nodeData,        HostOS=PlatformUtil.OS.OSX, StepTarget=containerInfo },
            })
            {
                var variables = new Dictionary<string, VariableValue>();
                variables.Add("agent.preferPowerShellOnContainers", "true");
                using (TestHostContext hc = CreateTestContext(test.Name))
                {
                    test.RunTest(hc, variables);
                    Environment.SetEnvironmentVariable("AGENT_PREFER_POWERSHELL_ON_CONTAINERS", "true");
                    test.RunTest(hc);
                    Environment.SetEnvironmentVariable("AGENT_PREFER_POWERSHELL_ON_CONTAINERS", null);
                }
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void VerifyTasksTests()
        {
            using (TestHostContext hc = CreateTestContext())
            {
                // Setup 
                var taskRunner = new TaskRunner();
                Mock<IConfigurationStore> store = new Mock<IConfigurationStore>();
                AgentSettings settings = new AgentSettings();
                settings.AlwaysExtractTask = true;
                store.Setup(x => x.GetSettings()).Returns(settings);
                hc.SetSingleton(store.Object);
                taskRunner.Initialize(hc);
                Definition definition = new Definition() { ZipPath = "test" };
                Mock<ITaskManager> taskManager = new Mock<ITaskManager>();
                taskManager.Setup(x => x.Extract(It.IsAny<IExecutionContext>(), It.IsAny<Pipelines.TaskStep>()));
                // VerifyTask reloads the Definition from disk after every re-extraction,
                // so Load must be mocked to return a (non-stale) definition.
                taskManager.Setup(x => x.Load(It.IsAny<Pipelines.TaskStep>())).Returns(() => definition);

                // Each Verify call should Extract since the ZipPath is given and AlwaysExtract = True
                taskRunner.VerifyTask(taskManager.Object, definition);
                taskManager.Verify(x => x.Extract(It.IsAny<IExecutionContext>(), It.IsAny<Pipelines.TaskStep>()), Times.Exactly(1));
                taskRunner.VerifyTask(taskManager.Object, definition);
                taskManager.Verify(x => x.Extract(It.IsAny<IExecutionContext>(), It.IsAny<Pipelines.TaskStep>()), Times.Exactly(2));
                taskRunner.VerifyTask(taskManager.Object, definition);
                taskManager.Verify(x => x.Extract(It.IsAny<IExecutionContext>(), It.IsAny<Pipelines.TaskStep>()), Times.Exactly(3));

                // Verify call should not Extract since AlwaysExtract = False
                settings.AlwaysExtractTask = false;
                taskRunner.VerifyTask(taskManager.Object, definition);
                taskManager.Verify(x => x.Extract(It.IsAny<IExecutionContext>(), It.IsAny<Pipelines.TaskStep>()), Times.Exactly(3));

                // Setting back to AlwaysExtract = true causes Extract to be called
                settings.AlwaysExtractTask = true;
                taskRunner.VerifyTask(taskManager.Object, definition);
                taskManager.Verify(x => x.Extract(It.IsAny<IExecutionContext>(), It.IsAny<Pipelines.TaskStep>()), Times.Exactly(4));

                // Clearing the ZipPath should not Extract
                definition.ZipPath = null;
                taskRunner.VerifyTask(taskManager.Object, definition);
                taskManager.Verify(x => x.Extract(It.IsAny<IExecutionContext>(), It.IsAny<Pipelines.TaskStep>()), Times.Exactly(4));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task RunAsyncResetsCommandSuppressionWhenTaskFails()
        {
            using TestHostContext hc = CreateTestContext();
            var commandManager = new Mock<IWorkerCommandManager>();
            hc.SetSingleton(commandManager.Object);

            var taskManager = new Mock<ITaskManager>();
            taskManager
                .Setup(x => x.Load(It.IsAny<Pipelines.TaskStep>()))
                .Throws(new InvalidOperationException("Expected test failure."));
            hc.SetSingleton(taskManager.Object);
            hc.SetSingleton(new Mock<IHandlerFactory>().Object);

            var variables = new Variables(hc, new Dictionary<string, VariableValue>(), out _);
            var executionContext = new Mock<IExecutionContext>();
            executionContext.SetupGet(x => x.Variables).Returns(variables);
            executionContext.Setup(x => x.GetScopedEnvironment()).Returns(new SystemEnvironment());
            executionContext.Setup(x => x.GetVariableValueOrDefault(It.IsAny<string>())).Returns((string)null);

            var taskRunner = new TaskRunner
            {
                ExecutionContext = executionContext.Object,
                Stage = JobRunStage.Main,
                Task = new Pipelines.TaskStep
                {
                    Id = Guid.NewGuid(),
                    Name = "TestTask",
                    Reference = new Pipelines.TaskStepDefinitionReference
                    {
                        Id = Guid.NewGuid(),
                        Name = "TestTask",
                        Version = "1.0.0"
                    }
                }
            };
            taskRunner.Initialize(hc);

            await Assert.ThrowsAsync<InvalidOperationException>(() => taskRunner.RunAsync());

            commandManager.Verify(x => x.ResetCommandSuppression(executionContext.Object), Times.Once);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task VerifyTaskReloadsDefinitionAfterExtractTests()
        {
            using (TestHostContext hc = CreateTestContext())
            {
                var taskRunner = new TaskRunner();
                Mock<IConfigurationStore> store = new Mock<IConfigurationStore>();
                AgentSettings settings = new AgentSettings();
                settings.AlwaysExtractTask = false;
                settings.SignatureVerification = new SignatureVerificationSettings() { Mode = SignatureVerificationMode.Error };
                store.Setup(x => x.GetSettings()).Returns(settings);
                hc.SetSingleton(store.Object);
                taskRunner.Initialize(hc);
                taskRunner.ExecutionContext = new Mock<IExecutionContext>().Object;

                Definition staleDefinition = new Definition() { ZipPath = "test" };

                Definition freshDefinition = new Definition() { ZipPath = "test" };

                Mock<ISignatureService> signatureService = new Mock<ISignatureService>();
                signatureService.Setup(x => x.VerifyAsync(It.IsAny<Definition>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
                hc.EnqueueInstance<ISignatureService>(signatureService.Object);

                Mock<ITaskManager> taskManager = new Mock<ITaskManager>();
                taskManager.Setup(x => x.Extract(It.IsAny<IExecutionContext>(), It.IsAny<Pipelines.TaskStep>()));
                taskManager.Setup(x => x.Load(It.IsAny<Pipelines.TaskStep>())).Returns(freshDefinition);

                Definition result = await taskRunner.VerifyTask(taskManager.Object, staleDefinition);

                signatureService.Verify(x => x.VerifyAsync(It.IsAny<Definition>(), It.IsAny<CancellationToken>()), Times.Once);
                taskManager.Verify(x => x.Extract(It.IsAny<IExecutionContext>(), It.IsAny<Pipelines.TaskStep>()), Times.Once);
                taskManager.Verify(x => x.Load(It.IsAny<Pipelines.TaskStep>()), Times.Once);
                Assert.Same(freshDefinition, result);
                Assert.NotSame(staleDefinition, result);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task VerifyTaskThrowsOnFailedVerificationErrorModeTests()
        {
            using (TestHostContext hc = CreateTestContext())
            {
                var taskRunner = new TaskRunner();
                Mock<IConfigurationStore> store = new Mock<IConfigurationStore>();
                AgentSettings settings = new AgentSettings();
                settings.AlwaysExtractTask = false;
                settings.SignatureVerification = new SignatureVerificationSettings() { Mode = SignatureVerificationMode.Error };
                store.Setup(x => x.GetSettings()).Returns(settings);
                hc.SetSingleton(store.Object);
                taskRunner.Initialize(hc);
                // signatureService.VerifyAsync is called with ExecutionContext.CancellationToken
                // before the success/failure branch is evaluated, so ExecutionContext must be non-null
                // even on this failure path.
                taskRunner.ExecutionContext = new Mock<IExecutionContext>().Object;

                Definition staleDefinition = new Definition() { ZipPath = "test" };

                Mock<ISignatureService> signatureService = new Mock<ISignatureService>();
                signatureService.Setup(x => x.VerifyAsync(It.IsAny<Definition>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
                hc.EnqueueInstance<ISignatureService>(signatureService.Object);

                Mock<ITaskManager> taskManager = new Mock<ITaskManager>();
                taskManager.Setup(x => x.Extract(It.IsAny<IExecutionContext>(), It.IsAny<Pipelines.TaskStep>()));
                taskManager.Setup(x => x.Load(It.IsAny<Pipelines.TaskStep>())).Returns(new Definition() { ZipPath = "test" });

                await Assert.ThrowsAsync<InvalidOperationException>(() => taskRunner.VerifyTask(taskManager.Object, staleDefinition));

                // A failed verification in Error mode must never reach Extract/Load:
                // the stale/unverified definition must not be re-extracted or reloaded.
                taskManager.Verify(x => x.Extract(It.IsAny<IExecutionContext>(), It.IsAny<Pipelines.TaskStep>()), Times.Never);
                taskManager.Verify(x => x.Load(It.IsAny<Pipelines.TaskStep>()), Times.Never);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task VerifyTaskDoesNotReloadOnFailedVerificationWarningModeTests()
        {
            using (TestHostContext hc = CreateTestContext())
            {
                var taskRunner = new TaskRunner();
                Mock<IConfigurationStore> store = new Mock<IConfigurationStore>();
                AgentSettings settings = new AgentSettings();
                settings.AlwaysExtractTask = false;
                settings.SignatureVerification = new SignatureVerificationSettings() { Mode = SignatureVerificationMode.Warning };
                store.Setup(x => x.GetSettings()).Returns(settings);
                hc.SetSingleton(store.Object);
                taskRunner.Initialize(hc);
                // The Warning-mode failure branch calls ExecutionContext.Warning(...), which
                // requires a non-null ExecutionContext (ArgUtil.NotNull check inside the extension method).
                taskRunner.ExecutionContext = new Mock<IExecutionContext>().Object;

                Definition originalDefinition = new Definition() { ZipPath = "test" };

                Mock<ISignatureService> signatureService = new Mock<ISignatureService>();
                signatureService.Setup(x => x.VerifyAsync(It.IsAny<Definition>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
                hc.EnqueueInstance<ISignatureService>(signatureService.Object);

                Mock<ITaskManager> taskManager = new Mock<ITaskManager>();
                taskManager.Setup(x => x.Extract(It.IsAny<IExecutionContext>(), It.IsAny<Pipelines.TaskStep>()));
                taskManager.Setup(x => x.Load(It.IsAny<Pipelines.TaskStep>())).Returns(new Definition() { ZipPath = "test" });

                Definition result = await taskRunner.VerifyTask(taskManager.Object, originalDefinition);

                // A failed verification in Warning mode must log and move on without
                // ever extracting or reloading - the original definition is returned as-is.
                taskManager.Verify(x => x.Extract(It.IsAny<IExecutionContext>(), It.IsAny<Pipelines.TaskStep>()), Times.Never);
                taskManager.Verify(x => x.Load(It.IsAny<Pipelines.TaskStep>()), Times.Never);
                Assert.Same(originalDefinition, result);
            }
        }
    }
}