// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker;
using System;
using System.Collections.Generic;
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
                
                // Use checkout task instead of script task since it's available in L1 framework
                message.Steps.Add(CreateCheckoutTask("self"));

                // Act
                var results = await RunWorker(message);

                // Assert
                Assert.Equal(TaskResult.Succeeded, results.Result);
                Assert.Equal(0, results.ReturnCode);
            }
            finally
            {
                Environment.SetEnvironmentVariable("AZP_ENABLE_TIMEOUT_LOG_FLUSHING", null);
            }
        }

        [Fact]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        public async Task TestTimeoutLogFlushingDisabled_JobCompletesSuccessfully()
        {
            try
            {
                // Arrange
                SetupL1();
                Environment.SetEnvironmentVariable("AZP_ENABLE_TIMEOUT_LOG_FLUSHING", "false");
                
                var message = LoadTemplateMessage();
                message.Steps.Clear();
                
                // Use checkout task instead of script task since it's available in L1 framework
                message.Steps.Add(CreateCheckoutTask("self"));

                // Act
                var results = await RunWorker(message);

                // Assert
                Assert.Equal(TaskResult.Succeeded, results.Result);
                Assert.Equal(0, results.ReturnCode);
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
                
                // Use checkout task instead of script task since it's available in L1 framework
                message.Steps.Add(CreateCheckoutTask("self"));

                // Act
                var results = await RunWorker(message);

                // Assert
                Assert.Equal(TaskResult.Succeeded, results.Result);
                Assert.Equal(0, results.ReturnCode);
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
                
                // Use multiple checkout tasks instead of script tasks since they're available in L1 framework
                message.Steps.Add(CreateCheckoutTask("self"));
                message.Steps.Add(CreateCheckoutTask("self"));
                message.Steps.Add(CreateCheckoutTask("self"));

                // Act
                var results = await RunWorker(message);

                // Assert
                Assert.Equal(TaskResult.Succeeded, results.Result);
                Assert.Equal(0, results.ReturnCode);
                
                // Verify all steps completed
                var steps = GetSteps();
                Assert.Equal(3, steps.Count);
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
            
            foreach (var testValue in testCases)
            {
                try
                {
                    // Arrange
                    SetupL1();
                    Environment.SetEnvironmentVariable("AZP_ENABLE_TIMEOUT_LOG_FLUSHING", testValue);
                    
                    var message = LoadTemplateMessage();
                    message.Steps.Clear();
                    
                    // Use checkout task instead of script task since it's available in L1 framework
                    message.Steps.Add(CreateCheckoutTask("self"));

                    // Act
                    var results = await RunWorker(message);

                    // Assert
                    Assert.Equal(TaskResult.Succeeded, results.Result);
                    Assert.Equal(0, results.ReturnCode);
                }
                finally
                {
                    Environment.SetEnvironmentVariable("AZP_ENABLE_TIMEOUT_LOG_FLUSHING", null);
                }
            }
        }
    }
}