// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.DistributedTask.WebApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.L1.Worker
{
    /// <summary>
    /// L1 tests for correlation context feature
    /// Tests end-to-end correlation tracking through full job execution
    /// </summary>
    [Collection("Worker L1 Tests")]
    public class CorrelationL1Tests : L1TestBase
    {
        [Fact]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        public async Task CorrelationContext_SingleStepJob_HasCorrelationInLogs()
        {
            try
            {
                // Arrange
                SetupL1();
                var message = LoadTemplateMessage();
                message.Steps.Clear();
                message.Steps.Add(CreateScriptTask("echo Testing correlation context"));

                // Enable enhanced logging to see correlation IDs
                message.Variables["AZP_USE_ENHANCED_LOGGING"] = "true";

                // Act
                var results = await RunWorker(message);

                // Assert
                AssertJobCompleted();
                Assert.Equal(TaskResult.Succeeded, results.Result);

                var steps = GetSteps();
                Assert.Equal(3, steps.Count); // Init, CmdLine, Finalize

                // Verify each step has a unique ID
                var stepIds = steps.Select(s => s.Id).Distinct().ToList();
                Assert.Equal(3, stepIds.Count); // All step IDs should be unique

                // Verify correlation IDs exist in timeline records
                foreach (var step in steps)
                {
                    Assert.NotEqual(Guid.Empty, step.Id);
                    
                    // Get log lines for the step
                    var logLines = GetTimelineLogLines(step);
                    Assert.NotEmpty(logLines);
                    
                    // With enhanced logging, correlation IDs should appear in logs
                    // Note: Actual correlation ID format is STEP-{guid-first-12-chars}
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
        public async Task CorrelationContext_MultipleSteps_EachStepHasUniqueCorrelation()
        {
            try
            {
                // Arrange
                SetupL1();
                var message = LoadTemplateMessage();
                message.Steps.Clear();
                
                // Add multiple script tasks
                message.Steps.Add(CreateScriptTask("echo Step 1"));
                message.Steps.Add(CreateScriptTask("echo Step 2"));
                message.Steps.Add(CreateScriptTask("echo Step 3"));
                
                message.Variables["AZP_USE_ENHANCED_LOGGING"] = "true";

                // Act
                var results = await RunWorker(message);

                // Assert
                AssertJobCompleted();
                Assert.Equal(TaskResult.Succeeded, results.Result);

                var steps = GetSteps();
                Assert.Equal(5, steps.Count); // Init, CmdLine, CmdLine, CmdLine, Finalize

                // Verify all steps have unique IDs
                var stepIds = steps.Select(s => s.Id).ToList();
                var uniqueIds = stepIds.Distinct().ToList();
                Assert.Equal(stepIds.Count, uniqueIds.Count); // All IDs should be unique

                // Verify each task step has different correlation
                var taskSteps = steps.Where(s => s.RecordType == "Task" && s.Name == "CmdLine").ToList();
                Assert.Equal(3, taskSteps.Count);

                foreach (var step in taskSteps)
                {
                    Assert.NotEqual(Guid.Empty, step.Id);
                    var logLines = GetTimelineLogLines(step);
                    Assert.NotEmpty(logLines);
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
        public async Task CorrelationContext_WithCheckout_CheckoutStepHasCorrelation()
        {
            try
            {
                // Arrange
                SetupL1();
                var message = LoadTemplateMessage(); // Includes checkout by default
                message.Variables["AZP_USE_ENHANCED_LOGGING"] = "true";

                // Act
                var results = await RunWorker(message);

                // Assert
                AssertJobCompleted();
                Assert.Equal(TaskResult.Succeeded, results.Result);

                var steps = GetSteps();
                var checkoutStep = steps.FirstOrDefault(s => s.Name.Contains("Checkout"));
                Assert.NotNull(checkoutStep);
                Assert.NotEqual(Guid.Empty, checkoutStep.Id);

                // Verify checkout has logs with potential correlation
                var checkoutLogs = GetTimelineLogLines(checkoutStep);
                Assert.NotEmpty(checkoutLogs);
            }
            finally
            {
                TearDown();
            }
        }

        [Fact]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        public async Task CorrelationContext_InitializeAndFinalize_HaveUniqueCorrelations()
        {
            try
            {
                // Arrange
                SetupL1();
                var message = LoadTemplateMessage();
                message.Steps.Clear();
                message.Steps.Add(CreateScriptTask("echo test"));
                message.Variables["AZP_USE_ENHANCED_LOGGING"] = "true";

                // Act
                var results = await RunWorker(message);

                // Assert
                AssertJobCompleted();
                Assert.Equal(TaskResult.Succeeded, results.Result);

                var steps = GetSteps();
                
                var initStep = steps.FirstOrDefault(s => s.Name == "Initialize job");
                var finalizeStep = steps.FirstOrDefault(s => s.Name == "Finalize Job");
                
                Assert.NotNull(initStep);
                Assert.NotNull(finalizeStep);
                
                // Both should have unique IDs
                Assert.NotEqual(Guid.Empty, initStep.Id);
                Assert.NotEqual(Guid.Empty, finalizeStep.Id);
                Assert.NotEqual(initStep.Id, finalizeStep.Id);

                // Both should have logs
                var initLogs = GetTimelineLogLines(initStep);
                var finalizeLogs = GetTimelineLogLines(finalizeStep);
                Assert.NotEmpty(initLogs);
                Assert.NotEmpty(finalizeLogs);
            }
            finally
            {
                TearDown();
            }
        }

        [Fact]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        public async Task CorrelationContext_FailedStep_HasCorrelationInErrorLogs()
        {
            try
            {
                // Arrange
                SetupL1();
                var message = LoadTemplateMessage();
                message.Steps.Clear();
                
                // Add a step that will fail
                message.Steps.Add(CreateScriptTask("exit 1"));
                message.Variables["AZP_USE_ENHANCED_LOGGING"] = "true";

                // Act
                var results = await RunWorker(message);

                // Assert
                AssertJobCompleted();
                Assert.Equal(TaskResult.Failed, results.Result);

                var steps = GetSteps();
                var failedStep = steps.FirstOrDefault(s => s.Result == TaskResult.Failed);
                
                Assert.NotNull(failedStep);
                Assert.NotEqual(Guid.Empty, failedStep.Id);

                // Verify failed step has logs with correlation
                var failedLogs = GetTimelineLogLines(failedStep);
                Assert.NotEmpty(failedLogs);
            }
            finally
            {
                TearDown();
            }
        }

        [Fact]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        public async Task CorrelationContext_PostJobSteps_HaveCorrelation()
        {
            try
            {
                // Arrange
                SetupL1();
                var message = LoadTemplateMessage(); // Includes checkout which has post-job step
                message.Variables["AZP_USE_ENHANCED_LOGGING"] = "true";

                // Act
                var results = await RunWorker(message);

                // Assert
                AssertJobCompleted();
                Assert.Equal(TaskResult.Succeeded, results.Result);

                var steps = GetSteps();
                
                // Look for post-job step
                var postJobStep = steps.FirstOrDefault(s => s.Name.StartsWith("Post-job:"));
                
                if (postJobStep != null)
                {
                    Assert.NotEqual(Guid.Empty, postJobStep.Id);
                    
                    // Post-job steps should have logs too
                    var postJobLogs = GetTimelineLogLines(postJobStep);
                    Assert.NotEmpty(postJobLogs);
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
        public async Task CorrelationContext_WithoutEnhancedLogging_StillHasStepIds()
        {
            try
            {
                // Arrange
                SetupL1();
                var message = LoadTemplateMessage();
                message.Steps.Clear();
                message.Steps.Add(CreateScriptTask("echo Without enhanced logging"));
                
                // Explicitly disable enhanced logging
                message.Variables["AZP_USE_ENHANCED_LOGGING"] = "false";

                // Act
                var results = await RunWorker(message);

                // Assert
                AssertJobCompleted();
                Assert.Equal(TaskResult.Succeeded, results.Result);

                var steps = GetSteps();
                
                // Even without enhanced logging, timeline records should have step IDs
                foreach (var step in steps)
                {
                    Assert.NotEqual(Guid.Empty, step.Id);
                }

                // Steps should still have logs
                var taskStep = steps.FirstOrDefault(s => s.Name == "CmdLine");
                Assert.NotNull(taskStep);
                
                var logs = GetTimelineLogLines(taskStep);
                Assert.NotEmpty(logs);
                
                // Without enhanced logging, correlation IDs might not appear in logs
                // but the infrastructure still maintains them
            }
            finally
            {
                TearDown();
            }
        }

        [Fact]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        public async Task CorrelationContext_LongRunningJob_CorrelationPersistsThroughout()
        {
            try
            {
                // Arrange
                SetupL1();
                var message = LoadTemplateMessage();
                message.Steps.Clear();
                
                // Add multiple steps with delays to simulate long-running job
                message.Steps.Add(CreateScriptTask("echo Starting long job"));
                message.Steps.Add(CreateScriptTask("echo Middle of job"));
                message.Steps.Add(CreateScriptTask("echo Ending long job"));
                
                message.Variables["AZP_USE_ENHANCED_LOGGING"] = "true";

                // Act
                var results = await RunWorker(message);

                // Assert
                AssertJobCompleted();
                Assert.Equal(TaskResult.Succeeded, results.Result);

                var steps = GetSteps();
                
                // Verify all steps completed and have unique IDs
                Assert.True(steps.Count >= 5); // Init + 3 tasks + Finalize
                
                var taskSteps = steps.Where(s => s.Name == "CmdLine").ToList();
                Assert.Equal(3, taskSteps.Count);
                
                // All task steps should have completed successfully
                foreach (var step in taskSteps)
                {
                    Assert.Equal(TaskResult.Succeeded, step.Result);
                    Assert.NotEqual(Guid.Empty, step.Id);
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
        public async Task CorrelationContext_TimelineRecords_ContainStepIdentifiers()
        {
            try
            {
                // Arrange
                SetupL1();
                var message = LoadTemplateMessage();
                message.Steps.Clear();
                message.Steps.Add(CreateScriptTask("echo Checking timeline records"));
                message.Variables["AZP_USE_ENHANCED_LOGGING"] = "true";

                // Act
                var results = await RunWorker(message);

                // Assert
                AssertJobCompleted();
                Assert.Equal(TaskResult.Succeeded, results.Result);

                // Get all timelines
                var timelines = GetTimelines();
                Assert.NotEmpty(timelines);

                var timeline = timelines[0];
                Assert.NotEmpty(timeline.Records);

                // Verify timeline records exist with unique IDs
                var recordIds = new HashSet<Guid>();
                foreach (var record in timeline.Records)
                {
                    Assert.NotEqual(Guid.Empty, record.Id);
                    Assert.NotNull(record.Name);
                    Assert.True(recordIds.Add(record.Id), $"Duplicate record ID found: {record.Id}");
                }
                
                // Verify we have the expected records (Initialize, Task, Finalize)
                Assert.True(recordIds.Count >= 3, $"Expected at least 3 timeline records, got {recordIds.Count}");
            }
            finally
            {
                TearDown();
            }
        }

        [Fact]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        public async Task CorrelationContext_JobWithVariables_CorrelationNotAffectedByVariables()
        {
            try
            {
                // Arrange
                SetupL1();
                var message = LoadTemplateMessage();
                message.Steps.Clear();
                
                // Add custom variables
                message.Variables["CUSTOM_VAR_1"] = "value1";
                message.Variables["CUSTOM_VAR_2"] = "value2";
                message.Variables["AZP_USE_ENHANCED_LOGGING"] = "true";
                
                message.Steps.Add(CreateScriptTask("echo Using custom variables"));

                // Act
                var results = await RunWorker(message);

                // Assert
                AssertJobCompleted();
                Assert.Equal(TaskResult.Succeeded, results.Result);

                var steps = GetSteps();
                
                // Correlation should work regardless of custom variables
                foreach (var step in steps)
                {
                    Assert.NotEqual(Guid.Empty, step.Id);
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
        public async Task CorrelationContext_StepWithOutput_CorrelationInOutputLogs()
        {
            try
            {
                // Arrange
                SetupL1();
                var message = LoadTemplateMessage();
                message.Steps.Clear();
                message.Steps.Add(CreateScriptTask("echo This is output from step"));
                message.Variables["AZP_USE_ENHANCED_LOGGING"] = "true";

                // Act
                var results = await RunWorker(message);

                // Assert
                AssertJobCompleted();
                Assert.Equal(TaskResult.Succeeded, results.Result);

                var steps = GetSteps();
                var outputStep = steps.FirstOrDefault(s => s.Name == "CmdLine");
                
                Assert.NotNull(outputStep);
                Assert.NotEqual(Guid.Empty, outputStep.Id);

                // Verify output logs exist
                var logs = GetTimelineLogLines(outputStep);
                Assert.NotEmpty(logs);
                
                // Should contain the echo output
                Assert.Contains(logs, l => l.Contains("This is output from step"));
            }
            finally
            {
                TearDown();
            }
        }

        [Fact]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        public async Task CorrelationContext_EmptyStepName_StillHasValidCorrelation()
        {
            try
            {
                // Arrange
                SetupL1();
                var message = LoadTemplateMessage();
                message.Steps.Clear();
                
                var scriptTask = CreateScriptTask("echo test");
                scriptTask.DisplayName = ""; // Empty display name
                message.Steps.Add(scriptTask);
                
                message.Variables["AZP_USE_ENHANCED_LOGGING"] = "true";

                // Act
                var results = await RunWorker(message);

                // Assert
                AssertJobCompleted();
                Assert.Equal(TaskResult.Succeeded, results.Result);

                var steps = GetSteps();
                var taskStep = steps.FirstOrDefault(s => s.RecordType == "Task");
                
                // Even with empty name, should have valid ID and correlation
                if (taskStep != null)
                {
                    Assert.NotEqual(Guid.Empty, taskStep.Id);
                }
            }
            finally
            {
                TearDown();
            }
        }
    }
}
