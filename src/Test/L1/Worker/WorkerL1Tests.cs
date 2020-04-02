// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.DistributedTask.Pipelines;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.WebApi;

namespace Microsoft.VisualStudio.Services.Agent.Tests.L1.Worker
{
    public class WorkerL1Tests : L1TestBase
    {
        [Fact]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        public async Task Test_Base()
        {
            try
            {
                // Arrange
                SetupL1();
                var message = LoadTemplateMessage();

                // Act
                var results = await RunWorker(message);

                // Assert
                AssertJobCompleted();
                Assert.Equal(TaskResult.Succeeded, results.Result);

                var steps = GetSteps();
                var expectedSteps = new[] { "Initialize job", "Checkout MyFirstProject@master to s", "CmdLine", "Post-job: Checkout MyFirstProject@master to s", "Finalize Job" };
                Assert.Equal(5, steps.Count()); // Init, Checkout, CmdLine, Post, Finalize
                for (var idx = 0; idx < steps.Count; idx++)
                {
                    Assert.Equal(expectedSteps[idx], steps[idx].Name);
                }
            }
            finally
            {
                TearDown();
            }
        }

        [Fact]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        public async Task NoCheckout()
        {
            try
            {
                // Arrange
                SetupL1();
                var message = LoadTemplateMessage();
                // Remove checkout
                for (var i = message.Steps.Count - 1; i >= 0; i--)
                {
                    var step = message.Steps[i];
                    if (step is TaskStep && ((TaskStep)step).Reference.Name == "Checkout")
                    {
                        message.Steps.RemoveAt(i);
                    }
                }

                // Act
                var results = await RunWorker(message);

                // Assert
                AssertJobCompleted();
                Assert.Equal(TaskResult.Succeeded, results.Result);

                var steps = GetSteps();
                Assert.Equal(3, steps.Count()); // Init, CmdLine, Finalize
                Assert.Equal(0, steps.Where(x => x.Name == "Checkout").Count());
            }
            finally
            {
                TearDown();
            }
        }

        [Fact]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        public async Task SetVariable_ReadVariable()
        {
            try
            {
                // Arrange
                SetupL1();
                var message = LoadTemplateMessage();
                // Remove all tasks
                message.Steps.Clear();
                // Add variable setting tasks
                message.Steps.Add(CreateScriptTask("echo \"##vso[task.setvariable variable=testVar]b\""));
                message.Steps.Add(CreateScriptTask("echo TestVar=$(testVar)"));
                message.Variables.Add("testVar", new Pipelines.VariableValue("a", false, false));

                // Act
                var results = await RunWorker(message);

                // Assert
                AssertJobCompleted();
                Assert.Equal(TaskResult.Succeeded, results.Result);

                var steps = GetSteps();
                Assert.Equal(4, steps.Count()); // Init, CmdLine, CmdLine, Finalize
                var outputStep = steps[2];
                var log = GetTimelineLogLines(outputStep);

                Assert.True(log.Where(x => x.Contains("TestVar=b")).Count() > 0);
            }
            finally
            {
                TearDown();
            }
        }

        [Fact]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        public async Task Conditions_Failed()
        {
            try
            {
                // Arrange
                SetupL1();
                var message = LoadTemplateMessage();
                // Remove all tasks
                message.Steps.Clear();
                // Add a normal step and one that only runs on failure
                message.Steps.Add(CreateScriptTask("echo This will run"));
                var failStep = CreateScriptTask("echo This shouldn't...");
                failStep.Condition = "failed()";
                message.Steps.Add(failStep);

                // Act
                var results = await RunWorker(message);

                // Assert
                AssertJobCompleted();
                Assert.Equal(TaskResult.Succeeded, results.Result);

                var steps = GetSteps();
                Assert.Equal(4, steps.Count()); // Init, CmdLine, CmdLine, Finalize
                var faiLStep = steps[2];
                Assert.Equal(TaskResult.Skipped, faiLStep.Result);
            }
            finally
            {
                TearDown();
            }
        }

        [Fact]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        public async Task StepTarget_RestrictedMode()
        {
            try
            {
                // Arrange
                SetupL1();
                var message = LoadTemplateMessage();
                // Remove all tasks
                message.Steps.Clear();
                var tagStep = CreateScriptTask("echo \"##vso[build.addbuildtag]sometag\"");
                tagStep.Target = new StepTarget
                {
                    Commands = "restricted"
                };
                message.Steps.Add(tagStep);

                // Act
                var results = await RunWorker(message);

                // Assert
                AssertJobCompleted();
                Assert.Equal(TaskResult.Succeeded, results.Result);

                var steps = GetSteps();
                Assert.Equal(3, steps.Count()); // Init, CmdLine, Finalize
                var log = GetTimelineLogLines(steps[1]);
                Assert.Equal(1, log.Where(x => x.Contains("##vso[build.addbuildtag] is not allowed in this step due to policy restrictions.")).Count());
                Assert.Equal(0, GetMockedService<FakeBuildServer>().BuildTags.Count);
            }
            finally
            {
                TearDown();
            }
        }

        [Fact]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        // TODO: When NuGet works cross-platform, remove these traits. Also, package NuGet with the Agent.
        [Trait("SkipOn", "darwin")]
        [Trait("SkipOn", "linux")]
        public async Task SignatureEnforcementMode_PassesWhenAllTasksAreSigned()
        {
            try
            {
                // Arrange
                SetupL1();
                FakeConfigurationStore fakeConfigurationStore = GetMockedService<FakeConfigurationStore>();
                AgentSettings settings = fakeConfigurationStore.GetSettings();
                settings.Fingerprint = _fingerprint;
                fakeConfigurationStore.UpdateSettings(settings);

                var message = LoadTemplateMessage();
                message.Steps.Clear();
                message.Steps.Add(GetSignedTask());

                // Act
                var results = await RunWorker(message);

                // Assert
                FakeJobServer fakeJobServer = GetMockedService<FakeJobServer>();
                Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(fakeJobServer.Timelines));

                AssertJobCompleted();
                Assert.Equal(TaskResult.Succeeded, results.Result);
            }
            finally
            {
                TearDown();
            }
        }

        [Fact]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        // TODO: When NuGet works cross-platform, remove these traits. Also, package NuGet with the Agent.
        [Trait("SkipOn", "darwin")]
        [Trait("SkipOn", "linux")]
        public async Task SignatureEnforcementMode_FailsWhenTasksArentSigned()
        {
            try
            {
                // Arrange
                SetupL1();
                FakeConfigurationStore fakeConfigurationStore = GetMockedService<FakeConfigurationStore>();
                AgentSettings settings = fakeConfigurationStore.GetSettings();
                settings.Fingerprint = _fingerprint;
                fakeConfigurationStore.UpdateSettings(settings);
                var message = LoadTemplateMessage();

                // Act
                var results = await RunWorker(message);

                // Assert
                AssertJobCompleted();
                Assert.Equal(TaskResult.Failed, results.Result);
            }
            finally
            {
                TearDown();
            }
        }

        private static TaskStep GetSignedTask()
        {
            var step = new TaskStep
            {
                Reference = new TaskStepDefinitionReference
                {
                    Id = Guid.Parse("5515f72c-5faa-4121-8a46-8f42a8f42132"),
                    Name = "servicetree-link-build-task-signed",
                    Version = "1.52.1"
                },
                Name = "servicetree-link-build-task-signed",
                DisplayName = "ServiceTree Integration - SIGNED",
                Id = Guid.NewGuid()
            };

            // These inputs let the task itself succeed.
            step.Inputs.Add("Service", "23ddace0-0682-541f-bfa9-6cbc76d9c051");
            step.Inputs.Add("ServiceTreeLinkNotRequiredIds", "2"); // Set to system.definitionId
            step.Inputs.Add("ServiceTreeGateway", "Foo");

            return step;
        }

        private void TearDown()
        {
            this._l1HostContext?.Dispose();
        }

        // Enable this test when read only variable enforcement is added
        /*[Fact]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        public async Task Readonly_Variables()
        {
            // Arrange
            var message = LoadTemplateMessage();
            // Remove all tasks
            message.Steps.Clear();
            // Add a normal step and one that only runs on failure
            message.Steps.Add(CreateScriptTask("echo ##vso[task.setvariable variable=system]someothervalue"));
            var alwayStep = CreateScriptTask("echo SystemVariableValue=$(system)");
            alwayStep.Condition = "always()";
            message.Steps.Add(alwayStep);

            // Act
            var results = await RunWorker(message);

            // Assert
            AssertJobCompleted();
            Assert.Equal(TaskResult.Succeeded, results.Result);

            var steps = GetSteps();
            Assert.Equal(4, steps.Count()); // Init, CmdLine, CmdLine, Finalize

            var failToSetStep = steps[1];
            Assert.Equal(TaskResult.Failed, failToSetStep.Result);

            var outputStep = steps[2];
            var log = GetTimelineLogLines(outputStep);
            Assert.True(log.Where(x => x.Contains("SystemVariableValue=build")).Count() == 1);
        }*/

        private static string _fingerprint = "3F9001EA83C560D712C24CF213C3D312CB3BFF51EE89435D3430BD06B5D0EECE";
    }
}
