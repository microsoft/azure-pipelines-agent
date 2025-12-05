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
        public static readonly TestScenario[] AllScenarios = new[]
        {
            // ============================================
            // GROUP 1: CUSTOM NODE SCENARIOS
            // ============================================
            
            new TestScenario( // DONE
                name: "CustomNode_Host_OverridesHandlerData",
                description: "Custom node path takes priority over handler data type",
                handlerData: typeof(Node20_1HandlerData),
                customNodePath: "/usr/local/custom/node",
                inContainer: false,
                expectedNode: "/usr/local/custom/node",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            new TestScenario( // DONE
                name: "CustomNode_Host_BypassesAllKnobs",
                description: "Custom node path ignores all global node version knobs",
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

            new TestScenario( // DONE
                name: "CustomNode_Host_BypassesEOLPolicy",
                description: "Custom node path bypasses EOL policy restrictions",
                handlerData: typeof(Node10HandlerData),
                knobs: new()
                {
                    ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true"
                },
                customNodePath: "/legacy/node6/bin/node",
                inContainer: false,
                expectedNode: "/legacy/node6/bin/node",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            // new TestScenario( // DO WE NEED THIS TEST, AS CUSTOM NODE DOES NOT HAVE GLIBC CHECKS, NOT EARLIER, NOT NOW ---
            //     name: "CustomNode_Host_NoGlibcFallback",
            //     description: "Custom node path ignores glibc compatibility errors",
            //     handlerData: typeof(Node24HandlerData),
            //     knobs: new() { ["AGENT_USE_NODE24_WITH_HANDLER_DATA"] = "true" },
            //     node24GlibcError: true, // This would normally cause fallback
            //     node20GlibcError: true, // This would normally cause fallback
            //     customNodePath: "/broken-glibc/node", // Custom path still used
            //     inContainer: false,
            //     expectedNode: "/broken-glibc/node",
            //     expectSuccess: true,
            //     shouldMatchBetweenModes: true
            // ),

            new TestScenario( // HOW DOES THIS TEST WORK, WHERE ARE WE PICKING CUSTOM NODE FROM DOCKER LABEL VIA THIS TEST ---
                name: "CustomNode_Container_FromDockerLabel",
                description: "Container uses custom node path from Docker label",
                handlerData: typeof(Node16HandlerData),
                customNodePath: "/container/custom/node",
                inContainer: true,
                expectedNode: "/container/custom/node",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            new TestScenario( // THIS AGENT_USE_NODE24_WITH_HANDLER_DATA KNOB IS FOR NODE HANDLER, DO WE NEED THIS TEST AND INCONTAINER = TRUE HERE ---
                name: "CustomNode_Container_OverridesHandlerData",
                description: "Container custom node path overrides task handler data",
                handlerData: typeof(Node24HandlerData),
                knobs: new() { ["AGENT_USE_NODE24_WITH_HANDLER_DATA"] = "true" },
                customNodePath: "/container/node20/bin/node", // Different from handler
                inContainer: true,
                expectedNode: "/container/node20/bin/node",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            new TestScenario( // CHECK IF THIS IS DUPLICATE TEST ---
                name: "CustomNode_Container_BypassesEOLPolicy",
                description: "Container custom node path bypasses EOL policy",
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

            new TestScenario( // what scenario is this one? ---
                name: "CustomNode_WindowsPath",
                description: "Custom node path works with Windows file path format",
                handlerData: typeof(Node20_1HandlerData),
                customNodePath: "C:\\Program Files\\nodejs\\node.exe",
                inContainer: false,
                expectedNode: "C:\\Program Files\\nodejs\\node.exe",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            new TestScenario( // what is this scenario testing? ---
                name: "CustomNode_RelativePath",
                description: "Custom node path works with relative file paths",
                handlerData: typeof(Node16HandlerData),
                customNodePath: "./custom-node/bin/node",
                inContainer: false,
                expectedNode: "./custom-node/bin/node",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

             new TestScenario(
                name: "CustomNode_NullPath_FallsBackToNormalLogic",
                description: "Null custom node path falls back to standard node selection",
                handlerData: typeof(Node24HandlerData),
                knobs: new() { ["AGENT_USE_NODE24_WITH_HANDLER_DATA"] = "true" },
                customNodePath: null,
                inContainer: false,
                expectedNode: "node24",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            new TestScenario(
                name: "CustomNode_EmptyString_IgnoredFallsBackToNormalLogic",
                description: "Empty custom node path is ignored, falls back to normal handler logic",
                handlerData: typeof(Node20_1HandlerData),
                customNodePath: "",
                inContainer: false,
                expectedNode: "node20_1",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            new TestScenario(
                name: "CustomNode_WhitespaceOnly_IgnoredFallsBackToNormalLogic",
                description: "Whitespace-only custom node path is ignored, falls back to normal handler logic",
                handlerData: typeof(Node16HandlerData),
                customNodePath: "   ",
                inContainer: false,
                expectedNode: "node16",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            new TestScenario(
                name: "CustomNode_Container_OverridesContainerKnobs",
                description: "Container custom node path overrides container-specific knobs",
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

            new TestScenario( // CHECK THIS TEST ---
                name: "CustomNode_Container_PathTranslation",
                description: "Custom host path gets translated to container path via TranslateToContainerPath",
                handlerData: typeof(Node16HandlerData),
                customNodePath: "/host/path/to/node", // Host path that should be translated
                inContainer: true,
                expectedNode: "/host/path/to/node", // Should be translated by TranslateToContainerPath
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            new TestScenario(// CHECK THIS TEST --- do we need this test
            // we have version extraction logic in custom ndoe strategy
            // chek if that funciton is required, if not then just remove that functions and this test as well
                name: "CustomNode_VersionExtraction_FromPath",
                description: "Node version is extracted from custom path for logging",
                handlerData: typeof(Node16HandlerData),
                customNodePath: "/usr/local/node20/bin/node",
                inContainer: false,
                expectedNode: "/usr/local/node20/bin/node",
                expectSuccess: true,
                shouldMatchBetweenModes: true
                // Note: Version extraction ("node20") is tested through strategy's ExtractNodeVersionFromPath method
            ),

            new TestScenario( // CHECK THIS TEST ---
            // how can this haapen, both host and container having custom paths
            // did we even implement this scenario in strategy? check this
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

            // ========================================================================================
            // GROUP 3: NODE6 SCENARIOS (Node6HandlerData - EOL)
            // ========================================================================================
            
            new TestScenario(
                name: "Node6_DefaultBehavior_EOLPolicyDisabled",
                description: "Node6 handler works when EOL policy is disabled",
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
                legacyExpectedNode: "node",
                legacyExpectSuccess: true,
                unifiedExpectedNode: "node24",
                unifiedExpectSuccess: true,
                shouldMatchBetweenModes: false
            ),

            // let add a test here for when node 6 handler, EOL is on, node24 has glibc error, fallsback to node 20
            // but ensure not to repeat this test
            // ADD ABOVE
            // ANOTHER TEST FOR WHEN BOTH NODE24 AND NODE20 HAVE GLIBC ERRORS, AND EOL IS ON, SHOULD THROW ERROR IN UNIFIED MODE
            // ADD ABOVE

            // ============================================
            // GROUP 4: NODE10 SCENARIOS (Node10HandlerData - EOL)
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
                legacyExpectedNode: "node10",
                legacyExpectSuccess: true,
                unifiedExpectedNode: "node24",
                unifiedExpectSuccess: true,
                shouldMatchBetweenModes: false
            ),

            new TestScenario( // NEWLY ADDED - BELOW COMMENTED TEST
                name: "Node10_EOLPolicy_Node24GlibcError_FallsBackToNode20",
                description: "Node10 handler with EOL policy and Node24 glibc error: legacy allows Node10, unified falls back to Node20",
                handlerData: typeof(Node10HandlerData),
                knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true" },
                node24GlibcError: true, // Node24 has glibc error, can't upgrade
                legacyExpectedNode: "node10", // Legacy allows EOL nodes regardless
                legacyExpectSuccess: true,
                unifiedExpectedNode: "node20_1", // Unified falls back to Node20 (next best option)
                unifiedExpectSuccess: true,
                shouldMatchBetweenModes: false // Different behavior
            ),

            new TestScenario(// NEWLY ADDED - BELOW COMMENTED TEST
                name: "Node10_EOLPolicy_BothNode24AndNode20GlibcErrors_ThrowsError",
                description: "Node10 handler with EOL policy and both newer versions having glibc errors: legacy allows Node10, unified throws error",
                handlerData: typeof(Node10HandlerData),
                knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true" },
                node24GlibcError: true, // Node24 has glibc error
                node20GlibcError: true, // Node20 also has glibc error
                legacyExpectedNode: "node10", // Legacy allows EOL nodes regardless of glibc errors
                legacyExpectSuccess: true,
                unifiedExpectSuccess: false, // Unified throws error (no compatible upgrade path)
                expectedErrorType: typeof(NotSupportedException),
                unifiedExpectedError: "No compatible Node.js version available for host execution. Handler type: Node10HandlerData. This may occur if all available versions are blocked by EOL policy. Please update your pipeline to use Node20 or Node24 tasks. To temporarily disable EOL policy: Set AGENT_ENABLE_EOL_NODE_VERSION_POLICY=false",
                shouldMatchBetweenModes: false // Different behavior
            ),

            // let add a test here for when node 10 handler, EOL is on, node24 has glibc error, fallsback to node 20
            // but ensure not to repeat this test
            // ADD ABOVE
            // ANOTHER TEST FOR WHEN BOTH NODE24 AND NODE20 HAVE GLIBC ERRORS, AND EOL IS ON, SHOULD THROW ERROR IN UNIFIED MODE
            // ADD ABOVE
            // *****   BOTH TESTS ADDED ABOVE  *********

            

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
                knobs: new() { },
                expectedNode: "node16",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            new TestScenario(
                name: "Node16_EOLPolicyEnabled_UpgradesToNode24",
                description: "Node16 handler with EOL policy: legacy allows Node16, unified upgrades to Node24",
                handlerData: typeof(Node16HandlerData),
                knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true" },
                legacyExpectedNode: "node16",
                legacyExpectSuccess: true,
                unifiedExpectedNode: "node24",
                unifiedExpectSuccess: true,
                shouldMatchBetweenModes: false
            ),

            // 1. let add a test here for when node 16 handler, EOL is on, node24 has glibc error, fallsback to node 20
            // 2. ANOTHER TEST FOR WHEN BOTH NODE24 AND NODE20 HAVE GLIBC ERRORS, AND EOL IS ON, SHOULD THROW ERROR IN UNIFIED MODE

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
                legacyExpectedNode: "node16",
                legacyExpectSuccess: true,
                unifiedExpectedNode: "node24",
                unifiedExpectSuccess: true,
                inContainer: true,
                shouldMatchBetweenModes: false
            ),

            new TestScenario(
                name: "Node16_EOLPolicy_Node24GlibcError_FallsBackToNode20",
                description: "Node16 handler with EOL policy and Node24 glibc error: legacy allows Node16, unified falls back to Node20",
                handlerData: typeof(Node16HandlerData),
                knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true" },
                node24GlibcError: true,
                legacyExpectedNode: "node16",
                legacyExpectSuccess: true,
                unifiedExpectedNode: "node20_1",
                unifiedExpectSuccess: true,
                shouldMatchBetweenModes: false
            ),

            new TestScenario(
                name: "Node16_EOLPolicy_BothNode24AndNode20GlibcErrors_ThrowsError",
                description: "Node16 handler with EOL policy and both newer versions having glibc errors: legacy allows Node16, unified throws error",
                handlerData: typeof(Node16HandlerData),
                knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true" },
                node24GlibcError: true,
                node20GlibcError: true,
                legacyExpectedNode: "node16",
                legacyExpectSuccess: true,
                unifiedExpectSuccess: false,
                expectedErrorType: typeof(NotSupportedException),
                unifiedExpectedError: "No compatible Node.js version available for host execution. Handler type: Node16HandlerData. This may occur if all available versions are blocked by EOL policy. Please update your pipeline to use Node20 or Node24 tasks. To temporarily disable EOL policy: Set AGENT_ENABLE_EOL_NODE_VERSION_POLICY=false",
                shouldMatchBetweenModes: false
            ),

            new TestScenario( // NEWLY ADDED - BELOW COMMENTED TEST
            // NEED FIX FOR CONTAINER TEST FOR REDIRECT FIELDSFOR NODE 16 AND NODE 20 REDIRECT FOR IN CONTAINER
            // FIX DONE
            // failing with node24 actially getting selected, need to debug
                name: "Node16_InContainer_EOLPolicy_Node24GlibcError_FallsBackToNode20",
                description: "Node16 handler in container with EOL policy and Node24 glibc error: legacy allows Node16, unified falls back to Node20",
                handlerData: typeof(Node16HandlerData),
                knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true" },
                containerNeedsNode20Redirect: true, // This simulates Node24 glibc error in container
                inContainer: true,
                legacyExpectedNode: "node16", // Legacy allows EOL nodes regardless
                legacyExpectSuccess: true,
                unifiedExpectedNode: "node20_1", // Unified falls back to Node20
                unifiedExpectSuccess: true,
                shouldMatchBetweenModes: false // Different behavior
            ),

            new TestScenario( // NEWLY ADDED - BELOW COMMENTED TEST
            // NEED FIX FOR CONTAINER TEST FOR REDIRECT FIELDSFOR NODE 16 AND NODE 20 REDIRECT FOR IN CONTAINER
            // FIX DONE
            // failing with no exception thrown, need to debug
                name: "Node16_InContainer_EOLPolicy_BothNode24AndNode20GlibcErrors_ThrowsError",
                description: "Node16 handler in container with EOL policy and both newer versions having glibc errors: legacy allows Node16, unified throws error",
                handlerData: typeof(Node16HandlerData),
                knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true" },
                containerNeedsNode16Redirect: true, // This simulates Node20 glibc error in container  
                containerNeedsNode20Redirect: true, // This simulates Node24 glibc error in container
                inContainer: true,
                legacyExpectedNode: "node16", // Legacy allows EOL nodes regardless
                legacyExpectSuccess: true,
                unifiedExpectSuccess: false, // Unified throws error (no compatible upgrade path)
                expectedErrorType: typeof(NotSupportedException),
                unifiedExpectedError: "No compatible Node.js version available for host execution. Handler type: Node16HandlerData. This may occur if all available versions are blocked by EOL policy. Please update your pipeline to use Node20 or Node24 tasks. To temporarily disable EOL policy: Set AGENT_ENABLE_EOL_NODE_VERSION_POLICY=false",
                shouldMatchBetweenModes: false // Different behavior
            ),

            // 1. let add a test here for when (IN CONTAINER) node 16 handler, EOL is on, node24 has glibc error, fallsback to node 20
            // 2. ANOTHER TEST FOR WHEN (IN CONTAINER) BOTH NODE24 AND NODE20 HAVE GLIBC ERRORS, AND EOL IS ON, SHOULD THROW ERROR IN UNIFIED MODE
            // REVIEW THESE 2 ABIVE SCCNARIOS, HOW TO DO THEM, DO THEY REQUIRED, NODE 16/20 REDIRECT FIELDS WHICH WE JUST REMOVED
            // ADDED THESE 2 ABOVE TESTS FOR HOST MODE, SIMILAR TESTS CAN BE ADDED FOR IN CONTAINER IF REQUIRED
            // 4 tests added above*************
            // 2 for host and 2 for in container


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
                description: "Global Node20 knob forces Node20 regardless of handler type",
                handlerData: typeof(Node20_1HandlerData),
                knobs: new() { ["AGENT_USE_NODE20_1"] = "true" },
                expectedNode: "node20_1",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),
            
            // should this give node24?? as node 24 glibc is compatible in this case (not set so good)
            // lets review this test case - review done of both below 2 test,
            // below 2 mentioned cases covered in immediate 2 below tests for EOL policy enabled
            // 2 cases in this for EOL = true
            // 1. node20 glibc error true, node24 glibc error false ( not set) - select node 24 - this test is covered - Node20_GlibcError_EOLPolicy_UpgradesToNode24
            // 2. node20 glibc error true, node24 glibc error true - should throw error as both have glibc errors
            // new TestScenario( // DONE
            //     name: "Node20_GlibcError_Node24_GlibcError_EOLPolicy_ThrowsError",
            //     description: "Node20 and Node24 with glibc error and EOL policy enabled throws error (cannot fallback to Node16), legacy picks Node16",
            //     handlerData: typeof(Node20_1HandlerData),
            //     knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true" },
            //     node20GlibcError: true,
            //     node24GlibcError: true,
            //     legacyExpectedNode: "node16", // Legacy falls back to Node16
            //     legacyExpectSuccess: true,
                // expectSuccess: false,
            //     expectedErrorType: typeof(NotSupportedException),
            //     unifiedExpectedError: "would fallback to Node16 (EOL) but EOL policy is enabled",
            //     shouldMatchBetweenModes: false, // Different EOL policy behavior
            // ),
            // uncomment htis test, giving some syntax error, check and fix
            
            new TestScenario(
                name: "Node20_GlibcError_EOLPolicy_UpgradesToNode24",
                description: "Node20 with glibc error and EOL policy: legacy falls back to Node16, unified upgrades to Node24",
                handlerData: typeof(Node20_1HandlerData),
                knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true" },
                node20GlibcError: true,
                legacyExpectedNode: "node16",
                legacyExpectSuccess: true,
                unifiedExpectedNode: "node24",
                unifiedExpectSuccess: true,
                shouldMatchBetweenModes: false
            ),

            new TestScenario(
                name: "Node20_WithGlobalUseNode24Knob",
                description: "Global Node24 knob overrides Node20 handler data",
                handlerData: typeof(Node20_1HandlerData),
                knobs: new() { ["AGENT_USE_NODE24"] = "true" },
                expectedNode: "node24",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            new TestScenario(
                name: "Node20_WithUseNode10Knob",
                description: "Node20 handler ignores deprecated Node10 knob in unified strategy",
                handlerData: typeof(Node20_1HandlerData),
                knobs: new() { ["AGENT_USE_NODE10"] = "true" },
                legacyExpectedNode: "node10",
                legacyExpectSuccess: true,
                unifiedExpectedNode: "node20_1",
                unifiedExpectSuccess: true,
                shouldMatchBetweenModes: false
            ),


            new TestScenario(
                name: "Node20_PriorityTest_UseNode20OverridesUseNode10",
                description: "Node20 global knob takes priority over Node10 global knob",
                handlerData: typeof(Node20_1HandlerData),
                knobs: new() 
                { 
                    ["AGENT_USE_NODE10"] = "true",
                    ["AGENT_USE_NODE20_1"] = "true"
                },
                expectedNode: "node20_1",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            new TestScenario(
                name: "Node20_PriorityTest_UseNode24OverridesUseNode20",
                description: "Node24 global knob takes priority over Node20 global knob",
                handlerData: typeof(Node20_1HandlerData),
                knobs: new() 
                { 
                    ["AGENT_USE_NODE20_1"] = "true",
                    ["AGENT_USE_NODE24"] = "true"
                },
                expectedNode: "node24",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            /*
            we need in container test here for node 20

            */

            // ============================================
            // GROUP 3: Node16 (Node16HandlerData) Scenarios - END-OF-LIFE
            // ============================================
            new TestScenario(
                name: "Node16_DefaultBehavior_EOLPolicyDisabled",
                description: "Node16 handler works when EOL policy is explicitly disabled",
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
                knobs: new() { },
                expectedNode: "node16",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            new TestScenario(
                name: "Node16_EOLPolicyEnabled_UpgradesToNode24",
                description: "Node16 handler with EOL policy: legacy allows Node16, unified upgrades to Node24",
                handlerData: typeof(Node16HandlerData),
                knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true" },
                legacyExpectedNode: "node16",
                legacyExpectSuccess: true,
                unifiedExpectedNode: "node24",
                unifiedExpectSuccess: true,
                shouldMatchBetweenModes: false
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

            new TestScenario( // se this test, correspoinf test in container Node16_InContainer_EOLPolicy_Node24GlibcError_FallsBackToNode20 is failing 
            // this test is successful, debug the other test
                name: "Node16_InContainer_EOLPolicyEnabled_UpgradesToNode24",
                description: "Node16 in container with EOL policy: legacy allows Node16, unified upgrades to Node24",
                handlerData: typeof(Node16HandlerData),
                knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true" },
                legacyExpectedNode: "node16",
                legacyExpectSuccess: true,
                unifiedExpectedNode: "node24",
                unifiedExpectSuccess: true,
                inContainer: true,
                shouldMatchBetweenModes: false
            ),

            // ============================================
            // GROUP 7: CONTAINER-SPECIFIC EOL SCENARIOS
            // ============================================
            
            new TestScenario( // VERIFY THIS TEST ---
                name: "Node24_InContainer_GlibcError_EOLPolicy_FallsBackToNode20",
                description: "Node24 in container with glibc error falls back to Node20 when EOL policy prevents Node16",
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
            
            new TestScenario( // VERIFY THIS TEST ---
                name: "Node24_InContainer_BothGlibcErrors_EOLPolicy_ThrowsError",
                description: "Node24 in container with all glibc errors and EOL policy throws error (unified) or falls back to Node16 (legacy)",
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
                unifiedExpectedError: "No compatible Node.js version available for host execution. Handler type: Node24HandlerData. This may occur if all available versions are blocked by EOL policy. Please update your pipeline to use Node20 or Node24 tasks. To temporarily disable EOL policy: Set AGENT_ENABLE_EOL_NODE_VERSION_POLICY=false", // this is wrong --- this TEST NEEDS FIXING
                inContainer: true,
                shouldMatchBetweenModes: false
            ),
            
            new TestScenario(
                name: "Node20_InContainer_GlibcError_EOLPolicy_UpgradesToNode24",
                description: "Node20 in container with glibc error and EOL policy upgrades to Node24 (unified) or falls back to Node16 (legacy)",
                handlerData: typeof(Node20_1HandlerData),
                knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true" },
                node20GlibcError: true,
                legacyExpectedNode: "node16",
                legacyExpectSuccess: true,
                unifiedExpectedNode: "node24",
                unifiedExpectSuccess: true,
                inContainer: true,
                shouldMatchBetweenModes: false
            ),

            new TestScenario(
                name: "Node16_InContainer_WithRedirect_EOLPolicy_UpgradesToNode24",
                description: "Node16 in container with redirect and EOL policy upgrades to Node24 (unified) or allows Node16 (legacy)",
                handlerData: typeof(Node16HandlerData),
                knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true" },
                containerNeedsNode16Redirect: true,
                legacyExpectedNode: "node16",
                legacyExpectSuccess: true,
                unifiedExpectedNode: "node24",
                unifiedExpectSuccess: true, 
                inContainer: true,
                shouldMatchBetweenModes: false
            ),
            
            new TestScenario( // VERIFY THIS TEST --- containerNeedsNode20Redirect = true sp node24 has glibc error on container
            // fix this, currently passing incorrectly
                name: "Node20_InContainer_WithRedirect_EOLPolicy_UpgradesToNode24",
                description: "Node20 in container with redirect and EOL policy upgrades to Node24 (unified) or stays Node20 (legacy)",
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

            new TestScenario( 
                name: "Node20_AllGlobalKnobsDisabled_UsesHandler",
                description: "Node20 handler uses handler data when all global knobs are disabled",
                handlerData: typeof(Node20_1HandlerData),
                knobs: new() 
                { 
                    ["AGENT_USE_NODE10"] = "false",
                    ["AGENT_USE_NODE20_1"] = "false",
                    ["AGENT_USE_NODE24"] = "false"
                },
                expectedNode: "node20_1",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            
            // ============================================
            // GROUP 6: NODE24 SCENARIOS (Node24HandlerData)
            // ============================================
            
            new TestScenario(
                name: "Node24_DefaultBehavior_WithKnobEnabled",
                description: "Node24 handler uses Node24 when handler-specific knob is enabled",
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
                expectedNode: "node20_1",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),
            
            new TestScenario(
                name: "Node24_WithGlobalUseNode24Knob",
                description: "Global Node24 knob overrides handler-specific knob setting",
                handlerData: typeof(Node24HandlerData),
                knobs: new() { ["AGENT_USE_NODE24"] = "true" },
                expectedNode: "node24",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),
            
            new TestScenario(
                name: "Node24_WithUseNode10Knob",
                description: "Node24 handler ignores deprecated Node10 knob in unified strategy",
                handlerData: typeof(Node24HandlerData),
                knobs: new() 
                { 
                    ["AGENT_USE_NODE24_WITH_HANDLER_DATA"] = "true",
                    ["AGENT_USE_NODE10"] = "true"
                },
                legacyExpectedNode: "node10",
                legacyExpectSuccess: true,
                unifiedExpectedNode: "node24",
                unifiedExpectSuccess: true,
                shouldMatchBetweenModes: false
            ),
            
            new TestScenario(
                name: "Node24_WithUseNode20Knob",
                description: "Node24 handler ignores deprecated Node20 knob in unified strategy",
                handlerData: typeof(Node24HandlerData),
                knobs: new() 
                { 
                    ["AGENT_USE_NODE24_WITH_HANDLER_DATA"] = "true",
                    ["AGENT_USE_NODE20_1"] = "true"
                },
                shouldMatchBetweenModes: false,
                legacyExpectedNode: "node20_1",
                unifiedExpectedNode: "node24"
            ),
            
            new TestScenario(
                name: "Node24_GlibcError_FallsBackToNode20",
                description: "Node24 with glibc compatibility error falls back to Node20",
                handlerData: typeof(Node24HandlerData),
                knobs: new() { ["AGENT_USE_NODE24_WITH_HANDLER_DATA"] = "true" },
                node24GlibcError: true,
                expectedNode: "node20_1",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),
            
            new TestScenario(
                name: "Node24_GlibcError_Node20GlibcError_FallsBackToNode16", 
                description: "Node24 with both Node24 and Node20 glibc errors falls back to Node16",
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
                description: "Node24 with all glibc errors and EOL policy throws error (unified) or falls back to Node16 (legacy)",
                handlerData: typeof(Node24HandlerData),
                knobs: new() 
                { 
                    ["AGENT_USE_NODE24_WITH_HANDLER_DATA"] = "true",
                    ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true"
                },
                node24GlibcError: true,
                node20GlibcError: true,
                legacyExpectedNode: "node16",
                legacyExpectSuccess: true,
                unifiedExpectSuccess: false,
                expectedErrorType: typeof(NotSupportedException),
                unifiedExpectedError: "No compatible Node.js version available for host execution. Handler type: Node24HandlerData. This may occur if all available versions are blocked by EOL policy. Please update your pipeline to use Node20 or Node24 tasks. To temporarily disable EOL policy: Set AGENT_ENABLE_EOL_NODE_VERSION_POLICY=false",
                shouldMatchBetweenModes: false
            ),
            
            new TestScenario(
                name: "Node24_PriorityTest_UseNode24OverridesUseNode20",
                description: "Node24 global knob takes priority over Node20 global knob",
                handlerData: typeof(Node24HandlerData),
                knobs: new() 
                { 
                    ["AGENT_USE_NODE20_1"] = "true",
                    ["AGENT_USE_NODE24"] = "true"
                },
                expectedNode: "node24",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            // ============================================
            // GROUP 8: EDGE CASES AND ERROR SCENARIOS
            // ============================================
            
            new TestScenario(
                name: "Node24_MultipleKnobs_GlobalWins",
                description: "Global Node24 knob takes highest priority when multiple knobs are set",
                handlerData: typeof(Node20_1HandlerData),
                knobs: new() 
                { 
                    ["AGENT_USE_NODE10"] = "true",
                    ["AGENT_USE_NODE20_1"] = "true",
                    ["AGENT_USE_NODE24"] = "true"
                },
                expectedNode: "node24",
                expectSuccess: true,
                shouldMatchBetweenModes: true
            ),

            new TestScenario(
                name: "Node16_EOLPolicy_WithUseNode10Knob_UpgradesToNode24",
                description: "Node16 handler with deprecated Node10 knob upgrades to Node24 when EOL policy is enabled (unified) or uses Node10 (legacy)",
                handlerData: typeof(Node16HandlerData),
                knobs: new() 
                { 
                    ["AGENT_USE_NODE10"] = "true",
                    ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true"
                },
                legacyExpectedNode: "node10",
                legacyExpectSuccess: true,
                unifiedExpectedNode: "node24",
                unifiedExpectSuccess: true,
                shouldMatchBetweenModes: false
            ),

            new TestScenario(
                name: "Node24_InContainer_DefaultBehavior",
                description: "Node24 handler works correctly in container environments",
                handlerData: typeof(Node24HandlerData),
                knobs: new() { ["AGENT_USE_NODE24_WITH_HANDLER_DATA"] = "true" },
                expectedNode: "node24",
                expectSuccess: true,
                inContainer: true,
                shouldMatchBetweenModes: true
            ),
        };

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
