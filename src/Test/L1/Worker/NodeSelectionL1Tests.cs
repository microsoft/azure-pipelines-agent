// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
        /// Verifies that node selection process was initiated and logs were generated.
        /// Note: This tests observable behavior through logs since L1 tests run the full worker pipeline.
        /// Unit tests should test the orchestrator/strategy interfaces directly.
        /// </summary>
        private void AssertNodeSelectionAttempted(IEnumerable<string> log, TaskResult result, bool useStrategy, string context = "")
        {
            string modeDescription = $"{(useStrategy ? "strategy" : "legacy")} mode{(string.IsNullOrEmpty(context) ? "" : $" - {context}")}";
            
            bool hasNodeSelection;
            if (useStrategy)
            {
                // Strategy mode: Verify orchestrator was invoked
                hasNodeSelection = log.Any(x => x.Contains("[Host] Selected Node version:") && x.Contains("(Strategy:")) ||
                                   log.Any(x => x.Contains("[Host] Node path:")) ||
                                   log.Any(x => x.Contains("[Host] Starting node version selection")) ||
                                   log.Any(x => x.Contains(NODE_SELECTION_LOG_PATTERN));
            }
            else
            {
                // Legacy mode: Verify traditional node handler was used
                hasNodeSelection = log.Any(x => x.Contains(NODE_SELECTION_LOG_PATTERN));
            }
            
            Assert.True(hasNodeSelection, $"Node selection process should be initiated: {modeDescription}");
            
            Assert.Equal(TaskResult.Succeeded, result);
        }
        
        /// <summary>
        /// Verifies that the expected Node version was selected and is reflected in logs.
        /// This validates the end-to-end selection result in L1 integration testing.
        /// </summary>
        private void AssertNodeSelectionSuccess(IEnumerable<string> log, string expectedPattern, bool useStrategy, string context = "")
        {
            string modeDescription = $"{(useStrategy ? "strategy" : "legacy")} mode{(string.IsNullOrEmpty(context) ? "" : $" - {context}")}";
            
            bool hasExpectedSelection;
            if (useStrategy)
            {
                // Strategy mode: Verify orchestrator selected the expected version
                hasExpectedSelection = log.Any(x => 
                    (x.Contains("[Host] Selected Node version:") || x.Contains("[Host] Node path:") || x.Contains(NODE_SELECTION_LOG_PATTERN)) && 
                    x.Contains(expectedPattern));
            }
            else
            {
                // Legacy mode: Verify legacy handler selected the expected version
                hasExpectedSelection = log.Any(x => x.Contains(NODE_SELECTION_LOG_PATTERN) && x.Contains(expectedPattern));
            }
            
            Assert.True(hasExpectedSelection, $"Expected node selection '{expectedPattern}' should be reflected in execution logs: {modeDescription}");
        }
        
        [Theory]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "windows")]
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
                
                // Note: NODE16 may fallback to newer versions for compatibility
                string expectedLogPattern = expectedNodeFolder == NODE20_1_FOLDER ? NODE20_LOG_PATTERN : expectedNodeFolder;
                
                if (expectedNodeFolder == NODE16_FOLDER)
                {
                    // NODE16 special case: may use requested version or fallback to compatible newer version
                    var usesRequestedOrNewer = log.Any(x => x.Contains(NODE_SELECTION_LOG_PATTERN) && 
                        (x.Contains(NODE16_FOLDER) || x.Contains(NODE20_LOG_PATTERN) || x.Contains(NODE24_FOLDER)));
                    Assert.True(usesRequestedOrNewer, 
                        $"Expected '{expectedNodeFolder}' or compatible fallback - {(useStrategy ? "strategy" : "legacy")} mode");
                }
                else
                {
                    // All other versions should use exact match
                    AssertNodeSelectionSuccess(log, expectedLogPattern, useStrategy, $"{expectedNodeFolder}");
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
        [Trait("SkipOn", "linux")]
        [Trait("SkipOn", "darwin")]
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
                Assert.Equal(TaskResult.Succeeded, results.Result);
                
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
                
                Assert.Equal(TaskResult.Succeeded, results.Result);
                bool usesCompatibleVersion = log.Any(x => x.Contains(NODE_SELECTION_LOG_PATTERN) && 
                    (x.Contains(NODE20_LOG_PATTERN) || x.Contains(NODE16_FOLDER) || x.Contains(NODE24_FOLDER)));
                Assert.True(usesCompatibleVersion, $"Should select compatible node version - {(useStrategy ? "strategy" : "legacy")} mode");
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
                
                Assert.Equal(TaskResult.Succeeded, results.Result);
                
                var steps = GetSteps();
                var taskStep = steps.FirstOrDefault(s => s.Name == "CmdLine");
                Assert.NotNull(taskStep);

                var log = GetTimelineLogLines(taskStep);

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
                Assert.True(hasNodeSelection, $"Expected node selection log for {(useStrategy ? "strategy" : "legacy")} mode");

                bool usesNode24 = log.Any(x => (x.Contains("[Host] Selected Node version:") || 
                                               x.Contains("[Host] Node path:") || 
                                               x.Contains(NODE_SELECTION_LOG_PATTERN)) && 
                                              x.Contains(NODE24_FOLDER));
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
        [Trait("SkipOn", "windows")]
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

                var log = GetTimelineLogLines(taskStep);
                AssertNodeSelectionAttempted(log, results.Result, useStrategy, "conflicting knobs");
                
                string expectedLogPattern = expectedNodeFolder == NODE20_1_FOLDER ? NODE20_LOG_PATTERN : expectedNodeFolder;
                AssertNodeSelectionSuccess(log, expectedLogPattern, useStrategy, "conflicting knobs resolution");
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
        [Trait("SkipOn", "darwin")]
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

                var log = GetTimelineLogLines(taskStep);
                AssertNodeSelectionAttempted(log, results.Result, useStrategy, "glibc compatibility");
                
                var usedCompatibleNode = log.Any(x => x.Contains(NODE_SELECTION_LOG_PATTERN) && 
                    (x.Contains(NODE24_FOLDER) || x.Contains(NODE20_LOG_PATTERN) || x.Contains(NODE16_FOLDER)));
                Assert.True(usedCompatibleNode, $"Should select glibc-compatible node version - {(useStrategy ? "strategy" : "legacy")} mode");
                
                var hasNode24ToNode20Fallback = log.Any(x => x.Contains(NODE_SELECTION_LOG_PATTERN) && 
                    x.Contains(NODE20_LOG_PATTERN) && !x.Contains(NODE24_FOLDER));
                if (hasNode24ToNode20Fallback)
                {
                    string expectedGlibcWarning = StringUtil.Loc("NodeGlibcFallbackWarning", "agent", "Node24", "Node20");
                    var hasGlibcWarning = log.Any(x => x.Contains(expectedGlibcWarning));
                    Assert.True(hasGlibcWarning, $"Should show glibc fallback warning - {(useStrategy ? "strategy" : "legacy")} mode");
                }
            }
            finally
            {
                ClearNodeEnvironmentVariables();
                TearDown();
            }
        }

        private void ClearNodeEnvironmentVariables()
        {
            Environment.SetEnvironmentVariable(AGENT_USE_NODE24, null);
            Environment.SetEnvironmentVariable(AGENT_USE_NODE20_1, null);
            Environment.SetEnvironmentVariable(AGENT_USE_NODE16, null);
            Environment.SetEnvironmentVariable(AGENT_USE_NODE_STRATEGY, null);
            Environment.SetEnvironmentVariable(AGENT_RESTRICT_EOL_NODE_VERSIONS, null);
        }

    }
}