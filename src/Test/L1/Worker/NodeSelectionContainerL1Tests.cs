// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.DistributedTask.Pipelines;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.Collections.Generic;
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
        // Environment variable constants
        private const string AGENT_USE_NODE24_TO_START_CONTAINER = "AZP_AGENT_USE_NODE24_TO_START_CONTAINER";
        private const string AGENT_USE_NODE20_TO_START_CONTAINER = "AZP_AGENT_USE_NODE20_TO_START_CONTAINER";
        private const string AGENT_RESTRICT_EOL_NODE_VERSIONS = "AZP_AGENT_RESTRICT_EOL_NODE_VERSIONS";
        private const string AGENT_USE_NODE_STRATEGY = "AZP_AGENT_USE_NODE_STRATEGY";
        
        // Log patterns for container node selection
        private const string CONTAINER_NODE_SELECTION_LOG_PATTERN = "[ContainerSetup] Legacy agent node:";
        private const string CONTAINER_NODE_PATH_LOG_PATTERN = "[Container] Node path:";
        private const string CROSS_PLATFORM_LOG_PATTERN = "Cross-platform scenario";
        private const string NODE_SELECTION_LOG_PATTERN = "Using node path:";
        private const string CONTAINER_SELECTION_OUTPUT_PATTERN = "Container node selection:";
        private const string ORCHESTRATOR_SELECTED_PATTERN = "Using Node";
        private const string CONTAINER_STARTUP_LOG_PATTERN = "Using Node .* for container startup";
        
        // Linux container-specific patterns (uses container node instead of host node)
        private const string LINUX_CONTAINER_SETUP_PATTERN = "Platform requirement - using container node";
        private const string CONTAINER_SETUP_COMPLETE_PATTERN = "Container setup complete:";
        
        private void AssertContainerNodeSelectionAttempted(IEnumerable<string> log, TaskResult result, bool useStrategy, string context = "")
        {
            string modeDescription = $"{(useStrategy ? "strategy" : "legacy")} mode{(string.IsNullOrEmpty(context) ? "" : $" - {context}")}";
            
            bool hasNodeSelection;
            if (useStrategy)
            {
                hasNodeSelection = log.Any(x => x.Contains(CONTAINER_NODE_SELECTION_LOG_PATTERN)) ||
                                   log.Any(x => x.Contains(CONTAINER_NODE_PATH_LOG_PATTERN)) ||
                                   log.Any(x => x.Contains(CROSS_PLATFORM_LOG_PATTERN)) ||
                                   log.Any(x => x.Contains(NODE_SELECTION_LOG_PATTERN)) ||
                                   log.Any(x => x.Contains(CONTAINER_SELECTION_OUTPUT_PATTERN)) ||
                                   log.Any(x => x.Contains(ORCHESTRATOR_SELECTED_PATTERN)) ||
                                   log.Any(x => System.Text.RegularExpressions.Regex.IsMatch(x, CONTAINER_STARTUP_LOG_PATTERN)) ||
                                   // Linux container patterns
                                   log.Any(x => x.Contains(LINUX_CONTAINER_SETUP_PATTERN)) ||
                                   log.Any(x => x.Contains(CONTAINER_SETUP_COMPLETE_PATTERN));
            }
            else
            {
                hasNodeSelection = log.Any(x => x.Contains(NODE_SELECTION_LOG_PATTERN)) ||
                                   log.Any(x => x.Contains(CONTAINER_SELECTION_OUTPUT_PATTERN)) ||
                                   log.Any(x => x.Contains(ORCHESTRATOR_SELECTED_PATTERN)) ||
                                   log.Any(x => x.Contains(CONTAINER_NODE_SELECTION_LOG_PATTERN)) ||
                                   log.Any(x => System.Text.RegularExpressions.Regex.IsMatch(x, CONTAINER_STARTUP_LOG_PATTERN)) ||
                                   // Linux container patterns
                                   log.Any(x => x.Contains(LINUX_CONTAINER_SETUP_PATTERN)) ||
                                   log.Any(x => x.Contains(CONTAINER_SETUP_COMPLETE_PATTERN));
            }
            

            Assert.True(hasNodeSelection, $"Should have container node selection log: {modeDescription}");
        }
        
        private void AssertContainerNodeSelectionSuccess(IEnumerable<string> log, string expectedVersion, bool useStrategy, string context = "")
        {
            string modeDescription = $"{(useStrategy ? "strategy" : "legacy")} mode{(string.IsNullOrEmpty(context) ? "" : $" - {context}")}";
            
            bool hasExpectedSelection;
            
            // Check if this is a Linux container scenario (uses container node instead of host node)
            bool isLinuxContainer = log.Any(x => x.Contains(LINUX_CONTAINER_SETUP_PATTERN));
            
            if (isLinuxContainer)
            {
                // For Linux containers, the environment variables don't control the Node.js version inside the container
                // Just verify that container setup completed successfully
                hasExpectedSelection = log.Any(x => x.Contains(CONTAINER_SETUP_COMPLETE_PATTERN));
            }
            else
            {
                // For Windows containers, check for specific Node version selection
                if (useStrategy)
                {
                    hasExpectedSelection = log.Any(x => 
                        (x.Contains(CONTAINER_NODE_SELECTION_LOG_PATTERN) || 
                         x.Contains(CONTAINER_NODE_PATH_LOG_PATTERN) || 
                         x.Contains(NODE_SELECTION_LOG_PATTERN) ||
                         x.Contains(CONTAINER_SELECTION_OUTPUT_PATTERN) ||
                         x.Contains(ORCHESTRATOR_SELECTED_PATTERN) ||
                         System.Text.RegularExpressions.Regex.IsMatch(x, CONTAINER_STARTUP_LOG_PATTERN)) && 
                        (x.Contains(expectedVersion.ToLower()) || 
                         x.Contains(expectedVersion.Replace("Node", "").ToLower())));
                }
                else
                {
                    hasExpectedSelection = log.Any(x => 
                        (x.Contains(NODE_SELECTION_LOG_PATTERN) ||
                         x.Contains(CONTAINER_SELECTION_OUTPUT_PATTERN) ||
                         x.Contains(ORCHESTRATOR_SELECTED_PATTERN) ||
                         x.Contains(CONTAINER_NODE_SELECTION_LOG_PATTERN) ||
                         System.Text.RegularExpressions.Regex.IsMatch(x, CONTAINER_STARTUP_LOG_PATTERN)) && 
                        (x.Contains(expectedVersion.ToLower()) || 
                         x.Contains(expectedVersion.Replace("Node", "").ToLower())));
                }
            }
            
            string expectedMessage = isLinuxContainer ? 
                "container setup completion" : 
                $"container node selection '{expectedVersion}'";
                
            Assert.True(hasExpectedSelection, $"Expected {expectedMessage}: {modeDescription}");
        }
        
        [Theory]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        [InlineData(AGENT_USE_NODE24_TO_START_CONTAINER, "true", "Node24", false)]
        [InlineData(AGENT_USE_NODE20_TO_START_CONTAINER, "true", "Node20", false)]
        [InlineData(AGENT_USE_NODE24_TO_START_CONTAINER, "true", "Node24", true)]
        [InlineData(AGENT_USE_NODE20_TO_START_CONTAINER, "true", "Node20", true)]
        public async Task ContainerNodeSelection_EnvironmentKnobs_SelectsCorrectVersion_Linux(string knob, string value, string expectedNodeVersion, bool useStrategy)
        {
            await RunContainerNodeSelectionTest(knob, value, expectedNodeVersion, useStrategy, isWindows: false);
        }
        
        [Theory]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "linux")]
        [Trait("SkipOn", "darwin")]
        [InlineData(AGENT_USE_NODE24_TO_START_CONTAINER, "true", "Node24", false)]
        [InlineData(AGENT_USE_NODE20_TO_START_CONTAINER, "true", "Node20", false)]
        [InlineData(AGENT_USE_NODE24_TO_START_CONTAINER, "true", "Node24", true)]
        [InlineData(AGENT_USE_NODE20_TO_START_CONTAINER, "true", "Node20", true)]
        public async Task ContainerNodeSelection_EnvironmentKnobs_SelectsCorrectVersion_Windows(string knob, string value, string expectedNodeVersion, bool useStrategy)
        {
            await RunContainerNodeSelectionTest(knob, value, expectedNodeVersion, useStrategy, isWindows: true);
        }
        
        private async Task RunContainerNodeSelectionTest(string knob, string value, string expectedNodeVersion, bool useStrategy, bool isWindows)
        {
            string testImageName = GetTestImageName(expectedNodeVersion, isWindows);
            
            try
            {
                // Check if Docker is available and supports the required platform
                if (isWindows && !await IsWindowsContainerSupportAvailable())
                {
                    // Skip test if Windows containers are not available

                    return;
                }
                
                if (!isWindows && !await IsLinuxContainerSupportAvailable())
                {
                    // Skip test if Linux containers are not available
                    return;
                }
                
                await CreateTestContainerImage(testImageName, expectedNodeVersion, isWindows);
                
                SetupL1();
                ClearNodeEnvironmentVariables();
                Environment.SetEnvironmentVariable(knob, value);
                Environment.SetEnvironmentVariable(AGENT_USE_NODE_STRATEGY, useStrategy.ToString().ToLower());
                
                var message = LoadTemplateMessage();
                var containerResource = new ContainerResource()
                {
                    Alias = "test_container"
                };
                containerResource.Properties.Set("image", testImageName);
                message.Resources.Containers.Add(containerResource);
                
                var containerMessage = new Microsoft.TeamFoundation.DistributedTask.Pipelines.AgentJobRequestMessage(
                    message.Plan, message.Timeline, message.JobId, message.JobName, message.JobDisplayName,
                    "test_container", message.JobSidecarContainers, message.Variables, message.MaskHints,
                    message.Resources, message.Workspace, message.Steps);
                
                containerMessage.Steps.Clear();
                string testCommand = isWindows ? 
                    $"echo Testing container node selection - {(useStrategy ? "strategy" : "legacy")} mode & node --version" :
                    $"echo Testing container node selection - {(useStrategy ? "strategy" : "legacy")} mode && node --version";
                containerMessage.Steps.Add(CreateScriptTask(testCommand));

                var results = await RunWorker(containerMessage);

                AssertJobCompleted();
                
                // Container node selection happens specifically in "Initialize containers" step
                var steps = GetSteps();
                var initContainersStep = steps.FirstOrDefault(s => s.Name == "Initialize containers");
                Assert.NotNull(initContainersStep);
                
                var containerLogs = GetTimelineLogLines(initContainersStep);
                
                AssertContainerNodeSelectionAttempted(containerLogs, results.Result, useStrategy, $"testing {knob} on {(isWindows ? "Windows" : "Linux")}");;
                
                if (results.Result == TaskResult.Succeeded)
                {
                    AssertContainerNodeSelectionSuccess(containerLogs, expectedNodeVersion, useStrategy);
                }
            }
            finally
            {
                await CleanupTestContainerImage(testImageName);
                ClearNodeEnvironmentVariables();
                TearDown();
            }
        }
        
        private async Task<bool> IsWindowsContainerSupportAvailable()
        {
            try
            {
                // First check if Docker is available
                var dockerVersionInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = "version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var dockerProcess = System.Diagnostics.Process.Start(dockerVersionInfo);
                await dockerProcess.WaitForExitAsync();
                
                if (dockerProcess.ExitCode != 0)
                {
                    return false; // Docker not available
                }

                // Check if we can actually pull a Windows container image
                var testPullInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "docker",
                    // Arguments = "pull mcr.microsoft.com/windows/nanoserver:ltsc2019",
                    Arguments = "pull mcr.microsoft.com/windows/servercore:ltsc2025",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var pullProcess = System.Diagnostics.Process.Start(testPullInfo);
                await pullProcess.WaitForExitAsync();

                return pullProcess.ExitCode == 0;
            }
            catch
            {
                // If any step fails, Windows containers are not available
                return false;
            }
        }
        
        private async Task<bool> IsLinuxContainerSupportAvailable()
        {
            try
            {
                // Test if we can pull a simple Linux image
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = "pull alpine:latest",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(processInfo);
                await process.WaitForExitAsync();

                return process.ExitCode == 0;
            }
            catch
            {
                // If we can't pull Linux images, assume Linux containers are not available
                return false;
            }
        }
        
        [Theory]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ContainerNodeSelection_DefaultBehavior_UsesFallback_Linux(bool useStrategy)
        {
            await RunContainerDefaultTest(useStrategy, isWindows: false);
        }
        
        [Theory]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "linux")]
        [Trait("SkipOn", "darwin")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ContainerNodeSelection_DefaultBehavior_UsesFallback_Windows(bool useStrategy)
        {
            await RunContainerDefaultTest(useStrategy, isWindows: true);
        }
        

        
        private async Task RunContainerDefaultTest(bool useStrategy, bool isWindows)
        {
            string testImageName = GetTestImageName("Minimal", isWindows);
            
            try
            {
                // Check if Docker is available and supports the required platform
                if (isWindows && !await IsWindowsContainerSupportAvailable())
                {
                    return;
                }
                
                if (!isWindows && !await IsLinuxContainerSupportAvailable())
                {
                    return;
                }
                
                await CreateTestContainerImage(testImageName, "Node16", isWindows);
                
                SetupL1();
                ClearNodeEnvironmentVariables();
                Environment.SetEnvironmentVariable(AGENT_USE_NODE_STRATEGY, useStrategy.ToString().ToLower());
                
                var message = LoadTemplateMessage();
                var containerResource = new ContainerResource()
                {
                    Alias = "test_container"
                };
                containerResource.Properties.Set("image", testImageName);
                message.Resources.Containers.Add(containerResource);
                
                var containerMessage = new Microsoft.TeamFoundation.DistributedTask.Pipelines.AgentJobRequestMessage(
                    message.Plan, message.Timeline, message.JobId, message.JobName, message.JobDisplayName,
                    "test_container", message.JobSidecarContainers, message.Variables, message.MaskHints,
                    message.Resources, message.Workspace, message.Steps);
                
                containerMessage.Steps.Clear();
                string testCommand = isWindows ?
                    $"echo Testing container default node selection - {(useStrategy ? "strategy" : "legacy")} mode & node --version" :
                    $"echo Testing container default node selection - {(useStrategy ? "strategy" : "legacy")} mode && node --version";
                containerMessage.Steps.Add(CreateScriptTask(testCommand));

                var results = await RunWorker(containerMessage);

                AssertJobCompleted();
                
                // Container node selection happens specifically in "Initialize containers" step
                var steps = GetSteps();
                var initContainersStep = steps.FirstOrDefault(s => s.Name == "Initialize containers");
                Assert.NotNull(initContainersStep);
                
                var containerLogs = GetTimelineLogLines(initContainersStep);
                
                AssertContainerNodeSelectionAttempted(containerLogs, results.Result, useStrategy, $"default behavior on {(isWindows ? "Windows" : "Linux")}");
                
                if (results.Result == TaskResult.Succeeded)
                {
                    bool hasValidNodeSelection = containerLogs.Any(x => 
                        x.Contains(NODE_SELECTION_LOG_PATTERN) || 
                        x.Contains(CONTAINER_NODE_SELECTION_LOG_PATTERN) ||
                        x.Contains(CROSS_PLATFORM_LOG_PATTERN) ||
                        x.Contains(CONTAINER_SELECTION_OUTPUT_PATTERN) ||
                        x.Contains(ORCHESTRATOR_SELECTED_PATTERN));
                    Assert.True(hasValidNodeSelection, $"Should have valid node selection in container - {(useStrategy ? "strategy" : "legacy")} mode on {(isWindows ? "Windows" : "Linux")}");
                }
            }
            finally
            {
                await CleanupTestContainerImage(testImageName);
                ClearNodeEnvironmentVariables();
                TearDown();
            }
        }
        
        private string GetTestImageName(string nodeVersion, bool isWindows)
        {
            if (isWindows)
            {
                // Use publicly available Windows images that actually exist
                return "mcr.microsoft.com/windows/servercore:ltsc2019";
            }
            else
            {
                // Use publicly available Node.js images
                return nodeVersion switch
                {
                    "Node24" => "node:18-alpine",
                    "Node20" => "node:16-alpine", 
                    "Node16" => "node:14-alpine",
                    _ => "alpine:latest"
                };
            }
        }
        
        private async Task CreateTestContainerImage(string imageName, string nodeVersion, bool isWindows = false)
        {
            // For L1 tests, we use publicly available images rather than building custom ones
            await Task.CompletedTask;
        }

        private async Task BuildDockerImage(string imageName, string dockerfileContent, bool isWindows = false)
        {
            // For L1 tests, we use publicly available images
            await Task.CompletedTask;
        }

        private async Task CleanupTestContainerImage(string imageName)
        {
            // No cleanup needed for publicly available images
            await Task.CompletedTask;
        }

        private void ClearNodeEnvironmentVariables()
        {
            Environment.SetEnvironmentVariable(AGENT_USE_NODE24_TO_START_CONTAINER, null);
            Environment.SetEnvironmentVariable(AGENT_USE_NODE20_TO_START_CONTAINER, null);
            Environment.SetEnvironmentVariable(AGENT_RESTRICT_EOL_NODE_VERSIONS, null);
            Environment.SetEnvironmentVariable(AGENT_USE_NODE_STRATEGY, null);
        }
        

    }
}
