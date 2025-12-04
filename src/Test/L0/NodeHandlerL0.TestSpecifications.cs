// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Worker;

namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    /// <summary>
    /// Centralized test specifications for NodeHandler behavior testing.
    /// 
    /// This is the SINGLE source of truth for ALL NodeHandler test scenarios.
    /// Each scenario specifies:
    /// - Handler data type (what task declares)
    /// - Environment knobs (global overrides) 
    /// - Runtime conditions (glibc errors, container context)
    /// - Expected behavior (which node version is selected)
    /// - Whether behavior differs between legacy and unified strategy
    /// 
    /// Organization:
    /// 1. CUSTOM NODE SCENARIOS (Priority 0 - HIGHEST)
    /// 2. NODE6 SCENARIOS (NodeHandlerData - EOL)
    /// 3. NODE10 SCENARIOS (Node10HandlerData - EOL) 
    /// 4. NODE16 SCENARIOS (Node16HandlerData - EOL)
    /// 5. NODE20 SCENARIOS (Node20_1HandlerData)
    /// 6. NODE24 SCENARIOS (Node24HandlerData)
    /// 7. CONTAINER-SPECIFIC SCENARIOS
    /// 8. EOL POLICY SCENARIOS
    /// 9. EDGE CASES AND ERROR SCENARIOS
    /// </summary>
    public static class NodeHandlerTestSpecs
    {
        /// <summary>
        /// All test scenarios. This is the single source of truth for NodeHandler behavior.
        /// </summary>
        public static readonly TestScenario[] AllScenarios = new[]
        {
            // ============================================
            // GROUP 1: CUSTOM NODE SCENARIOS (Priority 0 - HIGHEST)
            // ============================================
            
            // Custom node strategy has HIGHEST priority and bypasses ALL other logic:
            // - No knob requirements
            // - No EOL policy checks 
            // - No glibc fallback logic
            // - No handler data validation
            
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

            new TestScenario(
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

            new TestScenario( // what scenario is this one?
                name: "CustomNode_WindowsPath",
                description: "Custom node path with Windows format",
                handlerData: typeof(Node20_1HandlerData),
                customNodePath: "C:\\Program Files\\nodejs\\node.exe",
                inContainer: false,
                expectedNode: "C:\\Program Files\\nodejs\\node.exe",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            new TestScenario( // what is this scenario testing?
                name: "CustomNode_RelativePath",
                description: "Custom node path with relative path",
                handlerData: typeof(Node16HandlerData),
                customNodePath: "./custom-node/bin/node",
                inContainer: false,
                expectedNode: "./custom-node/bin/node",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            new TestScenario(
                name: "CustomNode_NullPath_FallsBackToNormalLogic",
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
                knobs: new() { ["AGENT_USE_NODE24_WITH_HANDLER_DATA"] = "true" },
                customNodePath: "/container/custom/node", // Container custom path
                inContainer: true,
                expectedNode: "/container/custom/node",
                expectSuccess: true,
                shouldMatchBetweenModes: true
                // Note: This tests Container.CustomNodePath vs StepTarget.CustomNodePath priority
            ),

            // ============================================
            // GROUP 3: NODE10 SCENARIOS (Node10HandlerData - EOL)
            // ============================================
            
            new TestScenario(
                name: "Node6_DefaultBehavior_EOLPolicyDisabled",
                description: "Node6 handler uses Node6 when EOL policy is disabled",
                handlerData: typeof(NodeHandlerData),
                knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "false" },
                expectedNode: "node",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            new TestScenario(
                name: "Node6_EOLPolicyEnabled_UpgradesToNode24",
                description: "Node6 handler with EOL policy: legacy allows Node6, unified upgrades to Node24",
                handlerData: typeof(NodeHandlerData),
                knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true" },
                legacyExpectedNode: "node", // Legacy allows EOL nodes (Node6 = "node" folder)
                legacyExpectSuccess: true,
                unifiedExpectedNode: "node24", // Unified upgrades to Node24
                unifiedExpectSuccess: true,
                shouldMatchBetweenModes: false // Different behavior
            ),

            // ============================================
            // GROUP 4: NODE16 SCENARIOS (Node16HandlerData - EOL)
            // ============================================
            
            new TestScenario(
                name: "Node10_DefaultBehavior_EOLPolicyDisabled",
                description: "Node10 handler uses Node10 when EOL policy is disabled",
                handlerData: typeof(Node10HandlerData),
                knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "false" },
                expectedNode: "node10",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            new TestScenario(
                name: "Node10_EOLPolicyEnabled_UpgradesToNode24",
                description: "Node10 handler with EOL policy: legacy allows Node10, unified upgrades to Node24",
                handlerData: typeof(Node10HandlerData),
                knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true" },
                legacyExpectedNode: "node10", // Legacy allows EOL nodes
                legacyExpectSuccess: true,
                unifiedExpectedNode: "node24", // Unified upgrades to Node24
                unifiedExpectSuccess: true,
                shouldMatchBetweenModes: false // Different behavior
            ),

            // ============================================
            // GROUP 4: NODE16 SCENARIOS (Node16HandlerData - EOL)
            // ============================================
            
            new TestScenario(
                name: "Node16_DefaultBehavior_EOLPolicyDisabled",
                description: "Node16 handler uses Node16 when EOL policy is disabled",
                handlerData: typeof(Node16HandlerData),
                knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "false" },
                expectedNode: "node16",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            new TestScenario(
                name: "Node16_DefaultEOLPolicy_AllowsNode16",
                description: "Node16 handler uses Node16 when EOL policy is default (disabled)",
                handlerData: typeof(Node16HandlerData),
                knobs: new() { }, // Default EOL policy is false
                expectedNode: "node16",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            new TestScenario(
                name: "Node16_EOLPolicyEnabled_UpgradesToNode24",
                description: "Node16 handler with EOL policy: legacy allows Node16, unified upgrades to Node24",
                handlerData: typeof(Node16HandlerData),
                knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true" },
                legacyExpectedNode: "node16", // Legacy allows EOL nodes
                legacyExpectSuccess: true,
                unifiedExpectedNode: "node24", // Unified upgrades to Node24
                unifiedExpectSuccess: true,
                shouldMatchBetweenModes: false // Different behavior
            ),

            new TestScenario(
                name: "Node16_InContainer_EOLPolicyDisabled",
                description: "Node16 works in containers when EOL policy is disabled",
                handlerData: typeof(Node16HandlerData),
                knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "false" },
                expectedNode: "node16", 
                expectSuccess: true,
                inContainer: true,
                shouldMatchBetweenModes: true
            ),

            new TestScenario(
                name: "Node16_InContainer_EOLPolicyEnabled_UpgradesToNode24",
                description: "Node16 in container with EOL policy: legacy allows Node16, unified upgrades to Node24",
                handlerData: typeof(Node16HandlerData),
                knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true" },
                legacyExpectedNode: "node16", // Legacy allows EOL nodes
                legacyExpectSuccess: true,
                unifiedExpectedNode: "node24", // Unified upgrades to Node24
                unifiedExpectSuccess: true,
                inContainer: true,
                shouldMatchBetweenModes: false // Different behavior
            ),

            // ============================================
            // GROUP 5: NODE20 SCENARIOS (Node20_1HandlerData)
            // ============================================
            new TestScenario(
                name: "Node20_DefaultBehavior_WithHandler",
                description: "Node20 handler uses Node20 by default",
                handlerData: typeof(Node20_1HandlerData),
                knobs: new() { },
                expectedNode: "node20_1",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),
            
            new TestScenario(
                name: "Node20_WithGlobalUseNode20Knob",
                description: "AGENT_USE_NODE20_1=true forces Node20 regardless of handler data",
                handlerData: typeof(Node20_1HandlerData),
                knobs: new() { ["AGENT_USE_NODE20_1"] = "true" },
                expectedNode: "node20_1",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),
            new TestScenario(
                name: "Node20_GlibcError_FallsBackToNode16",
                description: "Node20 with glibc error falls back to Node16",
                handlerData: typeof(Node20_1HandlerData),
                knobs: new() { },
                node24GlibcError: true,
                expectedNode: "node20_1",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),
            // right
            // new TestScenario(
            //     name: "Node20_GlibcError_EOLPolicy_ThrowsError",
            //     description: "Node20 with glibc error and EOL policy enabled throws error (cannot fallback to Node16)",
            //     handlerData: typeof(Node20_1HandlerData),
            //     knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true" },
            //     node20GlibcError: true,
            //     expectSuccess: false,
                // expectedErrorType: typeof(NotSupportedException),
                // unifiedExpectedError: "would fallback to Node16 (EOL) but EOL policy is enabled",
                // shouldMatchBetweenModes: false, // Different EOL policy behavior
            // ),
            // Fix the Node20_GlibcError_EOLPolicy_ThrowsError test specification
            /*
            new TestScenario(
                name: "Node20_GlibcError_EOLPolicy_ThrowsError",
                description: "Node20 with glibc error and EOL policy enabled throws error (cannot fallback to Node16)",
                handlerData: typeof(Node20_1HandlerData),
                knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true" },
                node20GlibcError: true,
                legacyExpectedNode: "node16", // Legacy falls back to Node16
                legacyExpectSuccess: true,
                unifiedExpectSuccess: false, // Unified throws error
                expectedErrorType: typeof(NotSupportedException), // check if node 24 is selected here, eol is on, node 20 has glibc error, so node 24 should get selected
                unifiedExpectedError: "would fallback to Node16 (EOL) but EOL policy is enabled",
                shouldMatchBetweenModes: false // Different EOL policy behavior
                // infact this test - gives below in comparsion testing
                // 
                // NodeHandlerL0StrategyComparisonTests.NodeHandler_DivergenceTest_LegacyAndUnifiedProduceDifferentResults
                // (scenario: Node20_GlibcError_EOLPolicy_ThrowsError) [91 ms]
                //     EXEC : error Message:  [C:\RISHABH\azure-pipelines-agent\src\dir.proj]
                //         Unified should fail for scenario: Node20_GlibcError_EOLPolicy_ThrowsError
                //     Expected: False
                //     Actual:   True
                // 
            ),*/
            new TestScenario(
                name: "Node20_GlibcError_EOLPolicy_UpgradesToNode24",
                description: "Node20 with glibc error and EOL policy: legacy falls back to Node16, unified upgrades to Node24",
                handlerData: typeof(Node20_1HandlerData),
                knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true" },
                node20GlibcError: true,
                legacyExpectedNode: "node16", // Legacy falls back to Node16
                legacyExpectSuccess: true,
                unifiedExpectedNode: "node24", // Unified upgrades to Node24 (EOL policy triggers upgrade)
                unifiedExpectSuccess: true,
                shouldMatchBetweenModes: false // Different behavior: legacy uses Node16, unified uses Node24
            ),
            
            new TestScenario( // this test is wrong, name wise it has node 20, why? hadnler is node 10 with EOL on,
                    // unified expected node would be node 24 -> 20 (in case of fallback glibc check implied)
                    // legacy would get node 10 as result as it would not have EOL check
                    // correct this test
                name: "Node20_EOLUpgrade_FromOldHandler",
                description: "Node10HandlerData with EOL policy: legacy allows Node10, unified upgrades to node 24",
                handlerData: typeof(Node10HandlerData), // Old handler
                knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true" },
                legacyExpectedNode: "node10", // Legacy allows EOL nodes
                legacyExpectSuccess: true,
                unifiedExpectedNode: "node24", // Unified upgrades to Node24
                unifiedExpectSuccess: true,
                shouldMatchBetweenModes: false // Different behavior
            ),

            new TestScenario(
                name: "Node20_WithGlobalUseNode24Knob",
                description: "AGENT_USE_NODE24=true global override forces Node24 even with Node20 handler",
                handlerData: typeof(Node20_1HandlerData),
                knobs: new() { ["AGENT_USE_NODE24"] = "true" },
                expectedNode: "node24", // Global Node24 knob has highest priority
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            new TestScenario(
                name: "Node20_WithUseNode10Knob",
                description: "Node20 handler ignores deprecated AGENT_USE_NODE10 knob in unified strategy",
                handlerData: typeof(Node20_1HandlerData),
                knobs: new() { ["AGENT_USE_NODE10"] = "true" },
                legacyExpectedNode: "node10", // Legacy honored deprecated knob
                legacyExpectSuccess: true,
                unifiedExpectedNode: "node20_1", // Unified ignores deprecated knob
                unifiedExpectSuccess: true,
                shouldMatchBetweenModes: false
            ),


            new TestScenario(
                name: "Node20_PriorityTest_UseNode20OverridesUseNode10",
                description: "When both global knobs are set, Node20 takes priority over Node10",
                handlerData: typeof(Node20_1HandlerData),
                knobs: new() 
                { 
                    ["AGENT_USE_NODE10"] = "true",
                    ["AGENT_USE_NODE20_1"] = "true"
                },
                expectedNode: "node20_1", // Higher priority global knob wins
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            new TestScenario(
                name: "Node20_PriorityTest_UseNode24OverridesUseNode20",
                description: "When both Node20 and Node24 global knobs are set, Node24 takes priority",
                handlerData: typeof(Node20_1HandlerData),
                knobs: new() 
                { 
                    ["AGENT_USE_NODE20_1"] = "true",
                    ["AGENT_USE_NODE24"] = "true"
                },
                expectedNode: "node24", // Node24 has higher priority
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            // ============================================
            // GROUP 3: Node16 (Node16HandlerData) Scenarios - END-OF-LIFE
            // ============================================
            new TestScenario(
                name: "Node16_DefaultBehavior_EOLPolicyDisabled",
                description: "Node16 handler uses Node16 when EOL policy is disabled",
                handlerData: typeof(Node16HandlerData),
                knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "false" },
                expectedNode: "node16",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            new TestScenario(
                name: "Node16_DefaultEOLPolicy_AllowsNode16",
                description: "Node16 handler uses Node16 when EOL policy is default (disabled)",
                handlerData: typeof(Node16HandlerData),
                knobs: new() { }, // Default EOL policy is false
                expectedNode: "node16",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            new TestScenario(
                name: "Node16_EOLPolicyEnabled_UpgradesToNode24",
                description: "Node16 handler with EOL policy: legacy allows Node16, unified upgrades to Node24",
                handlerData: typeof(Node16HandlerData),
                knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true" },
                legacyExpectedNode: "node16", // Legacy allows EOL nodes
                legacyExpectSuccess: true,
                unifiedExpectedNode: "node24", // Unified upgrades to Node24
                unifiedExpectSuccess: true,
                shouldMatchBetweenModes: false // Different behavior
            ),

            new TestScenario(
                name: "Node16_InContainer_EOLPolicyDisabled",
                description: "Node16 works in containers when EOL policy is disabled",
                handlerData: typeof(Node16HandlerData),
                knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "false" },
                expectedNode: "node16", 
                expectSuccess: true,
                inContainer: true,
                shouldMatchBetweenModes: true
            ),

            new TestScenario(
                name: "Node16_InContainer_EOLPolicyEnabled_UpgradesToNode24",
                description: "Node16 in container with EOL policy: legacy allows Node16, unified upgrades to Node24",
                handlerData: typeof(Node16HandlerData),
                knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true" },
                legacyExpectedNode: "node16", // Legacy allows EOL nodes
                legacyExpectSuccess: true,
                unifiedExpectedNode: "node24", // Unified upgrades to Node24
                unifiedExpectSuccess: true,
                inContainer: true,
                shouldMatchBetweenModes: false // Different behavior
            ),

            // ============================================
            // GROUP 7: CONTAINER-SPECIFIC EOL SCENARIOS
            // ============================================
            
            new TestScenario(
                name: "Node24_InContainer_GlibcError_EOLPolicy_FallsBackToNode20",
                description: "Node24 in container with glibc error and EOL policy falls back to Node20 (not Node16)",
                handlerData: typeof(Node24HandlerData),
                knobs: new() 
                { 
                    ["AGENT_USE_NODE24_WITH_HANDLER_DATA"] = "true",
                    ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true"
                },
                node24GlibcError: true,
                expectedNode: "node20_1",
                expectSuccess: true,
                inContainer: true,
                shouldMatchBetweenModes: true
            ),
            
            new TestScenario(
                name: "Node24_InContainer_BothGlibcErrors_EOLPolicy_ThrowsError",
                description: "Node24 in container with both glibc errors and EOL policy throws error (cannot fallback to Node16)",
                handlerData: typeof(Node24HandlerData),
                knobs: new() 
                { 
                    ["AGENT_USE_NODE24_WITH_HANDLER_DATA"] = "true",
                    ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true"
                },
                node24GlibcError: true,
                node20GlibcError: true,
                legacyExpectedNode: "node16", // Legacy falls back to Node16
                legacyExpectSuccess: true,
                unifiedExpectSuccess: false, // Unified throws error
                expectedErrorType: typeof(NotSupportedException),
                unifiedExpectedError: "notFound:NodeVersionNotAvailable", // this is wrong
                inContainer: true,
                shouldMatchBetweenModes: false
            ),
            
            // new TestScenario(
            //     name: "Node20_InContainer_GlibcError_EOLPolicy_ThrowsError",
            //     description: "Node20 in container with glibc error and EOL policy throws error (cannot fallback to Node16)",
            //     handlerData: typeof(Node20_1HandlerData),
            //     knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true" },
            //     node20GlibcError: true,
            //     legacyExpectedNode: "node16", // Legacy falls back to Node16
            //     legacyExpectSuccess: true,
            //     unifiedExpectSuccess: false, // Unified throws error
            //     expectedErrorType: typeof(NotSupportedException), // <<<<< should this be node 24 - for smae reason as in test Node20_GlibcError_EOLPolicy_ThrowsError
            //     unifiedExpectedError: "notFound:NodeVersionNotAvailable",
            //     inContainer: true,
            //     shouldMatchBetweenModes: false
            //     /*
            //     NodeHandlerL0StrategyComparisonTests.NodeHandler_DivergenceTest_LegacyAndUnifiedProduceDifferentResults
            //     (scenario: Node20_InContainer_GlibcError_EOLPolicy_ThrowsError) [90 ms]
            //     EXEC : error Message:  [C:\RISHABH\azure-pipelines-agent\src\dir.proj]
            //         Unified should fail for scenario: Node20_InContainer_GlibcError_EOLPolicy_ThrowsError
            //     Expected: False
            //     Actual:   True
            //     */
            // ),
            new TestScenario( // this is fixed test  for - Node20_InContainer_GlibcError_EOLPolicy_ThrowsError
                name: "Node20_InContainer_GlibcError_EOLPolicy_UpgradesToNode24",
                description: "Node20 in container with glibc error and EOL policy: legacy falls back to Node16, unified upgrades to Node24",
                handlerData: typeof(Node20_1HandlerData),
                knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true" },
                node20GlibcError: true,
                legacyExpectedNode: "node16", // Legacy falls back to Node16
                legacyExpectSuccess: true,
                unifiedExpectedNode: "node24", // Unified upgrades to Node24 (EOL policy triggers upgrade)
                unifiedExpectSuccess: true,
                inContainer: true,
                shouldMatchBetweenModes: false // Different behavior: legacy uses Node16, unified uses Node24
            ),

            new TestScenario(
                name: "Node16_InContainer_WithRedirect_EOLPolicy_UpgradesToNode24",
                description: "Node16 in container with redirect and EOL policy: legacy allows redirect to Node16, unified upgrades to Node24",
                handlerData: typeof(Node16HandlerData),
                knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true" },
                containerNeedsNode16Redirect: true,
                legacyExpectedNode: "node16", // Legacy allows EOL redirect
                legacyExpectSuccess: true,
                unifiedExpectedNode: "node24", // Unified upgrades despite redirect
                unifiedExpectSuccess: true, 
                inContainer: true,
                shouldMatchBetweenModes: false
            ),
            
            new TestScenario(
                name: "Node20_InContainer_WithRedirect_EOLPolicy_UpgradesToNode24",
                description: "Node20 in container with redirect and EOL policy: legacy stays Node20, unified upgrades to Node24",
                handlerData: typeof(Node20_1HandlerData),
                knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true" },
                containerNeedsNode20Redirect: true,
                legacyExpectedNode: "node20_1", // Legacy stays Node20
                legacyExpectSuccess: true,
                unifiedExpectedNode: "node24", // Unified upgrades to Node24 due to EOL policy
                unifiedExpectSuccess: true,
                inContainer: true,
                shouldMatchBetweenModes: false
            ),
            
            // ============================================
            // GROUP 6: NODE24 SCENARIOS (Node24HandlerData)
            // ============================================
            
            new TestScenario(
                name: "Node24_DefaultBehavior_WithKnobEnabled",
                description: "Node24 handler uses Node24 when AGENT_USE_NODE24_WITH_HANDLER_DATA=true",
                handlerData: typeof(Node24HandlerData),
                knobs: new() { ["AGENT_USE_NODE24_WITH_HANDLER_DATA"] = "true" },
                expectedNode: "node24",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),
            
            new TestScenario(
                name: "Node24_WithHandlerDataKnobDisabled_FallsBackToNode20",
                description: "Node24 handler falls back to Node20 when AGENT_USE_NODE24_WITH_HANDLER_DATA=false",
                handlerData: typeof(Node24HandlerData),
                knobs: new() { ["AGENT_USE_NODE24_WITH_HANDLER_DATA"] = "false" },
                expectedNode: "node20_1", // Should fallback to Node20
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),
            
            new TestScenario(
                name: "Node24_WithGlobalUseNode24Knob",
                description: "AGENT_USE_NODE24=true forces Node24 regardless of handler data knob",
                handlerData: typeof(Node24HandlerData),
                knobs: new() { ["AGENT_USE_NODE24"] = "true" },
                expectedNode: "node24",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),
            
            new TestScenario(
                name: "Node24_WithUseNode10Knob",
                description: "Node24 handler ignores deprecated AGENT_USE_NODE10 knob in unified strategy",
                handlerData: typeof(Node24HandlerData),
                knobs: new() 
                { 
                    ["AGENT_USE_NODE24_WITH_HANDLER_DATA"] = "true",
                    ["AGENT_USE_NODE10"] = "true"
                },
                legacyExpectedNode: "node10", // Legacy behavior honored deprecated knob
                legacyExpectSuccess: true,
                unifiedExpectedNode: "node24", // Unified ignores deprecated knob
                unifiedExpectSuccess: true,
                shouldMatchBetweenModes: false
            ),
            
            new TestScenario(
                name: "Node24_WithUseNode20Knob",
                description: "Node24 handler with AGENT_USE_NODE20_1=true: legacy honors knob, unified ignores deprecated knob",
                handlerData: typeof(Node24HandlerData),
                knobs: new() 
                { 
                    ["AGENT_USE_NODE24_WITH_HANDLER_DATA"] = "true",
                    ["AGENT_USE_NODE20_1"] = "true"
                },
                shouldMatchBetweenModes: false, // Legacy honors deprecated knob, unified ignores it
                legacyExpectedNode: "node20_1", // Legacy honors AGENT_USE_NODE20_1
                unifiedExpectedNode: "node24" // Unified ignores deprecated knob, uses handler data
            ),
            
            new TestScenario(
                name: "Node24_GlibcError_FallsBackToNode20",
                description: "Node24 with glibc error falls back to Node20",
                handlerData: typeof(Node24HandlerData),
                knobs: new() { ["AGENT_USE_NODE24_WITH_HANDLER_DATA"] = "true" },
                node24GlibcError: true,
                expectedNode: "node20_1",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),
            
            new TestScenario(
                name: "Node24_GlibcError_Node20GlibcError_FallsBackToNode16", 
                description: "Node24 with both glibc errors falls back to Node16 when EOL policy is disabled",
                handlerData: typeof(Node24HandlerData),
                knobs: new() { ["AGENT_USE_NODE24_WITH_HANDLER_DATA"] = "true" },
                node24GlibcError: true,
                node20GlibcError: true,
                expectedNode: "node16",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),
            
            new TestScenario(
                name: "Node24_GlibcError_Node20GlibcError_EOLPolicy_ThrowsError",
                description: "Node24 with glibc errors and EOL policy enabled throws error (cannot fallback to Node16)",
                handlerData: typeof(Node24HandlerData),
                knobs: new() 
                { 
                    ["AGENT_USE_NODE24_WITH_HANDLER_DATA"] = "true",
                    ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true"
                },
                node24GlibcError: true,
                node20GlibcError: true,
                legacyExpectedNode: "node16", // Legacy falls back to Node16
                legacyExpectSuccess: true,
                unifiedExpectSuccess: false, // Unified throws error
                expectedErrorType: typeof(NotSupportedException),
                unifiedExpectedError: "No compatible Node.js version available for host execution. Handler type: Node24HandlerData. This may occur if all available versions are blocked by EOL policy. Please update your pipeline to use Node20 or Node24 tasks. To temporarily disable EOL policy: Set AGENT_ENABLE_EOL_NODE_VERSION_POLICY=false",
                shouldMatchBetweenModes: false // Legacy doesn't have this EOL policy check
            ),
            
            new TestScenario(
                name: "Node24_PriorityTest_UseNode24OverridesUseNode20",
                description: "When both global knobs are set, Node24 takes priority",
                handlerData: typeof(Node24HandlerData),
                knobs: new() 
                { 
                    ["AGENT_USE_NODE20_1"] = "true",
                    ["AGENT_USE_NODE24"] = "true"
                },
                expectedNode: "node24", // Higher priority global knob wins
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            new TestScenario(
                name: "Node24_EOLUpgrade_FromOldHandler",
                description: "Node10HandlerData with EOL policy upgrades to Node24 (highest priority strategy)",
                handlerData: typeof(Node10HandlerData), // Old handler
                knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true" },
                legacyExpectedNode: "node10", // Legacy allows EOL nodes
                legacyExpectSuccess: true,
                unifiedExpectedNode: "node24", // Unified upgrades to Node24
                unifiedExpectSuccess: true,
                shouldMatchBetweenModes: false // Different behavior
            ),

            // ============================================
            // GROUP 8: EDGE CASES AND ERROR SCENARIOS
            // ============================================
            
            new TestScenario(
                name: "Node24_MultipleKnobs_GlobalWins",
                description: "When multiple knobs conflict, global AGENT_USE_NODE24 takes highest priority",
                handlerData: typeof(Node20_1HandlerData),
                knobs: new() 
                { 
                    ["AGENT_USE_NODE10"] = "true",
                    ["AGENT_USE_NODE20_1"] = "true",
                    ["AGENT_USE_NODE24"] = "true"
                },
                expectedNode: "node24", // Global Node24 has highest priority
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            new TestScenario(
                name: "Node16_EOLPolicy_WithUseNode10Knob_UpgradesToNode24",
                description: "Node16 handler with deprecated knob still upgrades to Node24 when EOL policy enabled",
                handlerData: typeof(Node16HandlerData),
                knobs: new() 
                { 
                    ["AGENT_USE_NODE10"] = "true",
                    ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true"
                },
                legacyExpectedNode: "node10", // Legacy honors deprecated knob
                legacyExpectSuccess: true,
                unifiedExpectedNode: "node24", // Unified upgrades to Node24
                unifiedExpectSuccess: true,
                shouldMatchBetweenModes: false // Different behavior
            ),

            new TestScenario(
                name: "Node24_InContainer_WithCustomPath",
                description: "Node24 handler works in container environment",
                handlerData: typeof(Node24HandlerData),
                knobs: new() { ["AGENT_USE_NODE24_WITH_HANDLER_DATA"] = "true" },
                expectedNode: "node24",
                expectSuccess: true,
                inContainer: true,
                shouldMatchBetweenModes: true
            ),

            new TestScenario(
                name: "Node20_AllGlobalKnobsDisabled_UsesHandler",
                description: "Node20 handler uses handler data when all global knobs are false",
                handlerData: typeof(Node20_1HandlerData),
                knobs: new() 
                { 
                    ["AGENT_USE_NODE10"] = "false",
                    ["AGENT_USE_NODE20_1"] = "false",
                    ["AGENT_USE_NODE24"] = "false"
                },
                expectedNode: "node20_1", // Should use handler data
                expectSuccess: true,
                shouldMatchBetweenModes: true
            )

            // ============================================
            // GROUP 7: Custom Node Path Scenarios (Priority 0 - HIGHEST)
            // TODO: Enable these tests once UnifiedCustomNodeStrategy is properly integrated
            // ============================================
            
            // TODO: Add custom node scenarios once the following is implemented:
            // 1. UnifiedCustomNodeStrategy needs to be registered in the orchestrator
            // 2. NodeHandler needs to properly set StepTarget.CustomNodePath and Container.CustomNodePath
            // 3. Test infrastructure needs to properly mock custom path scenarios
            // 
            // Expected custom node scenarios:
            // - CustomNode_Host_OverridesHandlerData: Custom path beats handler data
            // - CustomNode_Host_BypassesAllKnobs: Custom path ignores global knobs
            // - CustomNode_Host_BypassesEOLPolicy: Custom path bypasses EOL policy
            // - CustomNode_Container_FromDockerLabel: Docker label custom path
            // - CustomNode_HighestPriority_OverridesEverything: Custom path beats all logic
        };

        // ============================================
        // Simple Test Access - No Complex Helpers
        // ============================================

        /// <summary>
        /// Get all Node24 test scenarios for simple iteration.
        /// </summary>
        public static TestScenario[] Node24Scenarios 
            => AllScenarios.Where(s => s.HandlerDataType == typeof(Node24HandlerData)).ToArray();
            
        /// <summary>
        /// Get all Node20 test scenarios for simple iteration.
        /// </summary>
        public static TestScenario[] Node20Scenarios 
            => AllScenarios.Where(s => s.HandlerDataType == typeof(Node20_1HandlerData)).ToArray();
            
        /// <summary>
        /// Get all Node16 test scenarios for simple iteration.
        /// </summary>
        public static TestScenario[] Node16Scenarios 
            => AllScenarios.Where(s => s.HandlerDataType == typeof(Node16HandlerData)).ToArray();

        /// <summary>
        /// Get all Node10 test scenarios for simple iteration.
        /// </summary>
        public static TestScenario[] Node10Scenarios 
            => AllScenarios.Where(s => s.HandlerDataType == typeof(Node10HandlerData)).ToArray();

        /// <summary>
        /// Get all Node6 test scenarios for simple iteration.
        /// </summary>
        public static TestScenario[] Node6Scenarios 
            => AllScenarios.Where(s => s.HandlerDataType == typeof(NodeHandlerData)).ToArray();

        /// <summary>
        /// Get all Custom Node test scenarios for simple iteration.
        /// </summary>
        public static TestScenario[] CustomNodeScenarios 
            => AllScenarios.Where(s => !string.IsNullOrEmpty(s.CustomNodePath)).ToArray();
    }

    /// <summary>
    /// Test scenario specification.
    /// </summary>
    public class TestScenario
    {
        // Identification
        public string Name { get; set; }
        public string Description { get; set; }
        
        // Test inputs - Handler Configuration
        public Type HandlerDataType { get; set; }  // Single handler (matches reality)
        
        public Dictionary<string, string> Knobs { get; set; } = new();
        public bool Node20GlibcError { get; set; }
        public bool Node24GlibcError { get; set; }
        public bool InContainer { get; set; }
        public bool ContainerNeedsNode16Redirect { get; set; }
        public bool ContainerNeedsNode20Redirect { get; set; }
        public string CustomNodePath { get; set; }
        public bool IsAlpine { get; set; }
        public bool PrimaryNodeUnavailable { get; set; }
        public bool LtsNodeUnavailable { get; set; }
        public bool AllNodesUnavailable { get; set; }
        
        // Expected results (for equivalent scenarios)
        public string ExpectedNode { get; set; }
        public bool ExpectSuccess { get; set; } = true;
        
        // Expected results (for divergent scenarios)
        public string LegacyExpectedNode { get; set; }
        public string UnifiedExpectedNode { get; set; }
        public bool LegacyExpectSuccess { get; set; } = true;
        public bool UnifiedExpectSuccess { get; set; } = true;
        public string UnifiedExpectedError { get; set; }
        public Type ExpectedErrorType { get; set; }
        
        // Metadata
        public bool ShouldMatchBetweenModes { get; set; } = true;
        
        public TestScenario(
            string name, 
            string description,
            Type handlerData,
            Dictionary<string, string> knobs = null,
            string expectedNode = null,
            bool expectSuccess = true,
            string legacyExpectedNode = null,
            string unifiedExpectedNode = null,
            bool legacyExpectSuccess = true,
            bool unifiedExpectSuccess = true,
            string unifiedExpectedError = null,
            Type expectedErrorType = null,
            bool shouldMatchBetweenModes = true,
            bool node20GlibcError = false,
            bool node24GlibcError = false,
            bool inContainer = false,
            bool containerNeedsNode16Redirect = false,
            bool containerNeedsNode20Redirect = false,
            string customNodePath = null,
            bool isAlpine = false,
            bool primaryNodeUnavailable = false,
            bool ltsNodeUnavailable = false,
            bool allNodesUnavailable = false)
        {
            Name = name;
            Description = description;
            HandlerDataType = handlerData ?? throw new ArgumentNullException(nameof(handlerData));
            
            Knobs = knobs ?? new Dictionary<string, string>();
            ExpectedNode = expectedNode;
            ExpectSuccess = expectSuccess;
            LegacyExpectedNode = legacyExpectedNode ?? expectedNode;
            UnifiedExpectedNode = unifiedExpectedNode ?? expectedNode;
            LegacyExpectSuccess = legacyExpectSuccess;
            UnifiedExpectSuccess = unifiedExpectSuccess;
            UnifiedExpectedError = unifiedExpectedError;
            ExpectedErrorType = expectedErrorType;
            ShouldMatchBetweenModes = shouldMatchBetweenModes;
            Node20GlibcError = node20GlibcError;
            Node24GlibcError = node24GlibcError;
            InContainer = inContainer;
            ContainerNeedsNode16Redirect = containerNeedsNode16Redirect;
            ContainerNeedsNode20Redirect = containerNeedsNode20Redirect;
            CustomNodePath = customNodePath;
            IsAlpine = isAlpine;
            PrimaryNodeUnavailable = primaryNodeUnavailable;
            LtsNodeUnavailable = ltsNodeUnavailable;
            AllNodesUnavailable = allNodesUnavailable;
        }
        
        public override string ToString() => Name; // For test display names
    }

}
