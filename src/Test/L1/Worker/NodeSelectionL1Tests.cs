// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.DistributedTask.Pipelines;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
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
        // Environment variable constants
        private const string AGENT_USE_NODE24 = "AGENT_USE_NODE24";
        private const string AGENT_USE_NODE20_1 = "AGENT_USE_NODE20_1";
        private const string AGENT_USE_NODE16 = "AGENT_USE_NODE16";
        private const string AGENT_RESTRICT_EOL_NODE_VERSIONS = "AGENT_RESTRICT_EOL_NODE_VERSIONS";
        private const string AGENT_USE_NODE_STRATEGY = "AGENT_USE_NODE_STRATEGY";
        
        private const string NODE24_FOLDER = "node24";
        private const string NODE20_1_FOLDER = "node20_1";
        private const string NODE16_FOLDER = "node16";
        
        // Log pattern constants
        private const string NODE_SELECTION_LOG_PATTERN = "Using node path:";
        private const string NODE20_LOG_PATTERN = "node20"; // Logs show "node20" not "node20_1"
        
        [Theory]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "windows")] // Skip on Windows - uses PowerShell, not Node.js
        [InlineData(AGENT_USE_NODE24, "true", NODE24_FOLDER)]
        [InlineData(AGENT_USE_NODE20_1, "true", NODE20_1_FOLDER)]
        [InlineData(AGENT_USE_NODE16, "true", NODE16_FOLDER)]
        public async Task NodeSelection_EnvironmentKnobs_SelectsCorrectVersion_NonWindows(string knob, string value, string expectedNodeFolder)
        {
            try
            {
                // Arrange
                SetupL1();
                ClearNodeEnvironmentVariables();
                Environment.SetEnvironmentVariable(knob, value);
                
                var message = LoadTemplateMessage();
                message.Steps.Clear();
                message.Steps.Add(CreateScriptTask("echo Testing node selection"));

                // Act
                var results = await RunWorker(message);

                // Assert
                AssertJobCompleted();
                Assert.Equal(TaskResult.Succeeded, results.Result);
                
                var steps = GetSteps();
                var taskStep = steps.FirstOrDefault(s => s.Name == "CmdLine");
                Assert.NotNull(taskStep);

                var log = GetTimelineLogLines(taskStep);
                
                // Check for final node selection
                bool hasNodeSelection = log.Any(x => x.Contains(NODE_SELECTION_LOG_PATTERN));
                Assert.True(hasNodeSelection, "Should have final node selection log");
                
                // Verify node selection based on environment variable
                if (expectedNodeFolder == NODE16_FOLDER)
                {
                    // Node16 may fallback to newer versions if not available or restricted
                    bool usesRequestedOrNewer = log.Any(x => x.Contains(NODE_SELECTION_LOG_PATTERN) && 
                        (x.Contains(NODE16_FOLDER) || x.Contains(NODE20_LOG_PATTERN) || x.Contains(NODE24_FOLDER)));
                    Assert.True(usesRequestedOrNewer, 
                        $"Expected '{expectedNodeFolder}' or fallback to newer version in node selection log");
                }
                else if (expectedNodeFolder == NODE20_1_FOLDER)
                {
                    Assert.True(log.Any(x => x.Contains(NODE_SELECTION_LOG_PATTERN) && x.Contains(NODE20_LOG_PATTERN)), 
                        $"Expected node selection log to contain '{NODE20_LOG_PATTERN}' for {expectedNodeFolder}");
                }
                else
                {
                    Assert.True(log.Any(x => x.Contains(NODE_SELECTION_LOG_PATTERN) && x.Contains(expectedNodeFolder)), 
                        $"Expected node selection log to contain '{expectedNodeFolder}'");
                }
            }
            finally
            {
                ClearNodeEnvironmentVariables();
                TearDown();
            }
        }
        
        [Theory]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "linux")] // Skip on Linux - this test is for Windows PowerShell behavior
        [Trait("SkipOn", "darwin")] // Skip on macOS - this test is for Windows PowerShell behavior
        [InlineData(AGENT_USE_NODE24, "true", NODE24_FOLDER)]
        [InlineData(AGENT_USE_NODE20_1, "true", NODE20_1_FOLDER)]
        [InlineData(AGENT_USE_NODE16, "true", NODE16_FOLDER)]
        public async Task NodeSelection_EnvironmentKnobs_SelectsCorrectVersion_Windows(string knob, string value, string expectedNodeFolder)
        {
            try
            {
                SetupL1();
                ClearNodeEnvironmentVariables();
                Environment.SetEnvironmentVariable(knob, value);
                
                var message = LoadTemplateMessage();
                message.Steps.Clear();
                message.Steps.Add(CreateScriptTask("echo Testing node selection"));

                var results = await RunWorker(message);

                AssertJobCompleted();
                Assert.Equal(TaskResult.Succeeded, results.Result);
                
                var steps = GetSteps();
                var taskStep = steps.FirstOrDefault(s => s.Name == "CmdLine");
                Assert.NotNull(taskStep);

                // On Windows, CmdLine uses PowerShell, so we just verify task completion
                // Node.js environment variables don't affect PowerShell execution
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
        [Trait("SkipOn", "windows")]  // Skip on Windows - uses PowerShell, not Node.js
        public async Task NodeSelection_DefaultBehavior_UsesAppropriateVersion_NonWindows()
        {
            try
            {
                SetupL1();
                ClearNodeEnvironmentVariables();
                
                var message = LoadTemplateMessage();
                message.Steps.Clear();
                message.Steps.Add(CreateScriptTask("echo Testing default node selection"));

                var results = await RunWorker(message);

                AssertJobCompleted();
                Assert.Equal(TaskResult.Succeeded, results.Result);
                
                var steps = GetSteps();
                var taskStep = steps.FirstOrDefault(s => s.Name == "CmdLine");
                Assert.NotNull(taskStep);

                var log = GetTimelineLogLines(taskStep);
                
                // Should have node selection logging showing which version was chosen
                bool hasNodeSelection = log.Any(x => x.Contains(NODE_SELECTION_LOG_PATTERN));
                Assert.True(hasNodeSelection, "Should have node selection logging for default behavior");
                
                // Default should use a compatible node version
                bool usesCompatibleVersion = log.Any(x => x.Contains(NODE_SELECTION_LOG_PATTERN) && 
                    (x.Contains(NODE20_LOG_PATTERN) || x.Contains(NODE16_FOLDER) || x.Contains(NODE24_FOLDER)));
                Assert.True(usesCompatibleVersion, "Default behavior should select a compatible node version");
            }
            finally
            {
                ClearNodeEnvironmentVariables();
                TearDown();
            }
        }

        [Theory]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "windows")]  // Skip on Windows - uses PowerShell, not Node.js
        [InlineData(true)]
        [InlineData(false)]
        public async Task NodeSelection_StrategyVsLegacy_ProducesExpectedBehavior_NonWindows(bool useStrategy)
        {
            try
            {
                SetupL1();
                ClearNodeEnvironmentVariables();
                
                Environment.SetEnvironmentVariable(AGENT_USE_NODE_STRATEGY, useStrategy.ToString().ToLower());
                Environment.SetEnvironmentVariable(AGENT_USE_NODE24, "true");
                
                var message = LoadTemplateMessage();
                message.Steps.Clear();
                message.Steps.Add(CreateScriptTask("echo Testing strategy vs legacy"));

                var results = await RunWorker(message);

                AssertJobCompleted();
                Assert.Equal(TaskResult.Succeeded, results.Result);
                
                var steps = GetSteps();
                var taskStep = steps.FirstOrDefault(s => s.Name == "CmdLine");
                Assert.NotNull(taskStep);

                var log = GetTimelineLogLines(taskStep);

                // Both strategy and legacy should handle this scenario and select node24
                bool hasNodeSelection = log.Any(x => x.Contains(NODE_SELECTION_LOG_PATTERN));
                Assert.True(hasNodeSelection, $"Expected node selection log for {(useStrategy ? "strategy" : "legacy")} mode");

                // Should use node24 based on environment variable
                bool usesNode24 = log.Any(x => x.Contains(NODE_SELECTION_LOG_PATTERN) && x.Contains(NODE24_FOLDER));
                Assert.True(usesNode24, "Should use node24 based on AGENT_USE_NODE24=true");
            }
            finally
            {
                ClearNodeEnvironmentVariables();
                TearDown();
            }
        }

        [Theory]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        [InlineData(AGENT_USE_NODE24, AGENT_USE_NODE20_1, NODE24_FOLDER)] // node24 should win
        [InlineData(AGENT_USE_NODE20_1, AGENT_USE_NODE16, NODE20_1_FOLDER)] // node20_1 should win
        public async Task NodeSelection_ConflictingKnobs_HigherVersionWins(string winningKnob, string losingKnob, string expectedNodeFolder)
        {
            try
            {
                SetupL1();
                
                ClearNodeEnvironmentVariables();
                
                // Set conflicting knobs for this test
                Environment.SetEnvironmentVariable(winningKnob, "true");
                Environment.SetEnvironmentVariable(losingKnob, "true");
                
                var message = LoadTemplateMessage();
                message.Steps.Clear();
                message.Steps.Add(CreateScriptTask("echo Testing conflicting knobs"));

                var results = await RunWorker(message);

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
                    
                    // The key test: should use the higher version when multiple knobs are set
                    // Handle node20_1 special case where logs show "node20" but folder is "node20_1"
                    string expectedLogPattern = expectedNodeFolder == NODE20_1_FOLDER ? NODE20_LOG_PATTERN : expectedNodeFolder;
                    Assert.True(log.Any(x => x.Contains(NODE_SELECTION_LOG_PATTERN) && x.Contains(expectedLogPattern)), 
                        $"Expected conflicting knobs to resolve to '{expectedLogPattern}' (for {expectedNodeFolder}) in: {string.Join("\n", log)}");
                }
            }
            finally
            {
                // Clean up environment variables
                ClearNodeEnvironmentVariables();
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
                SetupL1();
                ClearNodeEnvironmentVariables();
                
                // Enable EOL policy and attempt to use EOL version
                Environment.SetEnvironmentVariable(AGENT_RESTRICT_EOL_NODE_VERSIONS, "true");
                Environment.SetEnvironmentVariable(AGENT_USE_NODE16, "true");
                
                var message = LoadTemplateMessage();
                message.Steps.Clear();
                message.Steps.Add(CreateScriptTask("echo Testing EOL policy"));

                var results = await RunWorker(message);

                AssertJobCompleted();

                var steps = GetSteps();
                var taskStep = steps.FirstOrDefault(s => s.Name == "CmdLine");
                
                // CmdLine uses PowerShell on Windows, Node.js on Linux/macOS
                // Only validate Node.js EOL policy on non-Windows platforms
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Assert.NotNull(taskStep);
                    var log = GetTimelineLogLines(taskStep);
                    
                    // EOL policy should either block Node16 or upgrade to newer version
                    bool hasNodeSelection = log.Any(x => x.Contains(NODE_SELECTION_LOG_PATTERN));
                    
                    if (results.Result == TaskResult.Failed)
                    {
                        // Task failed due to EOL policy restriction
                        string expectedEOLMessage = StringUtil.Loc("NodeEOLPolicyBlocked", "Node16");
                        bool hasEOLPolicyMessage = log.Any(x => x.Contains(expectedEOLMessage));
                        Assert.True(hasEOLPolicyMessage, "Should show EOL policy blocked message for Node16");
                    }
                    else
                    {
                        // Task succeeded - should have upgraded to supported version
                        Assert.True(hasNodeSelection, "Should have node selection logging");
                        
                        bool upgradedToNewer = log.Any(x => x.Contains(NODE_SELECTION_LOG_PATTERN) && 
                            (x.Contains(NODE24_FOLDER) || x.Contains(NODE20_LOG_PATTERN)));
                        Assert.True(upgradedToNewer, 
                            "EOL policy should upgrade Node16 to supported version (Node20 or Node24)");
                        
                        bool stillUsesNode16 = log.Any(x => x.Contains(NODE_SELECTION_LOG_PATTERN) && x.Contains(NODE16_FOLDER));
                        Assert.False(stillUsesNode16, 
                            "Should not use Node16 when EOL policy is enabled");
                    }
                }
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
        public async Task NodeSelection_GlibcFallback_FallsBackToCompatibleVersion()
        {
            try
            {
                SetupL1();
                ClearNodeEnvironmentVariables();
                
                // Test glibc compatibility fallback from Node24
                Environment.SetEnvironmentVariable(AGENT_USE_NODE24, "true");
                
                var message = LoadTemplateMessage();
                message.Steps.Clear();
                message.Steps.Add(CreateScriptTask("echo Testing glibc compatibility"));

                var results = await RunWorker(message);

                AssertJobCompleted();
                Assert.Equal(TaskResult.Succeeded, results.Result);

                var steps = GetSteps();
                var taskStep = steps.FirstOrDefault(s => s.Name == "CmdLine");
                Assert.NotNull(taskStep);

                // Only validate on Linux where glibc compatibility matters
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    var log = GetTimelineLogLines(taskStep);
                    
                    // Should have node selection logging
                    bool hasNodeSelection = log.Any(x => x.Contains(NODE_SELECTION_LOG_PATTERN));
                    Assert.True(hasNodeSelection, "Should have node selection logging");
                    
                    // Should use a glibc-compatible node version
                    bool usedCompatibleNode = log.Any(x => x.Contains(NODE_SELECTION_LOG_PATTERN) && 
                        (x.Contains(NODE24_FOLDER) || x.Contains(NODE20_LOG_PATTERN) || x.Contains(NODE16_FOLDER)));
                    Assert.True(usedCompatibleNode, "Should select a glibc-compatible node version");
                    
                    // Check for glibc fallback warning if Node24 fell back to Node20
                    bool hasNode24ToNode20Fallback = log.Any(x => x.Contains(NODE_SELECTION_LOG_PATTERN) && 
                        x.Contains(NODE20_LOG_PATTERN) && !x.Contains(NODE24_FOLDER));
                    if (hasNode24ToNode20Fallback)
                    {
                        string expectedGlibcWarning = StringUtil.Loc("NodeGlibcFallbackWarning", "agent", "Node24", "Node20");
                        bool hasGlibcWarning = log.Any(x => x.Contains(expectedGlibcWarning));
                        Assert.True(hasGlibcWarning, "Should show glibc fallback warning when falling back from Node24 to Node20");
                    }
                }
            }
            finally
            {
                ClearNodeEnvironmentVariables();
                TearDown();
            }
        }

        /// <summary>
        /// Clears all Node.js-related environment variables to ensure test isolation.
        /// </summary>
        private void ClearNodeEnvironmentVariables()
        {
            Environment.SetEnvironmentVariable(AGENT_USE_NODE24, null);
            Environment.SetEnvironmentVariable(AGENT_USE_NODE20_1, null);
            Environment.SetEnvironmentVariable(AGENT_USE_NODE16, null);
            Environment.SetEnvironmentVariable(AGENT_USE_NODE_STRATEGY, null);
            Environment.SetEnvironmentVariable(AGENT_RESTRICT_EOL_NODE_VERSIONS, null);
            Environment.SetEnvironmentVariable("AGENT_USE_NODE24_TO_START_CONTAINER", null);
            Environment.SetEnvironmentVariable("AGENT_USE_NODE20_TO_START_CONTAINER", null);
            Environment.SetEnvironmentVariable("AGENT_USE_NODE24_WITH_HANDLER_DATA", null);
        }

    }
}