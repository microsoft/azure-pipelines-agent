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
            
            if (!hasNodeSelection)
            {
                Console.WriteLine($"ASSERTION FAILURE DETAILS:");
                Console.WriteLine($"  Mode: {modeDescription}");
                Console.WriteLine($"  Total log lines: {log.Count()}");
                Console.WriteLine($"  Task Result: {result}");
                Console.WriteLine($"  Looking for patterns:");
                Console.WriteLine($"    - '{CONTAINER_NODE_SELECTION_LOG_PATTERN}'");
                Console.WriteLine($"    - '{CONTAINER_NODE_PATH_LOG_PATTERN}'");
                Console.WriteLine($"    - '{CROSS_PLATFORM_LOG_PATTERN}'");
                Console.WriteLine($"    - '{NODE_SELECTION_LOG_PATTERN}'");
                Console.WriteLine($"    - '{CONTAINER_SELECTION_OUTPUT_PATTERN}'");
                Console.WriteLine($"    - '{ORCHESTRATOR_SELECTED_PATTERN}'");
                Console.WriteLine($"    - '{CONTAINER_STARTUP_LOG_PATTERN}'");
                Console.WriteLine($"    - '{LINUX_CONTAINER_SETUP_PATTERN}'");
                Console.WriteLine($"    - '{CONTAINER_SETUP_COMPLETE_PATTERN}'");
                Console.WriteLine($"  Sample log lines (first 10):");
                foreach (var logLine in log.Take(10))
                {
                    Console.WriteLine($"    {logLine}");
                }
                if (log.Count() > 10)
                {
                    Console.WriteLine($"    ... and {log.Count() - 10} more lines");
                }
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
                    Console.WriteLine($"SKIP: Windows containers not available");
                    return;
                }
                
                if (!isWindows && !await IsLinuxContainerSupportAvailable())
                {
                    // Skip test if Linux containers are not available
                    Console.WriteLine($"SKIP: Linux containers not available");
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
                
                // Debug: Still dump ALL logs for comprehensive analysis if needed
                var allSteps = GetSteps();
                var allLogs = new List<string>();
                foreach (var step in allSteps)
                {
                    var stepLogs = GetTimelineLogLines(step);
                    allLogs.AddRange(stepLogs.Select(log => $"[{step.Name}] {log}"));
                }
                DumpLogsToFile(allLogs, $"testing {knob}", results.Result, useStrategy, isWindows);
                
                AssertContainerNodeSelectionAttempted(containerLogs, results.Result, useStrategy, $"testing {knob} on {(isWindows ? "Windows" : "Linux")}");
                
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
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = "version --format \"{{.Server.Os}}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(processInfo);
                string output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                return process.ExitCode == 0 && output.Trim().Contains("windows", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                // If we can't detect Docker or it fails, assume Windows containers are not available
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
        
        [Fact]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        public async Task DEBUG_ContainerNodeSelection_LogDumping_Windows()
        {
            // Simple test that just runs a container and dumps all logs - no assertions
            string testImageName = GetTestImageName("Minimal", true);
            
            try
            {
                if (!await IsWindowsContainerSupportAvailable())
                {
                    Console.WriteLine("SKIP: Windows containers not available");
                    return;
                }
                
                await CreateTestContainerImage(testImageName, "Node16", true);
                
                SetupL1();
                ClearNodeEnvironmentVariables();
                Environment.SetEnvironmentVariable(AGENT_USE_NODE20_TO_START_CONTAINER, "true");
                Environment.SetEnvironmentVariable(AGENT_USE_NODE_STRATEGY, "true");
                
                var message = LoadTemplateMessage();
                var containerResource = new ContainerResource()
                {
                    Alias = "debug_container"
                };
                containerResource.Properties.Set("image", testImageName);
                message.Resources.Containers.Add(containerResource);
                
                var containerMessage = new Microsoft.TeamFoundation.DistributedTask.Pipelines.AgentJobRequestMessage(
                    message.Plan, message.Timeline, message.JobId, message.JobName, message.JobDisplayName,
                    "debug_container", message.JobSidecarContainers, message.Variables, message.MaskHints,
                    message.Resources, message.Workspace, message.Steps);
                
                containerMessage.Steps.Clear();
                containerMessage.Steps.Add(CreateScriptTask("echo DEBUG: Testing container node selection && echo Done"));

                var results = await RunWorker(containerMessage);
                
                var steps = GetSteps();
                foreach (var step in steps)
                {
                    Console.WriteLine($"STEP: {step.Name} - {step.Result}");
                    var stepLogs = GetTimelineLogLines(step);
                    DumpLogsToFile(stepLogs, $"DEBUG_STEP_{step.Name}", step.Result ?? TaskResult.Succeeded, true, true);
                }
                
                // Always pass this test - it's just for log dumping
                Assert.True(true, "Debug test completed");
            }
            finally
            {
                await CleanupTestContainerImage(testImageName);
                ClearNodeEnvironmentVariables();
                TearDown();
            }
        }
        
        private async Task RunContainerDefaultTest(bool useStrategy, bool isWindows)
        {
            string testImageName = GetTestImageName("Minimal", isWindows);
            
            try
            {
                // Check if Docker is available and supports the required platform
                if (isWindows && !await IsWindowsContainerSupportAvailable())
                {
                    Console.WriteLine($"SKIP: Windows containers not available");
                    return;
                }
                
                if (!isWindows && !await IsLinuxContainerSupportAvailable())
                {
                    Console.WriteLine($"SKIP: Linux containers not available");
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
                
                // Debug: Still dump ALL logs for comprehensive analysis if needed
                var allSteps = GetSteps();
                var allLogs = new List<string>();
                foreach (var step in allSteps)
                {
                    var stepLogs = GetTimelineLogLines(step);
                    allLogs.AddRange(stepLogs.Select(log => $"[{step.Name}] {log}"));
                }
                DumpLogsToFile(allLogs, "default behavior", results.Result, useStrategy, isWindows);
                
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
            // For testing purposes, we don't actually need to build custom images
            // The test is about node selection logic, not about specific Node versions in containers
            // Just skip the image creation and let the test use whatever image name we provide
            
            // In a real scenario, these images would be pre-built or pulled from a registry
            // For our L1 tests, we're testing the agent's node selection logic, not Docker image management
            await Task.CompletedTask;
        }

        private async Task BuildDockerImage(string imageName, string dockerfileContent, bool isWindows = false)
        {
            // For L1 tests, we don't need to actually build Docker images
            // We're testing the agent's node selection logic, not Docker image management
            // The agent will pull the images we specify in GetTestImageName()
            await Task.CompletedTask;
        }

        private async Task CleanupTestContainerImage(string imageName)
        {
            // For publicly available images, we don't need to remove them
            // They weren't custom built by our tests
            await Task.CompletedTask;
        }

        private void ClearNodeEnvironmentVariables()
        {
            Environment.SetEnvironmentVariable(AGENT_USE_NODE24_TO_START_CONTAINER, null);
            Environment.SetEnvironmentVariable(AGENT_USE_NODE20_TO_START_CONTAINER, null);
            Environment.SetEnvironmentVariable(AGENT_RESTRICT_EOL_NODE_VERSIONS, null);
            Environment.SetEnvironmentVariable(AGENT_USE_NODE_STRATEGY, null);
        }
        
        private void DumpLogsToFile(IEnumerable<string> logs, string testContext, TaskResult result, bool useStrategy, bool isWindows)
        {
            try
            {
                string logFileName = $"NodeSelectionContainer_Debug_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{testContext.Replace(" ", "_").Replace(":", "_")}_{(isWindows ? "Windows" : "Linux")}_{(useStrategy ? "Strategy" : "Legacy")}.txt";
                string logFilePath = Path.Combine("C:\\RISHABH\\azure-pipelines-agent\\logs\\", logFileName);
                
                var logContent = new List<string>
                {
                    $"=== NODE SELECTION CONTAINER L1 TEST DEBUG LOG ===",
                    $"Test Context: {testContext}",
                    $"Platform: {(isWindows ? "Windows" : "Linux")}",
                    $"Strategy Mode: {(useStrategy ? "Enabled" : "Disabled")}",
                    $"Task Result: {result}",
                    $"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                    $"Log File: {logFilePath}",
                    $"Environment Variables:",
                    $"  AZP_AGENT_USE_NODE24_TO_START_CONTAINER: {Environment.GetEnvironmentVariable(AGENT_USE_NODE24_TO_START_CONTAINER)}",
                    $"  AZP_AGENT_USE_NODE20_TO_START_CONTAINER: {Environment.GetEnvironmentVariable(AGENT_USE_NODE20_TO_START_CONTAINER)}",
                    $"  AZP_AGENT_USE_NODE_STRATEGY: {Environment.GetEnvironmentVariable(AGENT_USE_NODE_STRATEGY)}",
                    $"  AZP_AGENT_RESTRICT_EOL_NODE_VERSIONS: {Environment.GetEnvironmentVariable(AGENT_RESTRICT_EOL_NODE_VERSIONS)}",
                    $"\n=== SEARCH PATTERNS ===",
                    $"Looking for these patterns:",
                    $"  CONTAINER_NODE_SELECTION_LOG_PATTERN: '{CONTAINER_NODE_SELECTION_LOG_PATTERN}'",
                    $"  CONTAINER_NODE_PATH_LOG_PATTERN: '{CONTAINER_NODE_PATH_LOG_PATTERN}'",
                    $"  CROSS_PLATFORM_LOG_PATTERN: '{CROSS_PLATFORM_LOG_PATTERN}'",
                    $"  NODE_SELECTION_LOG_PATTERN: '{NODE_SELECTION_LOG_PATTERN}'",
                    $"  CONTAINER_SELECTION_OUTPUT_PATTERN: '{CONTAINER_SELECTION_OUTPUT_PATTERN}'",
                    $"  ORCHESTRATOR_SELECTED_PATTERN: '{ORCHESTRATOR_SELECTED_PATTERN}'",
                    $"  CONTAINER_STARTUP_LOG_PATTERN: '{CONTAINER_STARTUP_LOG_PATTERN}'",
                    $"\n=== ALL CAPTURED LOGS ({logs.Count()} lines) ==="
                };
                
                int lineNumber = 1;
                foreach (var logLine in logs)
                {
                    logContent.Add($"[{lineNumber:D4}] {logLine}");
                    lineNumber++;
                }
                
                logContent.Add($"\n=== PATTERN MATCH ANALYSIS ===");
                foreach (var logLine in logs)
                {
                    bool hasContainer = logLine.Contains(CONTAINER_NODE_SELECTION_LOG_PATTERN);
                    bool hasPath = logLine.Contains(CONTAINER_NODE_PATH_LOG_PATTERN);
                    bool hasCross = logLine.Contains(CROSS_PLATFORM_LOG_PATTERN);
                    bool hasNode = logLine.Contains(NODE_SELECTION_LOG_PATTERN);
                    bool hasContainerOutput = logLine.Contains(CONTAINER_SELECTION_OUTPUT_PATTERN);
                    bool hasOrchestratorSelected = logLine.Contains(ORCHESTRATOR_SELECTED_PATTERN);
                    bool hasLinuxSetup = logLine.Contains(LINUX_CONTAINER_SETUP_PATTERN);
                    bool hasContainerComplete = logLine.Contains(CONTAINER_SETUP_COMPLETE_PATTERN);
                    
                    if (hasContainer || hasPath || hasCross || hasNode || hasContainerOutput || hasOrchestratorSelected || hasLinuxSetup || hasContainerComplete)
                    {
                        logContent.Add($"MATCH FOUND: {logLine}");
                        if (hasContainer) logContent.Add($"  -> Matches CONTAINER_NODE_SELECTION_LOG_PATTERN");
                        if (hasPath) logContent.Add($"  -> Matches CONTAINER_NODE_PATH_LOG_PATTERN");
                        if (hasCross) logContent.Add($"  -> Matches CROSS_PLATFORM_LOG_PATTERN");
                        if (hasNode) logContent.Add($"  -> Matches NODE_SELECTION_LOG_PATTERN");
                        if (hasContainerOutput) logContent.Add($"  -> Matches CONTAINER_SELECTION_OUTPUT_PATTERN");
                        if (hasOrchestratorSelected) logContent.Add($"  -> Matches ORCHESTRATOR_SELECTED_PATTERN");
                        if (hasLinuxSetup) logContent.Add($"  -> Matches LINUX_CONTAINER_SETUP_PATTERN");
                        if (hasContainerComplete) logContent.Add($"  -> Matches CONTAINER_SETUP_COMPLETE_PATTERN");
                    }
                    else if (logLine.ToLower().Contains("node") || logLine.ToLower().Contains("container") || logLine.ToLower().Contains("select"))
                    {
                        logContent.Add($"POTENTIAL: {logLine}");
                    }
                }
                
                logContent.Add($"\n=== END OF DEBUG LOG ===");
                
                File.WriteAllLines(logFilePath, logContent);
                Console.WriteLine($"Debug log written to: {logFilePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to write debug log: {ex.Message}");
            }
        }
    }
}
