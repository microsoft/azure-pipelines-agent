// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.DistributedTask.Pipelines;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.L1.Worker
{
    [Collection("Worker L1 Tests")]
    public class ContainerL1Tests : L1TestBase
    {
        [Theory]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task StepTarget_RestrictedMode(bool writeToBlobstorageService)
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
                message.Variables.Add("agent.LogToBlobstorageService", writeToBlobstorageService.ToString());

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
    }
}
