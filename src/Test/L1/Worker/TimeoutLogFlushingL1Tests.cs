// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.DistributedTask.WebApi;
using System;
using System.Runtime.InteropServices;
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

                message.Steps.Add(CreateNode20ScriptTask("echo Testing timeout log flushing functionality"));

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

                message.Steps.Add(CreateNode20ScriptTask("echo Testing default timeout log flushing behavior"));

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
        public async Task TestTimeoutLogFlushingWithSingleStep_CompletesSuccessfully()
        {
            try
            {
                // Arrange
                SetupL1();
                Environment.SetEnvironmentVariable("AZP_ENABLE_TIMEOUT_LOG_FLUSHING", "true");

                var message = LoadTemplateMessage();
                message.Steps.Clear();

                // Use cross-platform script task (works on Windows, macOS, and Linux)
                message.Steps.Add(CreateNode20ScriptTask("echo Testing timeout log flushing with single step"));

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

                    message.Steps.Add(CreateNode20ScriptTask($"echo \"Testing with env value: {testValue}\""));

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
        [Trait("SkipOn", "darwin")]
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

                // Add a script task that runs longer than the timeout
                // Use reliable commands that will definitely take more than 5 seconds
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    message.Steps.Add(CreateNode20ScriptTask("powershell -Command \"Start-Sleep -Seconds 10\""));
                }
                else
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                        message.Steps.Add(CreateNode20ScriptTask("/bin/bash -c 'sleep 10'"));
                    }
                    else
                    {
                        message.Steps.Add(CreateNode20ScriptTask("/bin/sleep 10"));
                    }
                }

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
        [Trait("SkipOn", "darwin")]
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

                // Add a script task that runs longer than the timeout (sleep for 10 seconds, timeout is 5 seconds)
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    message.Steps.Add(CreateNode20ScriptTask("powershell -Command \"Start-Sleep -Seconds 10\""));
                }
                else
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                        message.Steps.Add(CreateNode20ScriptTask("/bin/bash -c 'sleep 10'"));
                    }
                    else
                    {
                        message.Steps.Add(CreateNode20ScriptTask("/bin/sleep 10"));
                    }
                }

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
    }
}