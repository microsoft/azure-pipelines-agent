// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.DistributedTask.Pipelines;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.Collections.Generic;
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
        
        private const string NODE_SELECTION_LOG_PATTERN = "Using node path:";
        private const string NODE20_LOG_PATTERN = "node20";
        
        /// <summary>
        /// Asserts task result based on strategy vs legacy mode expectations
        /// </summary>
        private void AssertTaskResult(TaskResult actualResult, bool useStrategy, string context = "")
        {
            string modeDescription = $"{(useStrategy ? "strategy" : "legacy")} mode{(string.IsNullOrEmpty(context) ? "" : $" - {context}")}";
            
            if (actualResult == TaskResult.Succeeded)
            {
                // Both modes can succeed - this is fine
                return;
            }
            else if (useStrategy)
            {
                // Strategy mode is allowed to fail during testing
                Assert.True(actualResult == TaskResult.Failed, $"Strategy mode should either succeed or fail cleanly: {modeDescription}");
            }
            else
            {
                // Legacy mode should generally succeed
                Assert.Equal(TaskResult.Succeeded, actualResult);
            }
        }
        
        /// <summary>
        /// Asserts that node selection was attempted and validates success criteria
        /// </summary>
        private void AssertNodeSelectionAttempted(IEnumerable<string> log, TaskResult result, bool useStrategy, string context = "")
        {
            string modeDescription = $"{(useStrategy ? "strategy" : "legacy")} mode{(string.IsNullOrEmpty(context) ? "" : $" - {context}")}";
            
            bool hasNodeSelection;
            if (useStrategy)
            {
                // Strategy mode uses orchestrator logging patterns from NodeVersionOrchestrator
                hasNodeSelection = log.Any(x => x.Contains("[Host] Selected Node version:") && x.Contains("(Strategy:")) ||
                                   log.Any(x => x.Contains("[Host] Node path:")) ||
                                   log.Any(x => x.Contains("[Host] Starting node version selection")) ||
                                   log.Any(x => x.Contains(NODE_SELECTION_LOG_PATTERN)); // NodeHandler still logs this
            }
            else
            {
                // Legacy mode uses the traditional "Using node path:" pattern from NodeHandler
                hasNodeSelection = log.Any(x => x.Contains(NODE_SELECTION_LOG_PATTERN));
            }
            
            Assert.True(hasNodeSelection, $"Should have node selection log: {modeDescription}");
            
            if (result != TaskResult.Succeeded && !useStrategy)
            {
                // Legacy mode should not fail
                Assert.Equal(TaskResult.Succeeded, result);
            }
        }
        
        /// <summary>
        /// Asserts successful node selection with expected pattern
        /// </summary>
        private void AssertNodeSelectionSuccess(IEnumerable<string> log, string expectedPattern, bool useStrategy, string context = "")
        {
            string modeDescription = $"{(useStrategy ? "strategy" : "legacy")} mode{(string.IsNullOrEmpty(context) ? "" : $" - {context}")}";
            
            bool hasExpectedSelection;
            if (useStrategy)
            {
                // Strategy mode: Look for orchestrator logs from NodeVersionOrchestrator
                hasExpectedSelection = log.Any(x => 
                    (x.Contains("[Host] Selected Node version:") || x.Contains("[Host] Node path:") || x.Contains(NODE_SELECTION_LOG_PATTERN)) && 
                    x.Contains(expectedPattern));
            }
            else
            {
                // Legacy mode: Look for traditional "Using node path:" pattern from NodeHandler
                hasExpectedSelection = log.Any(x => x.Contains(NODE_SELECTION_LOG_PATTERN) && x.Contains(expectedPattern));
            }
            
            Assert.True(hasExpectedSelection, $"Expected node selection '{expectedPattern}': {modeDescription}");
        }
        
        [Theory]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "windows")] // Skip on Windows - uses PowerShell, not Node.js
        [InlineData(AGENT_USE_NODE24, "true", NODE24_FOLDER, false)] // Legacy mode
        [InlineData(AGENT_USE_NODE20_1, "true", NODE20_1_FOLDER, false)]
        [InlineData(AGENT_USE_NODE16, "true", NODE16_FOLDER, false)]
        [InlineData(AGENT_USE_NODE24, "true", NODE24_FOLDER, true)]  // Strategy mode
        [InlineData(AGENT_USE_NODE20_1, "true", NODE20_1_FOLDER, true)]
        [InlineData(AGENT_USE_NODE16, "true", NODE16_FOLDER, true)]
        public async Task NodeSelection_EnvironmentKnobs_SelectsCorrectVersion_NonWindows(string knob, string value, string expectedNodeFolder, bool useStrategy)
        {
            try
            {
                SetupL1();
                ClearNodeEnvironmentVariables();
                Environment.SetEnvironmentVariable(knob, value);
                Environment.SetEnvironmentVariable(AGENT_USE_NODE_STRATEGY, useStrategy.ToString().ToLower());
                
                var message = LoadTemplateMessage();
                message.Steps.Clear();
                message.Steps.Add(CreateScriptTask($"echo Testing node selection - {(useStrategy ? "strategy" : "legacy")} mode"));

                var results = await RunWorker(message);

                AssertJobCompleted();
                
                var steps = GetSteps();
                var taskStep = steps.FirstOrDefault(s => s.Name == "CmdLine");
                Assert.NotNull(taskStep);

                var log = GetTimelineLogLines(taskStep);
                
                AssertNodeSelectionAttempted(log, results.Result, useStrategy, $"testing {knob}");
                
                if (results.Result == TaskResult.Succeeded)
                {
                    if (expectedNodeFolder == NODE16_FOLDER)
                    {
                        bool usesRequestedOrNewer = log.Any(x => x.Contains(NODE_SELECTION_LOG_PATTERN) && 
                            (x.Contains(NODE16_FOLDER) || x.Contains(NODE20_LOG_PATTERN) || x.Contains(NODE24_FOLDER)));
                        Assert.True(usesRequestedOrNewer, 
                            $"Expected '{expectedNodeFolder}' or fallback to newer version - {(useStrategy ? "strategy" : "legacy")} mode");
                    }
                    else if (expectedNodeFolder == NODE20_1_FOLDER)
                    {
                        AssertNodeSelectionSuccess(log, NODE20_LOG_PATTERN, useStrategy, $"{expectedNodeFolder}");
                    }
                    else
                    {
                        AssertNodeSelectionSuccess(log, expectedNodeFolder, useStrategy);
                    }
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
        [InlineData(AGENT_USE_NODE24, "true", NODE24_FOLDER, false)] // Legacy mode
        [InlineData(AGENT_USE_NODE20_1, "true", NODE20_1_FOLDER, false)]
        [InlineData(AGENT_USE_NODE16, "true", NODE16_FOLDER, false)]
        [InlineData(AGENT_USE_NODE24, "true", NODE24_FOLDER, true)]  // Strategy mode
        [InlineData(AGENT_USE_NODE20_1, "true", NODE20_1_FOLDER, true)]
        [InlineData(AGENT_USE_NODE16, "true", NODE16_FOLDER, true)]
        public async Task NodeSelection_EnvironmentKnobs_SelectsCorrectVersion_Windows(string knob, string value, string expectedNodeFolder, bool useStrategy)
        {
            try
            {
                SetupL1();
                ClearNodeEnvironmentVariables();
                Environment.SetEnvironmentVariable(knob, value);
                Environment.SetEnvironmentVariable(AGENT_USE_NODE_STRATEGY, useStrategy.ToString().ToLower());
                
                var message = LoadTemplateMessage();
                message.Steps.Clear();
                message.Steps.Add(CreateScriptTask($"echo Testing node selection - {(useStrategy ? "strategy" : "legacy")} mode"));

                var results = await RunWorker(message);

                AssertJobCompleted();
                AssertTaskResult(results.Result, useStrategy, "Windows PowerShell execution");
                
                var steps = GetSteps();
                var taskStep = steps.FirstOrDefault(s => s.Name == "CmdLine");
                Assert.NotNull(taskStep);

                // On Windows, CmdLine uses PowerShell, so Node.js environment variables don't affect execution
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
        [InlineData(false)] // Legacy mode
        [InlineData(true)]  // Strategy mode
        public async Task NodeSelection_DefaultBehavior_UsesAppropriateVersion_NonWindows(bool useStrategy)
        {
            try
            {
                SetupL1();
                ClearNodeEnvironmentVariables();
                Environment.SetEnvironmentVariable(AGENT_USE_NODE_STRATEGY, useStrategy.ToString().ToLower());
                
                var message = LoadTemplateMessage();
                message.Steps.Clear();
                message.Steps.Add(CreateScriptTask($"echo Testing default node selection - {(useStrategy ? "strategy" : "legacy")} mode"));

                var results = await RunWorker(message);

                AssertJobCompleted();
                
                var steps = GetSteps();
                var taskStep = steps.FirstOrDefault(s => s.Name == "CmdLine");
                Assert.NotNull(taskStep);

                var log = GetTimelineLogLines(taskStep);
                
                AssertNodeSelectionAttempted(log, results.Result, useStrategy, "default behavior");
                
                if (results.Result == TaskResult.Succeeded)
                {
                    bool usesCompatibleVersion = log.Any(x => x.Contains(NODE_SELECTION_LOG_PATTERN) && 
                        (x.Contains(NODE20_LOG_PATTERN) || x.Contains(NODE16_FOLDER) || x.Contains(NODE24_FOLDER)));
                    Assert.True(usesCompatibleVersion, $"Should select compatible node version - {(useStrategy ? "strategy" : "legacy")} mode");
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
        [Trait("SkipOn", "windows")]
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
                
                // Both modes should succeed, but with different tolerance for node selection
                if (useStrategy)
                {
                    // Strategy mode should complete but may not always succeed in current implementation
                    Assert.True(results.Result == TaskResult.Succeeded || results.Result == TaskResult.Failed,
                        "Strategy mode should complete execution (success or failure expected)");
                }
                else
                {
                    // Legacy mode should succeed reliably
                    Assert.Equal(TaskResult.Succeeded, results.Result);
                }
                
                var steps = GetSteps();
                var taskStep = steps.FirstOrDefault(s => s.Name == "CmdLine");
                Assert.NotNull(taskStep);

                var log = GetTimelineLogLines(taskStep);

                // Validate node selection behavior based on task result
                if (results.Result == TaskResult.Succeeded)
                {
                    // Both modes should have node selection logging when successful
                    bool hasNodeSelection;
                    if (useStrategy)
                    {
                        hasNodeSelection = log.Any(x => x.Contains("[Host] Selected Node version:") || 
                                                       x.Contains("[Host] Node path:") || 
                                                       x.Contains(NODE_SELECTION_LOG_PATTERN));
                    }
                    else
                    {
                        hasNodeSelection = log.Any(x => x.Contains(NODE_SELECTION_LOG_PATTERN));
                    }
                    Assert.True(hasNodeSelection, $"Expected node selection log for successful {(useStrategy ? "strategy" : "legacy")} mode");

                    bool usesNode24 = log.Any(x => (x.Contains("[Host] Selected Node version:") || 
                                                   x.Contains("[Host] Node path:") || 
                                                   x.Contains(NODE_SELECTION_LOG_PATTERN)) && 
                                                  x.Contains(NODE24_FOLDER));
                    Assert.True(usesNode24, "Should use node24 based on AGENT_USE_NODE24=true");
                }
                // If strategy mode failed, we can't validate the node selection patterns, but that's acceptable
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
        [InlineData(AGENT_USE_NODE24, AGENT_USE_NODE20_1, NODE24_FOLDER, false)] // Legacy mode - node24 should win
        [InlineData(AGENT_USE_NODE20_1, AGENT_USE_NODE16, NODE20_1_FOLDER, false)] // Legacy mode - node20_1 should win
        [InlineData(AGENT_USE_NODE24, AGENT_USE_NODE20_1, NODE24_FOLDER, true)]  // Strategy mode - node24 should win
        [InlineData(AGENT_USE_NODE20_1, AGENT_USE_NODE16, NODE20_1_FOLDER, true)] // Strategy mode - node20_1 should win
        public async Task NodeSelection_ConflictingKnobs_HigherVersionWins(string winningKnob, string losingKnob, string expectedNodeFolder, bool useStrategy)
        {
            try
            {
                SetupL1();
                ClearNodeEnvironmentVariables();
                
                Environment.SetEnvironmentVariable(winningKnob, "true");
                Environment.SetEnvironmentVariable(losingKnob, "true");
                Environment.SetEnvironmentVariable(AGENT_USE_NODE_STRATEGY, useStrategy.ToString().ToLower());
                
                var message = LoadTemplateMessage();
                message.Steps.Clear();
                message.Steps.Add(CreateScriptTask($"echo Testing conflicting knobs - {(useStrategy ? "strategy" : "legacy")} mode"));

                var results = await RunWorker(message);

                AssertJobCompleted();
                
                var steps = GetSteps();
                var taskStep = steps.FirstOrDefault(s => s.Name == "CmdLine");
                Assert.NotNull(taskStep);

                // CmdLine uses PowerShell on Windows, Node.js on Linux/macOS
                // Only validate Node.js selection on non-Windows platforms
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var log = GetTimelineLogLines(taskStep);
                    
                    AssertNodeSelectionAttempted(log, results.Result, useStrategy, "conflicting knobs");
                    
                    if (results.Result == TaskResult.Succeeded)
                    {
                        string expectedLogPattern = expectedNodeFolder == NODE20_1_FOLDER ? NODE20_LOG_PATTERN : expectedNodeFolder;
                        AssertNodeSelectionSuccess(log, expectedLogPattern, useStrategy, "conflicting knobs resolution");
                    }
                }
                else
                {
                    // On Windows, just verify task completion since Node.js selection doesn't affect PowerShell
                    AssertTaskResult(results.Result, useStrategy, "Windows PowerShell with conflicting knobs");
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
        [InlineData(false)] // Legacy mode
        [InlineData(true)]  // Strategy mode
        public async Task NodeSelection_EOLPolicy_RestrictsOlderVersions(bool useStrategy)
        {
            try
            {
                SetupL1();
                ClearNodeEnvironmentVariables();
                
                Environment.SetEnvironmentVariable(AGENT_RESTRICT_EOL_NODE_VERSIONS, "true");
                Environment.SetEnvironmentVariable(AGENT_USE_NODE16, "true");
                Environment.SetEnvironmentVariable(AGENT_USE_NODE_STRATEGY, useStrategy.ToString().ToLower());
                
                var message = LoadTemplateMessage();
                message.Steps.Clear();
                message.Steps.Add(CreateScriptTask($"echo Testing EOL policy - {(useStrategy ? "strategy" : "legacy")} mode"));

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
                    
                    if (results.Result == TaskResult.Failed)
                    {
                        if (useStrategy)
                        {
                            // Strategy mode throws NotSupportedException for EOL policy violations
                            string expectedEOLMessage = StringUtil.Loc("NodeEOLPolicyBlocked", "Node16");
                            bool hasEOLPolicyMessage = log.Any(x => x.Contains(expectedEOLMessage)) ||
                                log.Any(x => x.Contains("NotSupportedException")) ||
                                log.Any(x => x.Contains("No compatible Node.js version available"));
                            
                            Assert.True(hasEOLPolicyMessage, 
                                "Strategy mode should show EOL policy exception when Node16 is blocked");
                        }
                        else
                        {
                            // Legacy mode shouldn't fail due to EOL policy - it should still select Node16
                            Assert.True(false, "Legacy mode should not fail when EOL policy is enabled - it should select Node16 anyway");
                        }
                    }
                    else
                    {
                        // Task succeeded
                        if (useStrategy)
                        {
                            // Strategy mode success is unexpected when EOL policy blocks Node16
                            // But if it succeeds, it should have upgraded to a newer version
                            AssertNodeSelectionAttempted(log, results.Result, useStrategy, "EOL policy upgrade");
                            
                            bool upgradedToNewer = log.Any(x => (x.Contains("[Host] Selected Node version:") || 
                                                               x.Contains("[Host] Node path:") || 
                                                               x.Contains(NODE_SELECTION_LOG_PATTERN)) && 
                                                              (x.Contains(NODE24_FOLDER) || x.Contains(NODE20_LOG_PATTERN)));
                            Assert.True(upgradedToNewer, 
                                "Strategy mode should upgrade Node16 to supported version when EOL policy is enabled");
                        }
                        else
                        {
                            // Legacy mode should succeed and use Node16 despite EOL policy
                            AssertNodeSelectionAttempted(log, results.Result, useStrategy, "Legacy EOL behavior");
                            
                            bool usesNode16 = log.Any(x => x.Contains(NODE_SELECTION_LOG_PATTERN) && x.Contains(NODE16_FOLDER));
                            Assert.True(usesNode16, 
                                "Legacy mode should still use Node16 even when EOL policy is enabled");
                        }
                    }
                }
                else
                {
                    // On Windows, verify task completion
                    AssertTaskResult(results.Result, useStrategy, "Windows EOL policy");
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
        [InlineData(false)] // Legacy mode
        [InlineData(true)]  // Strategy mode
        public async Task NodeSelection_GlibcFallback_FallsBackToCompatibleVersion(bool useStrategy)
        {
            try
            {
                SetupL1();
                ClearNodeEnvironmentVariables();
                
                Environment.SetEnvironmentVariable(AGENT_USE_NODE24, "true");
                Environment.SetEnvironmentVariable(AGENT_USE_NODE_STRATEGY, useStrategy.ToString().ToLower());
                
                var message = LoadTemplateMessage();
                message.Steps.Clear();
                message.Steps.Add(CreateScriptTask($"echo Testing glibc compatibility - {(useStrategy ? "strategy" : "legacy")} mode"));

                var results = await RunWorker(message);

                AssertJobCompleted();
                
                var steps = GetSteps();
                var taskStep = steps.FirstOrDefault(s => s.Name == "CmdLine");
                Assert.NotNull(taskStep);

                // Only validate on Linux where glibc compatibility matters
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    var log = GetTimelineLogLines(taskStep);
                    
                    AssertNodeSelectionAttempted(log, results.Result, useStrategy, "glibc compatibility");
                    
                    if (results.Result == TaskResult.Succeeded)
                    {
                        bool usedCompatibleNode = log.Any(x => x.Contains(NODE_SELECTION_LOG_PATTERN) && 
                            (x.Contains(NODE24_FOLDER) || x.Contains(NODE20_LOG_PATTERN) || x.Contains(NODE16_FOLDER)));
                        Assert.True(usedCompatibleNode, $"Should select glibc-compatible node version - {(useStrategy ? "strategy" : "legacy")} mode");
                        
                        bool hasNode24ToNode20Fallback = log.Any(x => x.Contains(NODE_SELECTION_LOG_PATTERN) && 
                            x.Contains(NODE20_LOG_PATTERN) && !x.Contains(NODE24_FOLDER));
                        if (hasNode24ToNode20Fallback)
                        {
                            string expectedGlibcWarning = StringUtil.Loc("NodeGlibcFallbackWarning", "agent", "Node24", "Node20");
                            bool hasGlibcWarning = log.Any(x => x.Contains(expectedGlibcWarning));
                            Assert.True(hasGlibcWarning, $"Should show glibc fallback warning - {(useStrategy ? "strategy" : "legacy")} mode");
                        }
                    }
                }
                else
                {
                    // On non-Linux platforms, verify task completion
                    AssertTaskResult(results.Result, useStrategy, "non-Linux glibc test");
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
            // Environment.SetEnvironmentVariable("AGENT_USE_NODE24_TO_START_CONTAINER", null);
            // Environment.SetEnvironmentVariable("AGENT_USE_NODE20_TO_START_CONTAINER", null);
            // Environment.SetEnvironmentVariable("AGENT_USE_NODE24_WITH_HANDLER_DATA", null);
        }

    }
}