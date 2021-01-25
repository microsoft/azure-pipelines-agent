// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.DistributedTask.WebApi;
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
