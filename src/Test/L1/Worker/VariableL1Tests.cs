// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.DistributedTask.WebApi;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.WebApi;

namespace Microsoft.VisualStudio.Services.Agent.Tests.L1.Worker
{
    [Collection("Worker L1 Tests")]
    public class VariableL1Tests : L1TestBase
    {
        [Theory]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task SetVariable_ReadVariable(bool writeToBlobstorageService)
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
                message.Variables.Add("agent.LogToBlobstorageService", writeToBlobstorageService.ToString());

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

        [Theory]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task UnsetVariable_RemovesFromLaterTasks(bool writeToBlobstorageService)
        {
            const string variableName = "NO_PROXY";
            string inheritedValue = Environment.GetEnvironmentVariable(variableName);
            try
            {
                // Removal preserves inherited values; isolate the fixture's process environment.
                Environment.SetEnvironmentVariable(variableName, null);
                SetupL1();
                var message = LoadTemplateMessage();
                message.Steps.Clear();
                message.Variables.Add("agent.LogToBlobstorageService", writeToBlobstorageService.ToString());

                bool windows = TestUtil.IsWindows();
                string Echo(string text) => windows ? $"echo {text}" : $"echo '{text}'";
                string checkPresent = windows
                    ? $"if not \"%{variableName}%\"==\"temporary-value\" exit /b 1"
                    : $"[ \"${variableName}\" = 'temporary-value' ] || exit 1";
                string checkAbsent = windows
                    ? $"powershell.exe -NoLogo -NoProfile -NonInteractive -Command \"if ([Environment]::GetEnvironmentVariables().Contains('{variableName}')) {{ exit 1 }}\"\nif errorlevel 1 exit /b 1"
                    : $"[ \"${{{variableName}+x}}\" != x ] || exit 1";
                string checkReset = windows
                    ? $"if not \"%{variableName}%\"==\"replacement-value\" exit /b 1"
                    : $"[ \"${variableName}\" = 'replacement-value' ] || exit 1";
                message.Steps.Add(CreateScriptTask(Echo($"##vso[task.setvariable variable={variableName}]temporary-value")));
                message.Steps.Add(CreateScriptTask(string.Join("\n",
                    checkPresent,
                    "echo VariablePresent",
                    Echo($"##vso[task.unsetvariable variable={variableName}]"),
                    checkPresent)));
                message.Steps.Add(CreateScriptTask(string.Join("\n",
                    checkAbsent,
                    "echo VariableAbsent",
                    Echo($"##vso[task.setvariable variable={variableName}]replacement-value"))));
                message.Steps.Add(CreateScriptTask(string.Join("\n", checkReset, "echo VariableReset")));

                var results = await RunWorker(message);

                AssertJobCompleted();
                Assert.False(results.TimedOut);
                var steps = GetSteps();
                Assert.True(
                    results.Result == TaskResult.Succeeded,
                    string.Join("\n", steps.Where(step => step.Log != null).SelectMany(GetTimelineLogLines)));
                Assert.Equal(6, steps.Count);
                Assert.All(steps, step => Assert.Equal(TaskResult.Succeeded, step.Result));
                // Match emitted output rather than debug logging of the script text.
                Assert.Contains(GetTimelineLogLines(steps[2]), line => line.Contains("Z VariablePresent"));
                Assert.Contains(GetTimelineLogLines(steps[3]), line => line.Contains("Z VariableAbsent"));
                Assert.Contains(GetTimelineLogLines(steps[4]), line => line.Contains("Z VariableReset"));
            }
            finally
            {
                Environment.SetEnvironmentVariable(variableName, inheritedValue);
                TearDown();
            }
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
            Assert.Equal(TaskResult.Failed, results.Result);

            var steps = GetSteps();
            Assert.Equal(4, steps.Count()); // Init, CmdLine, CmdLine, Finalize

            var failToSetStep = steps[1];
            Assert.Equal(TaskResult.Failed, failToSetStep.Result);

            var outputStep = steps[2];
            var log = GetTimelineLogLines(outputStep);
            Assert.True(log.Where(x => x.Contains("SystemVariableValue=build")).Count() > 0);
        }*/
    }
}
