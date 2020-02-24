// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.DistributedTask.Pipelines;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.L1.Worker
{
    public class WorkerL1Tests : L1TestBase
    {
        [Fact]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        public async Task Test_Base()
        {
            // Arrange
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

        [Fact]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        public async Task NoCheckout()
        {
            // Arrange
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

        [Fact]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        public async Task Conditions_Failed()
        {
            // Arrange
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
    }
}
