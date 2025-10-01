// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.DistributedTask.WebApi;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.L1.Worker
{
    [Collection("Worker L1 Tests")]
    public class ConditionsL1Tests : L1TestBase
    {
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
    }
}
