// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.DistributedTask.Pipelines;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.L1.Worker
{
    [Collection("Worker L1 Tests")]
    public class TimeoutLogFlushingL1Tests : L1TestBase
    {
        [Fact]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        public async Task TestTimeoutLogFlushingEnabled_JobCompletesSuccessfully()
        {
            try
            {
                // Arrange
                SetupL1();
                Environment.SetEnvironmentVariable("AZP_ENABLE_TIMEOUT_LOG_FLUSHING", "true");

                var message = LoadTemplateMessage();
                message.Steps.Clear();

                message.Steps.Add(CreateCheckoutTask("self"));

                // Act
                var results = await RunWorker(message);

                // Assert
                Assert.Equal(TaskResult.Succeeded, results.Result);
                Assert.Equal(100, results.ReturnCode);
            }
            finally
            {
                Environment.SetEnvironmentVariable("AZP_ENABLE_TIMEOUT_LOG_FLUSHING", null);
            }
        }

        [Fact]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        public async Task TestTimeoutLogFlushingNotSet_DefaultsToDisabled()
        {
            try
            {
                // Arrange
                SetupL1();
                Environment.SetEnvironmentVariable("AZP_ENABLE_TIMEOUT_LOG_FLUSHING", null);

                var message = LoadTemplateMessage();
                message.Steps.Clear();

                message.Steps.Add(CreateCheckoutTask("self"));

                // Act
                var results = await RunWorker(message);

                // Assert - When timeout log flushing is not set, job should succeed normally
                // This test verifies the default behavior when the environment variable is unset
                Assert.Equal(TaskResult.Succeeded, results.Result);
                Assert.False(results.TimedOut);
            }
            finally
            {
                Environment.SetEnvironmentVariable("AZP_ENABLE_TIMEOUT_LOG_FLUSHING", null);
            }
        }

        [Fact]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        public async Task TestTimeoutLogFlushingWithMultipleSteps_CompletesSuccessfully()
        {
            try
            {
                // Arrange
                SetupL1();
                Environment.SetEnvironmentVariable("AZP_ENABLE_TIMEOUT_LOG_FLUSHING", "true");

                var message = LoadTemplateMessage();
                message.Steps.Clear();

                message.Steps.Add(CreateCheckoutTask("self"));
                message.Steps.Add(CreateCheckoutTask("self"));
                message.Steps.Add(CreateCheckoutTask("self"));

                // Act
                var results = await RunWorker(message);

                // Assert
                Assert.Equal(TaskResult.Succeeded, results.Result);
                Assert.Equal(100, results.ReturnCode);

                // Verify all steps completed
                var steps = GetSteps();
                Assert.True(steps.Count >= 3, $"Expected at least 3 steps but got {steps.Count}");
                foreach (var step in steps)
                {
                    Assert.Equal(TaskResult.Succeeded, step.Result);
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("AZP_ENABLE_TIMEOUT_LOG_FLUSHING", null);
            }
        }

        [Fact]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        public async Task TestTimeoutLogFlushingEnvironmentVariableValues_HandlesVariousInputs()
        {
            var testCases = new[] { "true", "TRUE", "True", "1", "false", "FALSE", "False", "0", "" };

            // Setup once before all test cases
            SetupL1();

            foreach (var testValue in testCases)
            {
                try
                {
                    // Arrange
                    Environment.SetEnvironmentVariable("AZP_ENABLE_TIMEOUT_LOG_FLUSHING", testValue);

                    var message = LoadTemplateMessage();
                    message.Steps.Clear();

                    message.Steps.Add(CreateCheckoutTask("self"));

                    // Act
                    var results = await RunWorker(message);

                    // Assert
                    Assert.Equal(TaskResult.Succeeded, results.Result);
                    Assert.Equal(100, results.ReturnCode);
                }
                finally
                {
                    Environment.SetEnvironmentVariable("AZP_ENABLE_TIMEOUT_LOG_FLUSHING", null);
                }
            }
        }

        [Fact]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        public async Task TestTimeoutLogFlushingEnabled_JobTimesOutWithExpectedResult()
        {
            try
            {
                // Arrange
                SetupL1();
                Environment.SetEnvironmentVariable("AZP_ENABLE_TIMEOUT_LOG_FLUSHING", "true");

                // Set a very short job timeout (5 seconds) to force timeout
                JobTimeout = TimeSpan.FromSeconds(5);

                var message = LoadTemplateMessage();
                message.Steps.Clear();

                // Add a PowerShell task that sleeps longer than the timeout
                message.Steps.Add(CreatePowerShellTask("Start-Sleep -Seconds 30; Write-Host 'This should not execute'"));

                // Act
                var results = await RunWorker(message);

                // Assert - Job should timeout and have TimedOut = true
                Assert.True(results.TimedOut, "Job should have timed out");
            }
            finally
            {
                Environment.SetEnvironmentVariable("AZP_ENABLE_TIMEOUT_LOG_FLUSHING", null);
                // Reset JobTimeout to default
                JobTimeout = TimeSpan.FromSeconds(100);
            }
        }

        [Fact]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        public async Task TestTimeoutLogFlushingDisabled_JobTimesOutWithExpectedResult()
        {
            try
            {
                // Arrange
                SetupL1();
                Environment.SetEnvironmentVariable("AZP_ENABLE_TIMEOUT_LOG_FLUSHING", "false");

                // Set a very short job timeout (5 seconds) to force timeout
                JobTimeout = TimeSpan.FromSeconds(5);

                var message = LoadTemplateMessage();
                message.Steps.Clear();

                // Add a PowerShell task that sleeps longer than the timeout
                message.Steps.Add(CreatePowerShellTask("Start-Sleep -Seconds 30; Write-Host 'This should not execute'"));

                // Act
                var results = await RunWorker(message);

                // Assert - Job should timeout and have TimedOut = true
                Assert.True(results.TimedOut, "Job should have timed out");

            }
            finally
            {
                Environment.SetEnvironmentVariable("AZP_ENABLE_TIMEOUT_LOG_FLUSHING", null);
                // Reset JobTimeout to default
                JobTimeout = TimeSpan.FromSeconds(100);
            }
        }

        /// <summary>
        /// Creates a PowerShell task step for testing purposes
        /// </summary>
        protected static TaskStep CreatePowerShellTask(string script)
        {
            var step = new TaskStep
            {
                Reference = new TaskStepDefinitionReference
                {
                    Id = Guid.Parse("e213ff0f-5d5c-4791-802d-52ea3e7be1f1"),
                    Name = "PowerShell",
                    Version = "2.259.0"
                },
                Name = "PowerShell",
                DisplayName = "PowerShell Script",
                Id = Guid.NewGuid()
            };
            step.Inputs.Add("targetType", "inline");
            step.Inputs.Add("script", script);

            return step;
        }
    }
}