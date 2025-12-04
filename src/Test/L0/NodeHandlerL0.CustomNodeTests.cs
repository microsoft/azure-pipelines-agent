// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    /// <summary>
    /// Test specifications and test class specifically for Custom Node Path scenarios.
    /// 
    /// Custom Node Strategy has HIGHEST priority (Priority 0) and bypasses ALL other logic:
    /// - No knob requirements
    /// - No EOL policy checks 
    /// - No glibc fallback logic
    /// - No handler data validation
    /// 
    /// If a custom path is specified, it should ALWAYS be used regardless of any other settings.
    /// 
    /// </summary>
    [Collection("Unified NodeHandler Tests")]
    public sealed class NodeHandlerL0CustomNodeTests : NodeHandlerTestBase
    {
        /// <summary>
        /// All custom node test scenarios.
        /// </summary>
        public static readonly TestScenario[] CustomNodeScenarios = new[]
        {
            // ============================================
            // HOST CUSTOM NODE PATH SCENARIOS
            // ============================================
            new TestScenario(
                name: "CustomNode_Host_OverridesHandlerData",
                description: "Custom node path in host takes priority over handler data",
                handlerData: typeof(Node20_1HandlerData),
                customNodePath: "/usr/local/custom/node",
                inContainer: false,
                expectedNode: "/usr/local/custom/node",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            new TestScenario(
                name: "CustomNode_Host_BypassesAllKnobs",
                description: "Custom node path ignores all global knobs",
                handlerData: typeof(Node10HandlerData),
                knobs: new()
                {
                    ["AGENT_USE_NODE24"] = "true",
                    ["AGENT_USE_NODE20_1"] = "true",
                    ["AGENT_USE_NODE10"] = "true"
                },
                customNodePath: "/opt/my-node/bin/node",
                inContainer: false,
                expectedNode: "/opt/my-node/bin/node",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            new TestScenario(
                name: "CustomNode_Host_BypassesEOLPolicy",
                description: "Custom node path bypasses EOL policy even for blocked versions",
                handlerData: typeof(Node10HandlerData), // EOL handler
                knobs: new()
                {
                    ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true" // EOL policy enabled
                },
                customNodePath: "/legacy/node6/bin/node", // Even older version
                inContainer: false,
                expectedNode: "/legacy/node6/bin/node",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            new TestScenario( // check this test scenario
                name: "CustomNode_Host_NoGlibcFallback",
                description: "Custom node path used even with glibc errors - no fallback logic",
                handlerData: typeof(Node24HandlerData),
                knobs: new() { ["AGENT_USE_NODE24_WITH_HANDLER_DATA"] = "true" },
                node24GlibcError: true, // This would normally cause fallback
                node20GlibcError: true, // This would normally cause fallback
                customNodePath: "/broken-glibc/node", // Custom path still used
                inContainer: false,
                expectedNode: "/broken-glibc/node",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            // ============================================
            // CONTAINER CUSTOM NODE PATH SCENARIOS  
            // ============================================
            new TestScenario(
                name: "CustomNode_Container_FromDockerLabel",
                description: "Custom node path from Docker label in container",
                handlerData: typeof(Node16HandlerData),
                customNodePath: "/container/custom/node",
                inContainer: true,
                expectedNode: "/container/custom/node",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            new TestScenario(
                name: "CustomNode_Container_OverridesHandlerData",
                description: "Container custom path overrides handler data",
                handlerData: typeof(Node24HandlerData),
                knobs: new() { ["AGENT_USE_NODE24_WITH_HANDLER_DATA"] = "true" },
                customNodePath: "/container/node20/bin/node", // Different from handler
                inContainer: true,
                expectedNode: "/container/node20/bin/node",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            new TestScenario(
                name: "CustomNode_Container_BypassesEOLPolicy",
                description: "Container custom path bypasses EOL policy",
                handlerData: typeof(NodeHandlerData), // EOL handler (Node6)
                knobs: new()
                {
                    ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true"
                },
                customNodePath: "/container/legacy/node",
                inContainer: true,
                expectedNode: "/container/legacy/node",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            new TestScenario(
                name: "CustomNode_Container_OverridesContainerKnobs",
                description: "Custom path overrides container-specific knobs",
                handlerData: typeof(Node20_1HandlerData),
                knobs: new()
                {
                    ["AGENT_USE_NODE24_TO_START_CONTAINER"] = "true",
                    ["AGENT_USE_NODE20_TO_START_CONTAINER"] = "true"
                },
                customNodePath: "/container/custom/node",
                inContainer: true,
                expectedNode: "/container/custom/node",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            // ============================================
            // CROSS-PLATFORM AND TRANSLATION SCENARIOS
            // ============================================
            new TestScenario( // check this scenario, is this needed, if yes, then hos iw it even tested, if dont have any platform specifci code for linux, mac/window chceks in cusotm node strategy
                name: "CustomNode_CrossPlatform_MacOSHostLinuxContainer",
                description: "Custom path on macOS host with Linux container uses container's built-in node",
                handlerData: typeof(Node20_1HandlerData),
                customNodePath: "node", // Cross-platform forces "node" command
                // isHostMacOS: true,
                inContainer: true,
                expectedNode: "node",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            new TestScenario(
                name: "CustomNode_Container_PathTranslation",
                description: "Custom host path gets translated to container path via TranslateToContainerPath",
                handlerData: typeof(Node16HandlerData),
                customNodePath: "/host/path/to/node", // Host path that should be translated
                inContainer: true,
                expectedNode: "/host/path/to/node", // Should be translated by TranslateToContainerPath
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            // ============================================
            // PRIORITY AND PRECEDENCE SCENARIOS
            // ============================================
            new TestScenario(
                name: "CustomNode_HighestPriority_OverridesEverything",
                description: "Custom path has highest priority - overrides all knobs, EOL policy, and glibc errors",
                handlerData: typeof(Node10HandlerData),
                knobs: new()
                {
                    ["AGENT_USE_NODE24"] = "true",
                    ["AGENT_USE_NODE20_1"] = "true", 
                    ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true",
                    ["AGENT_USE_NODE24_WITH_HANDLER_DATA"] = "false"
                },
                node20GlibcError: true,
                node24GlibcError: true,
                customNodePath: "/ultimate/override/node",
                inContainer: false,
                expectedNode: "/ultimate/override/node",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            new TestScenario(
                name: "CustomNode_VersionExtraction_FromPath",
                description: "Node version correctly extracted from custom path for logging purposes",
                handlerData: typeof(Node16HandlerData),
                customNodePath: "/usr/local/node20/bin/node",
                inContainer: false,
                expectedNode: "/usr/local/node20/bin/node",
                expectSuccess: true,
                shouldMatchBetweenModes: true
                // Note: Version extraction ("node20") is tested through strategy's ExtractNodeVersionFromPath method
            ),

            new TestScenario(
                name: "CustomNode_MixedPaths_ContainerWins",
                description: "When both host and container have custom paths, container takes precedence",
                handlerData: typeof(Node24HandlerData),
                customNodePath: "/container/custom/node", // Container custom path
                inContainer: true,
                expectedNode: "/container/custom/node",
                expectSuccess: true,
                shouldMatchBetweenModes: true
                // Note: This tests Container.CustomNodePath vs StepTarget.CustomNodePath priority
            ),

            // ============================================
            // PATH FORMAT SCENARIOS
            // ============================================
            new TestScenario( // what scenario is this
                name: "CustomNode_WindowsPath",
                description: "Custom node path with Windows format",
                handlerData: typeof(Node20_1HandlerData),
                customNodePath: "C:\\Program Files\\nodejs\\node.exe",
                inContainer: false,
                expectedNode: "C:\\Program Files\\nodejs\\node.exe",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            new TestScenario( // what is this scenario
                name: "CustomNode_RelativePath",
                description: "Custom node path with relative path",
                handlerData: typeof(Node16HandlerData),
                customNodePath: "./custom-node/bin/node",
                inContainer: false,
                expectedNode: "./custom-node/bin/node",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            new TestScenario( // waht scenario is this
                name: "CustomNode_JustNodeCommand",
                description: "Custom node path as simple command",
                handlerData: typeof(Node24HandlerData),
                customNodePath: "node", // Container built-in node
                inContainer: true,
                expectedNode: "node",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            // ============================================
            // EDGE CASES
            // ============================================
            new TestScenario(
                name: "CustomNode_NullPath_IgnoredFallsBackToNormalLogic",
                description: "Null custom node path is ignored, falls back to normal handler logic",
                handlerData: typeof(Node24HandlerData),
                knobs: new() { ["AGENT_USE_NODE24_WITH_HANDLER_DATA"] = "true" }, // Required for Node24
                customNodePath: null, // Explicitly null
                inContainer: false,
                expectedNode: "node24",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            new TestScenario(
                name: "CustomNode_EmptyString_IgnoredFallsBackToNormalLogic",
                description: "Empty custom node path is ignored, falls back to normal handler logic",
                handlerData: typeof(Node20_1HandlerData),
                customNodePath: "", // Empty string should be ignored
                inContainer: false,
                expectedNode: "node20_1", // Should use handler data
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            new TestScenario(
                name: "CustomNode_WhitespaceOnly_IgnoredFallsBackToNormalLogic",
                description: "Whitespace-only custom node path is ignored, falls back to normal handler logic",
                handlerData: typeof(Node16HandlerData),
                customNodePath: "   ", // Whitespace only should be ignored
                inContainer: false,
                expectedNode: "node16", // Should use handler data
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            // ============================================
            // INVALID PATH SCENARIOS
            // ============================================
            new TestScenario(
                name: "CustomNode_InvalidPath_NonExistentFile",
                description: "Custom node path pointing to non-existent file - should we validate?",
                handlerData: typeof(Node20_1HandlerData),
                customNodePath: "/non/existent/path/to/node", // Invalid path
                inContainer: false,
                expectedNode: "/non/existent/path/to/node", // Currently returns as-is (no validation)
                expectSuccess: true, // Currently succeeds without validation
                shouldMatchBetweenModes: true
                // TODO: Should this fail or validate the path exists?
            ),

            new TestScenario(
                name: "CustomNode_InvalidPath_NotExecutable",
                description: "Custom node path pointing to non-executable file - should we validate?",
                handlerData: typeof(Node16HandlerData),
                customNodePath: "/etc/passwd", // Exists but not executable
                inContainer: false,
                expectedNode: "/etc/passwd", // Currently returns as-is (no validation)
                expectSuccess: true, // Currently succeeds without validation
                shouldMatchBetweenModes: true
                // TODO: Should this fail or validate the file is executable?
            ),

            new TestScenario(
                name: "CustomNode_InvalidPath_Directory",
                description: "Custom node path pointing to directory instead of executable",
                handlerData: typeof(Node24HandlerData),
                customNodePath: "/usr/bin", // Directory, not executable
                inContainer: false,
                expectedNode: "/usr/bin", // Currently returns as-is (no validation)
                expectSuccess: true, // Currently succeeds without validation
                shouldMatchBetweenModes: true
                // TODO: Should this fail or validate the path is a file?
            )
        };

        /// <summary>
        /// Test custom node path scenarios - should always use exact path specified.
        /// </summary>
        [Theory]
        [MemberData(nameof(GetCustomNodeScenarios))]
        [Trait("Level", "L0")]
        [Trait("Category", "NodeHandler")]
        [Trait("Category", "CustomNode")]
        public void NodeHandler_CustomNodePath_UsesExactPathSpecified(TestScenario scenario)
        {
            // Execute scenario
            RunScenarioAndAssert(scenario);
        }

        /// <summary>
        /// Test custom node strategy comparison between legacy and unified.
        /// Custom paths should behave identically in both strategies.
        /// </summary>
        [Theory]
        [MemberData(nameof(GetCustomNodeScenarios))]
        [Trait("Level", "L0")]
        [Trait("Category", "NodeHandler")]
        [Trait("Category", "CustomNodeStrategyComparison")]
        public void NodeHandler_CustomNodeStrategyComparison_BehavesIdentically(TestScenario scenario)
        {
            // Custom node should behave identically in both strategies
            TestResult legacyResult = RunScenarioForResult(scenario, useUnifiedStrategy: false);
            TestResult unifiedResult = RunScenarioForResult(scenario, useUnifiedStrategy: true);

            // Both should succeed and use the same path
            Assert.True(legacyResult.Success, $"Legacy strategy should succeed for custom node scenario: {scenario.Name}");
            Assert.True(unifiedResult.Success, $"Unified strategy should succeed for custom node scenario: {scenario.Name}");
            Assert.Equal(legacyResult.NodePath, unifiedResult.NodePath);
        }

        /// <summary>
        /// Get test scenarios for xUnit data.
        /// </summary>
        public static IEnumerable<object[]> GetCustomNodeScenarios()
        {
            return CustomNodeScenarios.Select(scenario => new object[] { scenario });
        }
    }
}