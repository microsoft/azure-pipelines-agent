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
    public class CheckoutL1Tests : L1TestBase
    {
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
    }
}
