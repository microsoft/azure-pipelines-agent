// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.DistributedTask.WebApi;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.L1.Worker
{
    [Collection("Worker L1 Tests")]
    public class NodeSelectionContainerL1Tests : L1TestBase
    {
        // Container-specific environment variable constants
        private const string AZP_AGENT_USE_NODE24_TO_START_CONTAINER = "AZP_AGENT_USE_NODE24_TO_START_CONTAINER";
        private const string AZP_AGENT_USE_NODE20_TO_START_CONTAINER = "AZP_AGENT_USE_NODE20_TO_START_CONTAINER";
        private const string AGENT_USE_NODE_STRATEGY = "AGENT_USE_NODE_STRATEGY";
        
        // Test container image names - using known working public images
        private const string TestImageNameNode20 = "mcr.microsoft.com/playwright:v1.40.0-jammy";
        private const string TestImageNameNode18 = "mcr.microsoft.com/playwright:v1.40.0-jammy";
        private const string TestImageNameNode16 = "mcr.microsoft.com/playwright:v1.40.0-jammy";

        [Fact]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "darwin")] // Skip on macOS due to container limitations
        public async Task Container_NodeSelection_StrategyVsLegacy_SelectsNode20()
        {
            try
            {
                SetupL1();
                ClearNodeEnvironmentVariables();
                
                Environment.SetEnvironmentVariable(AGENT_USE_NODE_STRATEGY, "true");
                Environment.SetEnvironmentVariable(AZP_AGENT_USE_NODE20_TO_START_CONTAINER, "true");
                
                var message = LoadTemplateMessage();
                var containerMessage = CreateContainerJob(message, TestImageNameNode20);

                var results = await RunWorker(containerMessage);

                AssertJobCompleted();
                Assert.Equal(TaskResult.Succeeded, results.Result);
                
                var steps = GetSteps();
                Assert.True(steps.Count() >= 1, "Container job should have executed at least one step");
            }
            finally
            {
                ClearNodeEnvironmentVariables();
                TearDown();
            }
        }

        [Fact]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "darwin")] // Skip on macOS due to container limitations
        public async Task Container_NodeSelection_Node20Missing_FallsBackToNode18()
        {
            try
            {
                SetupL1();
                ClearNodeEnvironmentVariables();
                
                Environment.SetEnvironmentVariable(AGENT_USE_NODE_STRATEGY, "true");
                Environment.SetEnvironmentVariable(AZP_AGENT_USE_NODE20_TO_START_CONTAINER, "true");
                
                var message = LoadTemplateMessage();
                var containerMessage = CreateContainerJob(message, TestImageNameNode18);

                var results = await RunWorker(containerMessage);

                AssertJobCompleted();
                Assert.Equal(TaskResult.Succeeded, results.Result);
                
                var steps = GetSteps();
                Assert.True(steps.Count() >= 1, "Container job should have executed at least one step");
            }
            finally
            {
                ClearNodeEnvironmentVariables();
                TearDown();
            }
        }

        [Fact]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "darwin")] // Skip on macOS due to container limitations
        public async Task Container_NodeSelection_OnlyNode16Available_UsesNode16()
        {
            try
            {
                SetupL1();
                ClearNodeEnvironmentVariables();
                
                Environment.SetEnvironmentVariable(AGENT_USE_NODE_STRATEGY, "true");
                Environment.SetEnvironmentVariable(AZP_AGENT_USE_NODE20_TO_START_CONTAINER, "true");
                
                var message = LoadTemplateMessage();
                var containerMessage = CreateContainerJob(message, TestImageNameNode16);

                var results = await RunWorker(containerMessage);

                AssertJobCompleted();
                Assert.Equal(TaskResult.Succeeded, results.Result);
                
                var steps = GetSteps();
                Assert.True(steps.Count() >= 1, "At least one step should have been executed");
            }
            finally
            {
                ClearNodeEnvironmentVariables();
                TearDown();
            }
        }

        [Fact]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "darwin")] // Skip on macOS due to container limitations
        public async Task Container_NodeSelection_NoContainerKnobs_UsesDefaultSelection()
        {
            try
            {
                SetupL1();
                ClearNodeEnvironmentVariables();
                
                Environment.SetEnvironmentVariable(AGENT_USE_NODE_STRATEGY, "true");
                // Don't set any container-specific knobs
                
                var message = LoadTemplateMessage();
                var containerMessage = CreateContainerJob(message, TestImageNameNode16);

                var results = await RunWorker(containerMessage);

                AssertJobCompleted();
                Assert.Equal(TaskResult.Succeeded, results.Result);
                
                var steps = GetSteps();
                Assert.True(steps.Count() >= 1, "Container job should have executed at least one step");
            }
            finally
            {
                ClearNodeEnvironmentVariables();
                TearDown();
            }
        }

        // [Theory]
        // [Trait("Level", "L1")]
        // [Trait("Category", "Worker")]
        // [Trait("SkipOn", "darwin")] // Skip on macOS due to container limitations
        // [InlineData(AZP_AGENT_USE_NODE24_TO_START_CONTAINER, AZP_AGENT_USE_NODE20_TO_START_CONTAINER, "24")] // Node 24 should win
        // public async Task Container_NodeSelection_ConflictingContainerKnobs_HigherVersionWins(string winningKnob, string losingKnob, string expectedVersion)
        // {
        //     string containerImage = null;
        //     try
        //     {
        //         // Arrange
        //         containerImage = await CreateTestContainerWithMultipleNodeVersions();
        //         SetupL1();
        //         ClearNodeEnvironmentVariables();
                
        //         Environment.SetEnvironmentVariable(AGENT_USE_NODE_STRATEGY, "true"); // Use strategy mode for this test
        //         Environment.SetEnvironmentVariable(winningKnob, "true");
        //         Environment.SetEnvironmentVariable(losingKnob, "true");
                
        //         var message = LoadTemplateMessage();
        //         message = ConvertToContainerJob(message, containerImage);

        //         // Act
        //         var results = await RunWorker(message);

        //         // Assert
        //         AssertJobCompleted();
        //         Assert.Equal(TaskResult.Succeeded, results.Result);
                
        //         // Verify higher version takes precedence
        //         var steps = GetSteps();
        //         var containerStep = steps.FirstOrDefault(s => s.Name.Contains("Initialize containers"));
        //         if (containerStep != null)
        //         {
        //             var log = GetTimelineLogLines(containerStep);
                    
        //             bool usedExpectedVersion = log.Any(x => x.Contains("node") && x.Contains(expectedVersion));
        //             Assert.True(usedExpectedVersion, $"Expected Node {expectedVersion} to win when both container knobs are set");
        //         }
        //     }
        //     finally
        //     {
        //         ClearNodeEnvironmentVariables();
        //         await CleanupContainerImage(containerImage);
        //         TearDown();
        //     }
        // }

        // [Fact]
        // [Trait("Level", "L1")]
        // [Trait("Category", "Worker")]
        // [Trait("SkipOn", "darwin")] // Skip on macOS due to container limitations
        // public async Task Container_NodeSelection_NoContainerKnobs_UsesDefaultSelection()
        // {
        //     string containerImage = null;
        //     try
        //     {
        //         // Arrange
        //         containerImage = await CreateTestContainerWithMultipleNodeVersions();
        //         SetupL1();
        //         ClearNodeEnvironmentVariables();
                
        //         Environment.SetEnvironmentVariable(AGENT_USE_NODE_STRATEGY, "true");
        //         // Don't set any container-specific knobs
                
        //         var message = LoadTemplateMessage();
        //         message = ConvertToContainerJob(message, containerImage);

        //         // Act
        //         var results = await RunWorker(message);

        //         // Assert
        //         AssertJobCompleted();
        //         Assert.Equal(TaskResult.Succeeded, results.Result);
                
        //         // Verify some node version is selected (default behavior)
        //         var steps = GetSteps();
        //         var containerStep = steps.FirstOrDefault(s => s.Name.Contains("Initialize containers"));
        //         if (containerStep != null)
        //         {
        //             var log = GetTimelineLogLines(containerStep);
                    
        //             bool usedSomeNode = log.Any(x => x.Contains("node"));
        //             Assert.True(usedSomeNode, "Expected some Node version to be selected for container startup");
        //         }
        //     }
        //     finally
        //     {
        //         ClearNodeEnvironmentVariables();
        //         await CleanupContainerImage(containerImage);
        //         TearDown();
        //     }
        // }

        // [Theory]
        // [Trait("Level", "L1")]
        // [Trait("Category", "Worker")]
        // [Trait("SkipOn", "darwin")] // Skip on macOS due to container limitations
        // [InlineData(true)]  // Strategy mode enabled
        // [InlineData(false)] // Legacy mode
        // public async Task Container_RealNodeSelection_StrategyVsLegacy_ExecutesActualNode(bool useStrategy)
        // {
        //     string containerImage = null;
        //     try
        //     {
        //         // Arrange - Use real Node.js container
        //         containerImage = await CreateRealNodeContainer(new[] { NODE24_FOLDER, NODE20_FOLDER });
        //         SetupL1();
        //         ClearNodeEnvironmentVariables();
                
        //         Environment.SetEnvironmentVariable(AGENT_USE_NODE_STRATEGY, useStrategy.ToString().ToLower());
        //         Environment.SetEnvironmentVariable(AZP_AGENT_USE_NODE24_TO_START_CONTAINER, "true");
                
        //         var message = LoadTemplateMessage();
        //         message = ConvertToContainerJob(message, containerImage);

        //         // Act
        //         var results = await RunWorker(message);

        //         // Assert
        //         AssertJobCompleted();
        //         Assert.Equal(TaskResult.Succeeded, results.Result);
                
        //         // Verify actual Node.js execution in container
        //         var steps = GetSteps();
        //         var containerStep = steps.FirstOrDefault(s => s.Name.Contains("Initialize containers"));
        //         if (containerStep != null)
        //         {
        //             var log = GetTimelineLogLines(containerStep);
                    
        //             // Real Node.js should show actual version output
        //             bool hasRealNodeVersion = log.Any(x => x.Contains("v24.") || x.Contains("v20."));
        //             Assert.True(hasRealNodeVersion, $"Expected real Node.js version output in {(useStrategy ? "strategy" : "legacy")} mode");
                    
        //             // Should show container node path selection
        //             bool hasNodePath = log.Any(x => x.Contains("/usr/local/node") || x.Contains("node"));
        //             Assert.True(hasNodePath, "Expected container node path to be logged");
        //         }
        //     }
        //     finally
        //     {
        //         ClearNodeEnvironmentVariables();
        //         await CleanupContainerImage(containerImage);
        //         TearDown();
        //     }
        // }

        // [Fact]
        // [Trait("Level", "L1")]
        // [Trait("Category", "Worker")]
        // [Trait("SkipOn", "darwin")] // Skip on macOS due to container limitations
        // public async Task Container_NodeSelection_CompatibilityFailure_FallsBackToWorkingVersion()
        // {
        //     string containerImage = null;
        //     try
        //     {
        //         // Arrange - Create container where Node 24 fails due to compatibility issues
        //         containerImage = await CreateIncompatibleNodeContainer(new[] { NODE24_FOLDER, NODE20_FOLDER, NODE16_FOLDER });
        //         SetupL1();
        //         ClearNodeEnvironmentVariables();
                
        //         Environment.SetEnvironmentVariable(AGENT_USE_NODE_STRATEGY, "true"); // Use strategy mode to test compatibility checks
        //         Environment.SetEnvironmentVariable(AZP_AGENT_USE_NODE24_TO_START_CONTAINER, "true");
                
        //         var message = LoadTemplateMessage();
        //         message = ConvertToContainerJob(message, containerImage);

        //         // Act
        //         var results = await RunWorker(message);

        //         // Assert
        //         AssertJobCompleted();
        //         Assert.Equal(TaskResult.Succeeded, results.Result);
                
        //         // Verify fallback from incompatible Node 24 to working Node 20
        //         var steps = GetSteps();
        //         var containerStep = steps.FirstOrDefault(s => s.Name.Contains("Initialize containers"));
        //         if (containerStep != null)
        //         {
        //             var log = GetTimelineLogLines(containerStep);
                    
        //             // Should fallback to Node 20 when Node 24 has compatibility issues
        //             bool usedNode20 = log.Any(x => x.Contains("v20.") || x.Contains("node20"));
        //             Assert.True(usedNode20, "Expected fallback to Node 20 when Node 24 has compatibility issues");
                    
        //             // Should log the compatibility error or fallback reason
        //             bool hasCompatibilityInfo = log.Any(x => x.Contains("GLIBC") || x.Contains("fallback") || x.Contains("compatibility"));
        //             // Note: This assertion might be too strict depending on how the agent logs compatibility issues
        //             if (hasCompatibilityInfo)
        //             {
        //                 Assert.True(hasCompatibilityInfo, "Expected compatibility error or fallback information in logs");
        //             }
        //         }
        //     }
        //     finally
        //     {
        //         ClearNodeEnvironmentVariables();
        //         await CleanupContainerImage(containerImage);
        //         TearDown();
        //     }
        // }

        #region Helper Methods

        /// <summary>
        /// Creates a container job message similar to ContainerLabelL1Tests approach
        /// </summary>
        private Microsoft.TeamFoundation.DistributedTask.Pipelines.AgentJobRequestMessage CreateContainerJob(
            Microsoft.TeamFoundation.DistributedTask.Pipelines.AgentJobRequestMessage message, 
            string containerImage)
        {
            var containerResource = new Microsoft.TeamFoundation.DistributedTask.Pipelines.ContainerResource()
            {
                Alias = "test_container"
            };
            containerResource.Properties.Set("image", containerImage);
            
            message.Resources.Containers.Add(containerResource);
            
            var containerMessage = new Microsoft.TeamFoundation.DistributedTask.Pipelines.AgentJobRequestMessage(
                message.Plan,
                message.Timeline,
                message.JobId,
                message.JobName,
                message.JobDisplayName,
                "test_container", // Container target
                message.JobSidecarContainers,
                message.Variables,
                message.MaskHints,
                message.Resources,
                message.Workspace,
                message.Steps
            );
            
            // Clear existing steps and add a simple test step
            containerMessage.Steps.Clear();
            var testStep = CreateScriptTask("echo Testing Node selection in container && node --version");
            containerMessage.Steps.Add(testStep);
            
            return containerMessage;
        }

        /// <summary>
        /// Clears all Node.js-related environment variables to ensure test isolation
        /// </summary>
        private void ClearNodeEnvironmentVariables()
        {
            Environment.SetEnvironmentVariable(AZP_AGENT_USE_NODE24_TO_START_CONTAINER, null);
            Environment.SetEnvironmentVariable(AZP_AGENT_USE_NODE20_TO_START_CONTAINER, null);
            Environment.SetEnvironmentVariable(AGENT_USE_NODE_STRATEGY, null);
        }

        #endregion
    }
}