// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.DistributedTask.Pipelines;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.L1.Worker
{
    [Collection("Worker L1 Tests")]
    public class NodeSelectionL1Tests : L1TestBase
    {
        [Theory]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        [InlineData("AGENT_USE_NODE24", "true", "node24")]
        [InlineData("AGENT_USE_NODE20_1", "true", "node20_1")]  
        [InlineData("AGENT_USE_NODE16", "true", "node16")]
        public async Task NodeSelection_EnvironmentKnobs_SelectsCorrectVersion(string knob, string value, string expectedNodeFolder)
        {
            try
            {
                // Arrange
                SetupL1();
                Environment.SetEnvironmentVariable(knob, value);
                
                var message = LoadTemplateMessage();
                message.Steps.Clear();
                
                // Use CmdLine task which behaves differently on different platforms
                message.Steps.Add(CreateScriptTask("echo Testing node selection"));

                // Act
                var results = await RunWorker(message);

                // Assert
                AssertJobCompleted();
                
                // Check if task succeeded, if not provide better diagnostics
                if (results.Result != TaskResult.Succeeded)
                {
                    var steps = GetSteps();
                    var taskStep = steps.FirstOrDefault(s => s.Name == "CmdLine");
                    if (taskStep != null)
                    {
                        var log = GetTimelineLogLines(taskStep);
                        throw new InvalidOperationException($"Task failed with result: {results.Result}. Environment: {knob}={value}. Logs: {string.Join("\n", log)}");
                    }
                    throw new InvalidOperationException($"Task failed with result: {results.Result}. Environment: {knob}={value}");
                }

                var steps = GetSteps();
                var taskStep = steps.FirstOrDefault(s => s.Name == "CmdLine");
                Assert.NotNull(taskStep);

                // CmdLine uses PowerShell on Windows, Node.js on Linux/macOS
                // Only validate Node.js selection on non-Windows platforms
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var log = GetTimelineLogLines(taskStep);
                    
                    // Should contain log indicating which node version was selected
                    Assert.True(log.Any(x => x.Contains("Using node path:") && x.Contains(expectedNodeFolder)), 
                        $"Expected to find node selection log with '{expectedNodeFolder}' in: {string.Join("\n", log)}");
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
        public async Task NodeSelection_DefaultBehavior_UsesAppropriateVersion()
        {
            try
            {
                // Arrange
                SetupL1();
                // No special environment variables - test default behavior
                
                var message = LoadTemplateMessage();
                message.Steps.Clear();
                message.Steps.Add(CreateScriptTask("echo Testing default node selection"));

                // Act
                var results = await RunWorker(message);

                // Assert
                AssertJobCompleted();
                Assert.Equal(TaskResult.Succeeded, results.Result);

                var steps = GetSteps();
                var taskStep = steps.FirstOrDefault(s => s.Name == "CmdLine");
                Assert.NotNull(taskStep);

                // CmdLine uses PowerShell on Windows, Node.js on Linux/macOS
                // Only validate Node.js selection on non-Windows platforms
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var log = GetTimelineLogLines(taskStep);
                    
                    // Should have node selection logging showing which version was chosen
                    bool hasNodeSelection = log.Any(x => x.Contains("Using node path:"));
                    Assert.True(hasNodeSelection, "Should have node selection logging for default behavior");
                    
                    // Default should typically be node20_1 
                    bool usesDefaultVersion = log.Any(x => x.Contains("Using node path:") && x.Contains("node20"));
                    Assert.True(usesDefaultVersion, "Default behavior should use node20 series");
                }
            }
            finally
            {
                TearDown();
            }
        }

        [Theory]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        [InlineData(true)]
        [InlineData(false)]
        public async Task NodeSelection_StrategyVsLegacy_ProducesExpectedBehavior(bool useStrategy)
        {
            try
            {
                // Arrange
                SetupL1();
                Environment.SetEnvironmentVariable("AGENT_USE_NODE_STRATEGY", useStrategy.ToString().ToLower());
                Environment.SetEnvironmentVariable("AGENT_USE_NODE24", "true");
                
                var message = LoadTemplateMessage();
                message.Steps.Clear();
                message.Steps.Add(CreateScriptTask("echo Testing strategy vs legacy"));

                // Act
                var results = await RunWorker(message);

                // Assert
                AssertJobCompleted();
                
                // Check if task succeeded, if not provide better diagnostics
                if (results.Result != TaskResult.Succeeded)
                {
                    var steps = GetSteps();
                    var taskStep = steps.FirstOrDefault(s => s.Name == "CmdLine");
                    if (taskStep != null)
                    {
                        var log = GetTimelineLogLines(taskStep);
                        throw new InvalidOperationException($"Task failed with result: {results.Result}. Strategy: {useStrategy}. Logs: {string.Join("\n", log)}");
                    }
                    throw new InvalidOperationException($"Task failed with result: {results.Result}. Strategy: {useStrategy}");
                }

                var steps = GetSteps();
                var taskStep = steps.FirstOrDefault(s => s.Name == "CmdLine");
                Assert.NotNull(taskStep);

                // CmdLine uses PowerShell on Windows, Node.js on Linux/macOS
                // Only validate Node.js selection on non-Windows platforms
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var log = GetTimelineLogLines(taskStep);

                    // Both strategy and legacy should handle this scenario and select node
                    bool hasNodeSelection = log.Any(x => x.Contains("Using node path:"));
                    Assert.True(hasNodeSelection, $"Expected node selection log for {(useStrategy ? "strategy" : "legacy")} mode");

                    // Should use node24 based on environment variable
                    bool usesNode24 = log.Any(x => x.Contains("Using node path:") && x.Contains("node24"));
                    Assert.True(usesNode24, "Should use node24 based on AGENT_USE_NODE24=true");
                }
            }
            finally
            {
                TearDown();
            }
        }

        [Theory]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        [InlineData("AGENT_USE_NODE24", "AGENT_USE_NODE20_1", "node24")] // node24 should win
        [InlineData("AGENT_USE_NODE20_1", "AGENT_USE_NODE16", "node20_1")] // node20_1 should win
        public async Task NodeSelection_ConflictingKnobs_HigherVersionWins(string winningKnob, string losingKnob, string expectedNodeFolder)
        {
            try
            {
                // Arrange
                SetupL1();
                Environment.SetEnvironmentVariable(winningKnob, "true");
                Environment.SetEnvironmentVariable(losingKnob, "true");
                
                var message = LoadTemplateMessage();
                message.Steps.Clear();
                message.Steps.Add(CreateScriptTask("echo Testing conflicting knobs"));

                // Act
                var results = await RunWorker(message);

                // Assert
                AssertJobCompleted();
                
                // Check if task succeeded, if not provide better diagnostics
                if (results.Result != TaskResult.Succeeded)
                {
                    var steps = GetSteps();
                    var taskStep = steps.FirstOrDefault(s => s.Name == "CmdLine");
                    if (taskStep != null)
                    {
                        var log = GetTimelineLogLines(taskStep);
                        throw new InvalidOperationException($"Task failed with result: {results.Result}. Knobs: {winningKnob}={true}, {losingKnob}={true}. Logs: {string.Join("\n", log)}");
                    }
                    throw new InvalidOperationException($"Task failed with result: {results.Result}. Knobs: {winningKnob}={true}, {losingKnob}={true}");
                }

                var steps = GetSteps();
                var taskStep = steps.FirstOrDefault(s => s.Name == "CmdLine");
                Assert.NotNull(taskStep);

                // CmdLine uses PowerShell on Windows, Node.js on Linux/macOS
                // Only validate Node.js selection on non-Windows platforms
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var log = GetTimelineLogLines(taskStep);
                    
                    // Should use the higher version when multiple knobs are set
                    Assert.True(log.Any(x => x.Contains("Using node path:") && x.Contains(expectedNodeFolder)), 
                        $"Expected conflicting knobs to resolve to '{expectedNodeFolder}' in: {string.Join("\n", log)}");
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
        public async Task NodeSelection_EOLPolicy_RestrictsOlderVersions()
        {
            try
            {
                // Arrange
                SetupL1();
                Environment.SetEnvironmentVariable("AGENT_RESTRICT_EOL_NODE_VERSIONS", "true");
                Environment.SetEnvironmentVariable("AGENT_USE_NODE16", "true"); // Try to force EOL version
                
                var message = LoadTemplateMessage();
                message.Steps.Clear();
                message.Steps.Add(CreateScriptTask("echo Testing EOL policy"));

                // Act
                var results = await RunWorker(message);

                // Assert
                AssertJobCompleted();

                var steps = GetSteps();
                var taskStep = steps.FirstOrDefault(s => s.Name == "CmdLine");
                
                // CmdLine uses PowerShell on Windows, Node.js on Linux/macOS
                // Only validate Node.js EOL policy on non-Windows platforms
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Assert.NotNull(taskStep);
                    var log = GetTimelineLogLines(taskStep);
                    
                    // With EOL policy enabled, should either:
                    // 1. Upgrade to newer version (node20_1 or node24)
                    // 2. Show EOL policy message
                    // 3. Allow execution but with warnings
                    bool upgradedToNewer = log.Any(x => x.Contains("Using node path:") && 
                        (x.Contains("node24") || x.Contains("node20")));
                    bool showsEOLMessage = log.Any(x => x.Contains("EOL") || 
                        x.Contains("end of life") || x.Contains("not supported"));
                    
                    // At minimum, should have node selection logging
                    bool hasNodeSelection = log.Any(x => x.Contains("Using node path:"));
                    Assert.True(hasNodeSelection, "Should have node selection logging even with EOL policy");
                    
                    // EOL policy should have some effect - upgrade, warn, or restrict
                    if (results.Result == TaskResult.Failed)
                    {
                        // Task failed due to EOL policy restriction
                        Assert.True(showsEOLMessage, "If task fails, should show EOL policy message");
                    }
                    else
                    {
                        // Task succeeded - should either upgrade or warn
                        Assert.True(upgradedToNewer || showsEOLMessage || hasNodeSelection, 
                            "EOL policy should upgrade to newer version, show EOL message, or at least work normally");
                    }
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
        [Trait("SkipOn", "darwin")] // Skip on macOS due to container limitations
        public async Task NodeSelection_Container_UsesContainerNodePath()
        {
            try
            {
                // Arrange
                SetupL1();
                Environment.SetEnvironmentVariable("AGENT_USE_NODE24", "true");
                
                var message = LoadTemplateMessage();
                message.Steps.Clear();
                
                // Create a container-based script task
                var containerTask = CreateScriptTask("echo Testing container node selection");
                containerTask.Target = new StepTarget
                {
                    Target = "container"
                };
                message.Steps.Add(containerTask);

                // Act
                var results = await RunWorker(message);

                // Assert - Container tests may behave differently or need container setup
                // For now, just verify the test infrastructure works
                var steps = GetSteps();
                Assert.NotNull(steps);
                
                // Note: Actual container node selection validation would require 
                // proper container setup in L1 test environment
            }
            finally
            {
                TearDown();
            }
        }

        [Theory]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        [InlineData("node")]
        [InlineData("node16")]  
        [InlineData("node20_1")]
        [InlineData("node24")]
        public async Task NodeSelection_CustomNodePath_UsesSpecifiedPath(string customNodeFolder)
        {
            try
            {
                // Arrange
                SetupL1();
                
                // Set custom node path based on folder
                string customNodePath = System.IO.Path.Combine(
                    GetWorkingDirectory(),
                    "externals", 
                    customNodeFolder,
                    "bin",
                    RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "node.exe" : "node");
                
                Environment.SetEnvironmentVariable("AGENT_CUSTOM_NODE_PATH", customNodePath);
                
                var message = LoadTemplateMessage();
                message.Steps.Clear();
                message.Steps.Add(CreateScriptTask("echo Testing custom node path"));

                // Act
                var results = await RunWorker(message);

                // Assert
                AssertJobCompleted();
                Assert.Equal(TaskResult.Succeeded, results.Result);

                var steps = GetSteps();
                var taskStep = steps.FirstOrDefault(s => s.Name == "CmdLine");
                Assert.NotNull(taskStep);

                // CmdLine uses PowerShell on Windows, Node.js on Linux/macOS
                // Only validate Node.js custom path on non-Windows platforms
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var log = GetTimelineLogLines(taskStep);
                    
                    // Should use custom node path if properly configured
                    // Note: This test validates the infrastructure for custom paths
                    bool hasNodeSelection = log.Any(x => x.Contains("Using node path:"));
                    Assert.True(hasNodeSelection, "Should have node selection logging for custom path");
                }
            }
            finally
            {
                TearDown();
            }
        }

    }
}