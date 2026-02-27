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
    public static class NodeHandlerTestSpecs
    {
        public static readonly TestScenario[] AllScenarios = new[]
        {
            // ============================================
            // GROUP 0: CUSTOM NODE SCENARIOS
            // ============================================
            new TestScenario(
                name: "CustomNode_Host_OverridesHandlerData",
                description: "Custom node path takes priority over handler data type",
                handlerData: typeof(Node20_1HandlerData),
                customNodePath: "/usr/local/custom/node",
                inContainer: false,
                expectedNode: "/usr/local/custom/node"
            ),

            new TestScenario(
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
                expectedNode: "/opt/my-node/bin/node"
            ),

            new TestScenario(
                name: "CustomNode_Host_BypassesEOLPolicy",
                description: "Custom node path bypasses EOL policy restrictions",
                handlerData: typeof(Node10HandlerData),
                knobs: new()
                {
                    ["AGENT_RESTRICT_EOL_NODE_VERSIONS"] = "true"
                },
                customNodePath: "/legacy/node6/bin/node",
                inContainer: false,
                expectedNode: "/legacy/node6/bin/node"
            ),

            new TestScenario(
                name: "CustomNode_HighestPriority_OverridesEverything",
                description: "Custom path has highest priority - overrides all knobs, EOL policy, and glibc errors",
                handlerData: typeof(Node10HandlerData),
                knobs: new()
                {
                    ["AGENT_USE_NODE24"] = "true",
                    ["AGENT_USE_NODE20_1"] = "true", 
                    ["AGENT_RESTRICT_EOL_NODE_VERSIONS"] = "true",
                    ["AGENT_USE_NODE24_WITH_HANDLER_DATA"] = "false"
                },
                node20GlibcError: true,
                node24GlibcError: true,
                customNodePath: "/ultimate/override/node",
                inContainer: false,
                expectedNode: "/ultimate/override/node"
            ),

             new TestScenario(
                name: "CustomNode_NullPath_FallsBackToNormalLogic",
                description: "Null custom node path falls back to standard node selection",
                handlerData: typeof(Node24HandlerData),
                knobs: new() { ["AGENT_USE_NODE24_WITH_HANDLER_DATA"] = "true" },
                customNodePath: null,
                inContainer: false,
                expectedNode: "node24"
            ),

            new TestScenario(
                name: "CustomNode_EmptyString_IgnoredFallsBackToNormalLogic",
                description: "Empty custom node path is ignored, falls back to normal handler logic",
                handlerData: typeof(Node20_1HandlerData),
                customNodePath: "",
                inContainer: false,
                expectedNode: "node20_1"
            ),

            new TestScenario(
                name: "CustomNode_WhitespaceOnly_IgnoredFallsBackToNormalLogic",
                description: "Whitespace-only custom node path is ignored, falls back to normal handler logic",
                handlerData: typeof(Node16HandlerData),
                customNodePath: "   ",
                inContainer: false,
                expectedNode: "node16"
            ),

            // ========================================================================================
            // GROUP 1: NODE6 SCENARIOS (Node6HandlerData - EOL)
            // ========================================================================================
            new TestScenario(
                name: "Node6_DefaultBehavior",
                description: "Node6 handler works when in default behavior (EOL policy disabled)",
                handlerData: typeof(NodeHandlerData),
                knobs: new() {},
                expectedNode: "node"
            ),

            new TestScenario(
                name: "Node6_DefaultBehavior_EOLPolicyDisabled",
                description: "Node6 handler works when EOL policy is disabled",
                handlerData: typeof(NodeHandlerData),
                knobs: new() { ["AGENT_RESTRICT_EOL_NODE_VERSIONS"] = "false" },
                expectedNode: "node"
            ),

            new TestScenario(
                name: "Node6_EOLPolicyEnabled_UpgradesToNode24",
                description: "Node6 handler with EOL policy: legacy allows Node6, strategy-based upgrades to Node24",
                handlerData: typeof(NodeHandlerData),
                knobs: new() { ["AGENT_RESTRICT_EOL_NODE_VERSIONS"] = "true" },
                legacyExpectedNode: "node",
                strategyExpectedNode: "node24"
            ),

            new TestScenario(
                name: "Node6_WithGlobalUseNode10Knob",
                description: "Node6 handler with global Node10 knob: legacy uses Node10, strategy-based ignores deprecated knob and uses Node6",
                handlerData: typeof(NodeHandlerData),
                knobs: new() { ["AGENT_USE_NODE10"] = "true" },
                legacyExpectedNode: "node10",
                strategyExpectedNode: "node"
            ),

            new TestScenario(
                name: "Node6_WithGlobalUseNode20Knob",
                description: "Global Node20 knob overrides Node6 handler data",
                handlerData: typeof(NodeHandlerData),
                knobs: new() { ["AGENT_USE_NODE20_1"] = "true" },
                expectedNode: "node20_1"
            ),

            new TestScenario(
                name: "Node6_WithGlobalUseNode24Knob",
                description: "Global Node24 knob overrides Node6 handler data",
                handlerData: typeof(NodeHandlerData),
                knobs: new() { ["AGENT_USE_NODE24"] = "true" },
                expectedNode: "node24"
            ),

            new TestScenario(
                name: "Node6_PriorityTest_UseNode24OverridesUseNode20",
                description: "Node24 global knob takes priority over Node20 global knob with Node6 handler",
                handlerData: typeof(NodeHandlerData),
                knobs: new() 
                { 
                    ["AGENT_USE_NODE20_1"] = "true",
                    ["AGENT_USE_NODE24"] = "true"
                },
                expectedNode: "node24"
            ),

            new TestScenario(
                name: "Node6_PriorityTest_UseNode20OverridesUseNode10",
                description: "Node20 global knob takes priority over Node10 global knob with Node6 handler",
                handlerData: typeof(NodeHandlerData),
                knobs: new() 
                { 
                    ["AGENT_USE_NODE10"] = "true",
                    ["AGENT_USE_NODE20_1"] = "true"
                },
                expectedNode: "node20_1"
            ),

            new TestScenario(
                name: "Node6_MultipleKnobs_GlobalWins",
                description: "Global Node24 knob takes highest priority when multiple knobs are set with Node6 handler",
                handlerData: typeof(NodeHandlerData),
                knobs: new() 
                { 
                    ["AGENT_USE_NODE10"] = "true",
                    ["AGENT_USE_NODE20_1"] = "true",
                    ["AGENT_USE_NODE24"] = "true"
                },
                expectedNode: "node24"
            ),

            new TestScenario(
                name: "Node6_AllGlobalKnobsDisabled_UsesHandler",
                description: "Node6 handler uses handler data when all global knobs are disabled",
                handlerData: typeof(NodeHandlerData),
                knobs: new() 
                { 
                    ["AGENT_USE_NODE10"] = "false",
                    ["AGENT_USE_NODE20_1"] = "false",
                    ["AGENT_USE_NODE24"] = "false"
                },
                expectedNode: "node"
            ),

            new TestScenario(
                name: "Node6_EOLPolicy_Node24GlibcError_FallsBackToNode20",
                description: "Node6 handler with EOL policy and Node24 glibc error: legacy allows Node6, strategy-based falls back to Node20",
                handlerData: typeof(NodeHandlerData),
                knobs: new() { ["AGENT_RESTRICT_EOL_NODE_VERSIONS"] = "true" },
                node24GlibcError: true,
                legacyExpectedNode: "node",
                strategyExpectedNode: "node20_1"
            ),

            new TestScenario(
                name: "Node6_EOLPolicy_BothNode24AndNode20GlibcErrors_ThrowsError",
                description: "Node6 handler with EOL policy and both newer versions having glibc errors: legacy allows Node6, strategy-based throws error",
                handlerData: typeof(NodeHandlerData),
                knobs: new() { ["AGENT_RESTRICT_EOL_NODE_VERSIONS"] = "true" },
                node24GlibcError: true,
                node20GlibcError: true,
                legacyExpectedNode: "node",
                expectedErrorType: typeof(NotSupportedException),
                strategyExpectedError: "No compatible Node.js version available for host execution. Handler type: NodeHandlerData. This may occur if all available versions are blocked by EOL policy. Please update your pipeline to use Node20 or Node24 tasks. To temporarily disable EOL policy: Set AGENT_RESTRICT_EOL_NODE_VERSIONS=false"
            ),

            // ========================================================================================
            // GROUP 2: NODE10 SCENARIOS (Node10HandlerData - EOL)
            // ========================================================================================
            
            new TestScenario(
                name: "Node10_DefaultBehavior",
                description: "Node10 handler uses Node10",
                handlerData: typeof(Node10HandlerData),
                knobs: new() {},
                expectedNode: "node10"
            ),

            new TestScenario(
                name: "Node10_DefaultBehavior_EOLPolicyDisabled",
                description: "Node10 handler uses Node10 when EOL policy is disabled",
                handlerData: typeof(Node10HandlerData),
                knobs: new() { ["AGENT_RESTRICT_EOL_NODE_VERSIONS"] = "false" },
                expectedNode: "node10"
            ),

            new TestScenario(
                name: "Node10_EOLPolicyEnabled_UpgradesToNode24",
                description: "Node10 handler with EOL policy: legacy allows Node10, strategy-based upgrades to Node24",
                handlerData: typeof(Node10HandlerData),
                knobs: new() { ["AGENT_RESTRICT_EOL_NODE_VERSIONS"] = "true" },
                legacyExpectedNode: "node10",
                strategyExpectedNode: "node24"
            ),

            new TestScenario(
                name: "Node10_WithGlobalUseNode10Knob",
                description: "Global Node10 knob reinforces Node10 handler data",
                handlerData: typeof(Node10HandlerData),
                knobs: new() { ["AGENT_USE_NODE10"] = "true" },
                expectedNode: "node10"
            ),

            new TestScenario(
                name: "Node10_WithGlobalUseNode20Knob",
                description: "Global Node20 knob overrides Node10 handler data",
                handlerData: typeof(Node10HandlerData),
                knobs: new() { ["AGENT_USE_NODE20_1"] = "true" },
                expectedNode: "node20_1"
            ),

            new TestScenario(
                name: "Node10_WithGlobalUseNode24Knob",
                description: "Global Node24 knob overrides Node10 handler data",
                handlerData: typeof(Node10HandlerData),
                knobs: new() { ["AGENT_USE_NODE24"] = "true" },
                expectedNode: "node24"
            ),

            new TestScenario(
                name: "Node10_PriorityTest_UseNode24OverridesUseNode20",
                description: "Node24 global knob takes priority over Node20 global knob with Node10 handler",
                handlerData: typeof(Node10HandlerData),
                knobs: new() 
                { 
                    ["AGENT_USE_NODE20_1"] = "true",
                    ["AGENT_USE_NODE24"] = "true"
                },
                expectedNode: "node24"
            ),

            new TestScenario(
                name: "Node10_PriorityTest_UseNode20OverridesUseNode10",
                description: "Node20 global knob takes priority over Node10 global knob with Node10 handler",
                handlerData: typeof(Node10HandlerData),
                knobs: new() 
                { 
                    ["AGENT_USE_NODE10"] = "true",
                    ["AGENT_USE_NODE20_1"] = "true"
                },
                expectedNode: "node20_1"
            ),

            new TestScenario(
                name: "Node10_MultipleKnobs_GlobalWins",
                description: "Global Node24 knob takes highest priority when multiple knobs are set with Node10 handler",
                handlerData: typeof(Node10HandlerData),
                knobs: new() 
                { 
                    ["AGENT_USE_NODE10"] = "true",
                    ["AGENT_USE_NODE20_1"] = "true",
                    ["AGENT_USE_NODE24"] = "true"
                },
                expectedNode: "node24"
            ),

            new TestScenario(
                name: "Node10_AllGlobalKnobsDisabled_UsesHandler",
                description: "Node10 handler uses handler data when all global knobs are disabled",
                handlerData: typeof(Node10HandlerData),
                knobs: new() 
                { 
                    ["AGENT_USE_NODE10"] = "false",
                    ["AGENT_USE_NODE20_1"] = "false",
                    ["AGENT_USE_NODE24"] = "false"
                },
                expectedNode: "node10"
            ),

            new TestScenario(
                name: "Node10_EOLPolicy_Node24GlibcError_FallsBackToNode20",
                description: "Node10 handler with EOL policy and Node24 glibc error: legacy allows Node10, strategy-based falls back to Node20",
                handlerData: typeof(Node10HandlerData),
                knobs: new() { ["AGENT_RESTRICT_EOL_NODE_VERSIONS"] = "true" },
                node24GlibcError: true,
                legacyExpectedNode: "node10",
                strategyExpectedNode: "node20_1"
            ),

            new TestScenario(
                name: "Node10_EOLPolicy_BothNode24AndNode20GlibcErrors_ThrowsError",
                description: "Node10 handler with EOL policy and both newer versions having glibc errors: legacy allows Node10, strategy-based throws error",
                handlerData: typeof(Node10HandlerData),
                knobs: new() { ["AGENT_RESTRICT_EOL_NODE_VERSIONS"] = "true" },
                node24GlibcError: true,
                node20GlibcError: true,
                legacyExpectedNode: "node10",
                expectedErrorType: typeof(NotSupportedException),
                strategyExpectedError: "No compatible Node.js version available for host execution. Handler type: Node10HandlerData. This may occur if all available versions are blocked by EOL policy. Please update your pipeline to use Node20 or Node24 tasks. To temporarily disable EOL policy: Set AGENT_RESTRICT_EOL_NODE_VERSIONS=false"
            ),

            // ========================================================================================
            // GROUP 3: NODE16 SCENARIOS (Node16HandlerData)
            // ========================================================================================
            
            new TestScenario(
                name: "Node16_DefaultBehavior_EOLPolicyDisabled",
                description: "Node16 handler uses Node16 when EOL policy is disabled",
                handlerData: typeof(Node16HandlerData),
                knobs: new() { ["AGENT_RESTRICT_EOL_NODE_VERSIONS"] = "false" },
                expectedNode: "node16"
            ),

            new TestScenario(
                name: "Node16_DefaultEOLPolicy_AllowsNode16",
                description: "Node16 handler uses Node16 when EOL policy is default (disabled)",
                handlerData: typeof(Node16HandlerData),
                knobs: new() { },
                expectedNode: "node16"
            ),

            new TestScenario(
                name: "Node16_EOLPolicyEnabled_UpgradesToNode24",
                description: "Node16 handler with EOL policy: legacy allows Node16, strategy-based upgrades to Node24",
                handlerData: typeof(Node16HandlerData),
                knobs: new() { ["AGENT_RESTRICT_EOL_NODE_VERSIONS"] = "true" },
                legacyExpectedNode: "node16",
                strategyExpectedNode: "node24"
            ),

            new TestScenario(
                name: "Node16_EOLPolicy_Node24GlibcError_FallsBackToNode20",
                description: "Node16 handler with EOL policy and Node24 glibc error: legacy allows Node16, strategy-based falls back to Node20",
                handlerData: typeof(Node16HandlerData),
                knobs: new() { ["AGENT_RESTRICT_EOL_NODE_VERSIONS"] = "true" },
                node24GlibcError: true,
                legacyExpectedNode: "node16",
                strategyExpectedNode: "node20_1"
            ),

            new TestScenario(
                name: "Node16_EOLPolicy_BothNode24AndNode20GlibcErrors_ThrowsError",
                description: "Node16 handler with EOL policy and both newer versions having glibc errors: legacy allows Node16, strategy-based throws error",
                handlerData: typeof(Node16HandlerData),
                knobs: new() { ["AGENT_RESTRICT_EOL_NODE_VERSIONS"] = "true" },
                node24GlibcError: true,
                node20GlibcError: true,
                legacyExpectedNode: "node16",
                expectedErrorType: typeof(NotSupportedException),
                strategyExpectedError: "No compatible Node.js version available for host execution. Handler type: Node16HandlerData. This may occur if all available versions are blocked by EOL policy. Please update your pipeline to use Node20 or Node24 tasks. To temporarily disable EOL policy: Set AGENT_RESTRICT_EOL_NODE_VERSIONS=false"
            ),
            
            // ========================================================================================
            // GROUP 4: NODE20 SCENARIOS (Node20_1HandlerData)
            // ========================================================================================
            new TestScenario(
                name: "Node20_DefaultBehavior_WithHandler",
                description: "Node20 handler uses Node20 by default",
                handlerData: typeof(Node20_1HandlerData),
                knobs: new() { },
                expectedNode: "node20_1"
            ),
            
            new TestScenario(
                name: "Node20_WithGlobalUseNode20Knob",
                description: "Global Node20 knob forces Node20 regardless of handler type",
                handlerData: typeof(Node20_1HandlerData),
                knobs: new() { ["AGENT_USE_NODE20_1"] = "true" },
                expectedNode: "node20_1"
            ),

            new TestScenario(
                name: "Node20_GlibcError_EOLPolicy_UpgradesToNode24",
                description: "Node20 with glibc error and EOL policy: legacy falls back to Node16, strategy-based upgrades to Node24",
                handlerData: typeof(Node20_1HandlerData),
                knobs: new() { ["AGENT_RESTRICT_EOL_NODE_VERSIONS"] = "true" },
                node20GlibcError: true,
                legacyExpectedNode: "node16",
                strategyExpectedNode: "node24"
            ),

            new TestScenario(
                name: "Node20_WithGlobalUseNode24Knob",
                description: "Global Node24 knob overrides Node20 handler data",
                handlerData: typeof(Node20_1HandlerData),
                knobs: new() { ["AGENT_USE_NODE24"] = "true" },
                expectedNode: "node24"
            ),

            new TestScenario(
                name: "Node20_WithUseNode10Knob",
                description: "Node20 handler ignores deprecated Node10 knob in strategy-based approach",
                handlerData: typeof(Node20_1HandlerData),
                knobs: new() { ["AGENT_USE_NODE10"] = "true" },
                legacyExpectedNode: "node10",
                strategyExpectedNode: "node20_1"
            ),

            new TestScenario(
                name: "Node20_MultipleKnobs_GlobalWins",
                description: "Global Node24 knob takes highest priority when multiple knobs are set with Node20 handler",
                handlerData: typeof(Node20_1HandlerData),
                knobs: new() 
                { 
                    ["AGENT_USE_NODE10"] = "true",
                    ["AGENT_USE_NODE20_1"] = "true",
                    ["AGENT_USE_NODE24"] = "true"
                },
                expectedNode: "node24"
            ),

            new TestScenario(
                name: "Node20_GlibcError_Node24GlibcError_EOLPolicy_ThrowsError",
                description: "Node20 and Node24 with glibc error and EOL policy enabled throws error (cannot fallback to Node16), legacy picks Node16",
                handlerData: typeof(Node20_1HandlerData),
                knobs: new() { ["AGENT_RESTRICT_EOL_NODE_VERSIONS"] = "true" },
                node20GlibcError: true,
                node24GlibcError: true,
                legacyExpectedNode: "node16",
                expectedErrorType: typeof(NotSupportedException),
                strategyExpectedError: "No compatible Node.js version available for host execution. Handler type: Node20_1HandlerData. This may occur if all available versions are blocked by EOL policy. Please update your pipeline to use Node20 or Node24 tasks. To temporarily disable EOL policy: Set AGENT_RESTRICT_EOL_NODE_VERSIONS=false"
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
                expectedNode: "node20_1"
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
                expectedNode: "node24"
            ),
            
            // ========================================================================================
            // GROUP 5: CONTAINER-SPECIFIC EOL SCENARIOS
            // ========================================================================================
                      
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
                expectedNode: "node20_1"
            ),

            
            // ========================================================================================
            // GROUP 6: NODE24 SCENARIOS (Node24HandlerData)
            // ========================================================================================
            
            new TestScenario(
                name: "Node24_DefaultBehavior_WithKnobEnabled",
                description: "Node24 handler uses Node24 when handler-specific knob is enabled",
                handlerData: typeof(Node24HandlerData),
                knobs: new() { ["AGENT_USE_NODE24_WITH_HANDLER_DATA"] = "true" },
                expectedNode: "node24"
            ),
            
            new TestScenario(
                name: "Node24_WithHandlerDataKnobDisabled_FallsBackToNode20",
                description: "Node24 handler falls back to Node20 when AGENT_USE_NODE24_WITH_HANDLER_DATA=false",
                handlerData: typeof(Node24HandlerData),
                knobs: new() { ["AGENT_USE_NODE24_WITH_HANDLER_DATA"] = "false" },
                expectedNode: "node20_1"
            ),
            
            new TestScenario(
                name: "Node24_WithGlobalUseNode24Knob",
                description: "Global Node24 knob overrides handler-specific knob setting",
                handlerData: typeof(Node24HandlerData),
                knobs: new() { ["AGENT_USE_NODE24"] = "true" },
                expectedNode: "node24"
            ),
            
            new TestScenario(
                name: "Node24_WithUseNode10Knob",
                description: "Node24 handler ignores deprecated Node10 knob in strategy-based approach",
                handlerData: typeof(Node24HandlerData),
                knobs: new() 
                { 
                    ["AGENT_USE_NODE24_WITH_HANDLER_DATA"] = "true",
                    ["AGENT_USE_NODE10"] = "true"
                },
                legacyExpectedNode: "node10",
                strategyExpectedNode: "node24"
            ),
            
            new TestScenario(
                name: "Node24_WithUseNode20Knob",
                description: "Node24 handler ignores deprecated Node20 knob in strategy-based approach",
                handlerData: typeof(Node24HandlerData),
                knobs: new() 
                { 
                    ["AGENT_USE_NODE24_WITH_HANDLER_DATA"] = "true",
                    ["AGENT_USE_NODE20_1"] = "true"
                },
                legacyExpectedNode: "node20_1",
                strategyExpectedNode: "node24"
            ),
            
            new TestScenario(
                name: "Node24_GlibcError_FallsBackToNode20",
                description: "Node24 with glibc compatibility error falls back to Node20",
                handlerData: typeof(Node24HandlerData),
                knobs: new() { ["AGENT_USE_NODE24_WITH_HANDLER_DATA"] = "true" },
                node24GlibcError: true,
                expectedNode: "node20_1"
            ),
            
            new TestScenario(
                name: "Node24_GlibcError_Node20GlibcError_FallsBackToNode16", 
                description: "Node24 with both Node24 and Node20 glibc errors falls back to Node16",
                handlerData: typeof(Node24HandlerData),
                knobs: new() { ["AGENT_USE_NODE24_WITH_HANDLER_DATA"] = "true" },
                node24GlibcError: true,
                node20GlibcError: true,
                expectedNode: "node16"
            ),
            
            new TestScenario(
                name: "Node24_GlibcError_Node20GlibcError_EOLPolicy_ThrowsError",
                description: "Node24 with all glibc errors and EOL policy throws error (strategy-based) or falls back to Node16 (legacy)",
                handlerData: typeof(Node24HandlerData),
                knobs: new() 
                { 
                    ["AGENT_USE_NODE24_WITH_HANDLER_DATA"] = "true",
                    ["AGENT_RESTRICT_EOL_NODE_VERSIONS"] = "true"
                },
                node24GlibcError: true,
                node20GlibcError: true,
                legacyExpectedNode: "node16",
                expectedErrorType: typeof(NotSupportedException),
                strategyExpectedError: "No compatible Node.js version available for host execution. Handler type: Node24HandlerData. This may occur if all available versions are blocked by EOL policy. Please update your pipeline to use Node20 or Node24 tasks. To temporarily disable EOL policy: Set AGENT_RESTRICT_EOL_NODE_VERSIONS=false"
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
                expectedNode: "node24"
            ),

            // ========================================================================================
            // GROUP 7: EDGE CASES AND ERROR SCENARIOS
            // ========================================================================================
            
            new TestScenario(
                name: "Node16_EOLPolicy_WithUseNode10Knob_UpgradesToNode24",
                description: "Node16 handler with deprecated Node10 knob upgrades to Node24 when EOL policy is enabled (strategy-based) or uses Node10 (legacy)",
                handlerData: typeof(Node16HandlerData),
                knobs: new() 
                { 
                    ["AGENT_USE_NODE10"] = "true",
                    ["AGENT_RESTRICT_EOL_NODE_VERSIONS"] = "true"
                },
                legacyExpectedNode: "node10",
                strategyExpectedNode: "node24"
            ),

            // ========================================================================================
            // GROUP 8: CONTAINER SCENARIOS
            // ========================================================================================
            
            new TestScenario(
                name: "CustomNode_Container_OverridesHandlerData",
                description: "Container custom node path overrides task handler data",
                handlerData: typeof(Node24HandlerData),
                customNodePath: "/container/node20/bin/node",
                inContainer: true,
                expectedNode: "/container/node20/bin/node"
            ),

            new TestScenario(
                name: "CustomNode_Container_OverridesContainerKnobs",
                description: "Container custom node path overrides container-specific knobs",
                handlerData: typeof(Node20_1HandlerData),
                knobs: new()
                {
                    ["AZP_AGENT_USE_NODE24_TO_START_CONTAINER"] = "true",
                    ["AZP_AGENT_USE_NODE20_TO_START_CONTAINER"] = "true"
                },
                customNodePath: "/container/custom/node",
                inContainer: true,
                expectedNode: "/container/custom/node"
            ),

            new TestScenario(
                name: "CustomNode_Container_OverridesContainerNode20Knobs",
                description: "Container custom node path overrides node20 knob to start container",
                handlerData: typeof(Node20_1HandlerData),
                knobs: new()
                {
                    ["AZP_AGENT_USE_NODE20_TO_START_CONTAINER"] = "true"
                },
                customNodePath: "/container/custom/node",
                inContainer: true,
                expectedNode: "/container/custom/node"
            ),

            new TestScenario(
                name: "CustomNode_Container_OverridesContainerNode24Knobs",
                description: "Container custom node path overrides node24 knob to start container",
                handlerData: typeof(Node20_1HandlerData),
                knobs: new()
                {
                    ["AZP_AGENT_USE_NODE24_TO_START_CONTAINER"] = "true",
                },
                customNodePath: "/container/custom/node",
                inContainer: true,
                expectedNode: "/container/custom/node"
            ),

            new TestScenario(
                name: "Container_EOLPolicyDisabled_AllowsNode16Fallback",
                description: "Container with EOL policy disabled allows fallback to Node16 when container knobs are disabled",
                handlerData: typeof(Node16HandlerData),
                knobs: new() { 
                    ["AGENT_RESTRICT_EOL_NODE_VERSIONS"] = "false",
                    ["AZP_AGENT_USE_NODE20_TO_START_CONTAINER"] = "false",
                    ["AZP_AGENT_USE_NODE24_TO_START_CONTAINER"] = "false"
                },
                expectedNode: "node16",
                inContainer: true
            ),

            new TestScenario(
                name: "Container_EOLPolicy_UpgradesToNode24",
                description: "Container with EOL policy upgrades to Node24 when Node24 container knob is enabled",
                handlerData: typeof(Node16HandlerData),
                knobs: new() { 
                    ["AGENT_RESTRICT_EOL_NODE_VERSIONS"] = "true", 
                    ["AZP_AGENT_USE_NODE24_TO_START_CONTAINER"] = "true"
                },
                expectedNode: "node24",
                inContainer: true
            ),

            new TestScenario(
                name: "Container_Node20Enabled_DefaultBehavior",
                description: "Container with Node20 enabled works correctly when Node24 is disabled",
                handlerData: typeof(Node20_1HandlerData),
                knobs: new() { 
                    ["AZP_AGENT_USE_NODE20_TO_START_CONTAINER"] = "true",
                    ["AZP_AGENT_USE_NODE24_TO_START_CONTAINER"] = "false"
                },
                expectedNode: "node20_1",
                inContainer: true
            ),

            new TestScenario(
                name: "Container_Node24Enabled_DefaultBehavior",
                description: "Container with Node24 enabled works correctly",
                handlerData: typeof(Node24HandlerData),
                knobs: new() { 
                    ["AZP_AGENT_USE_NODE24_TO_START_CONTAINER"] = "true"
                },
                expectedNode: "node24",
                inContainer: true
            ),

            new TestScenario(
                name: "Container_EOLPolicy_Node24Preferred_GlibcError_FallsBackToNode20",
                description: "Container with EOL policy, Node24 preferred but has glibc error: falls back to Node20",
                handlerData: typeof(Node16HandlerData),
                knobs: new() { 
                    ["AGENT_RESTRICT_EOL_NODE_VERSIONS"] = "true", 
                    ["AZP_AGENT_USE_NODE20_TO_START_CONTAINER"] = "true",
                    ["AZP_AGENT_USE_NODE24_TO_START_CONTAINER"] = "true"
                },
                node24GlibcError: true,
                inContainer: true,
                expectedNode: "node20_1"
            ),

            new TestScenario(
                name: "Container_Node20Preferred_GlibcError_FallsBackToNode16",
                description: "Container with Node20 preferred but has glibc error: falls back to Node16 when EOL policy disabled",
                handlerData: typeof(Node20_1HandlerData),
                knobs: new() { 
                    ["AGENT_RESTRICT_EOL_NODE_VERSIONS"] = "false",
                    ["AZP_AGENT_USE_NODE20_TO_START_CONTAINER"] = "true",
                    ["AZP_AGENT_USE_NODE24_TO_START_CONTAINER"] = "false"
                },
                node20GlibcError: true,
                expectedNode: "node16",
                inContainer: true
            ),

            new TestScenario(
                name: "Container_Node24Enabled_GlibcError_EOLPolicy_FallsBackToNode20",
                description: "Container with Node24 enabled but has glibc error: falls back to Node20 when EOL policy prevents Node16",
                handlerData: typeof(Node24HandlerData),
                knobs: new() 
                { 
                    ["AGENT_USE_NODE24_WITH_HANDLER_DATA"] = "true",
                    ["AGENT_RESTRICT_EOL_NODE_VERSIONS"] = "true",
                    ["AZP_AGENT_USE_NODE20_TO_START_CONTAINER"] = "true"
                },
                node24GlibcError: true,
                expectedNode: "node20_1",
                inContainer: true
            ),

            new TestScenario(
                name: "Container_EOLPolicy_AllModernNodesFailGlibc_ThrowsError",
                description: "Container with EOL policy and both Node24/Node20 glibc errors: cannot use Node16 due to policy, throws error",
                handlerData: typeof(Node16HandlerData),
                knobs: new() { ["AGENT_RESTRICT_EOL_NODE_VERSIONS"] = "true" },
                node24GlibcError: true,
                node20GlibcError: true,
                inContainer: true,
                legacyExpectedNode: "node16",
                expectedErrorType: typeof(NotSupportedException),
                strategyExpectedError: "No compatible Node.js version available for container execution. Node16 is blocked by EOL policy. Please update your pipeline to use Node20 or Node24 tasks."
            ),

            new TestScenario(
                name: "Container_AllModernVersionsFailGlibc_EOLPolicy_ThrowsError",
                description: "Container with all modern Node.js versions having glibc errors and EOL policy: throws error (strategy-based) or falls back to Node16 (legacy)",
                handlerData: typeof(Node24HandlerData),
                knobs: new() 
                { 
                    ["AGENT_USE_NODE24_WITH_HANDLER_DATA"] = "true",
                    ["AGENT_RESTRICT_EOL_NODE_VERSIONS"] = "true"
                },
                node24GlibcError: true,
                node20GlibcError: true,
                legacyExpectedNode: "node16",
                expectedErrorType: typeof(NotSupportedException),
                strategyExpectedError: "No compatible Node.js version available for container execution. Node16 is blocked by EOL policy. Please update your pipeline to use Node20 or Node24 tasks.",
                inContainer: true
            ),

            new TestScenario(
                name: "Container_GlobalNode24Knob_OverridesContainerDefaults",
                description: "Global Node24 knob with container Node24 knob enabled uses Node24 in container",
                handlerData: typeof(Node20_1HandlerData),
                knobs: new() { 
                    ["AGENT_USE_NODE24"] = "true", 
                    ["AZP_AGENT_USE_NODE24_TO_START_CONTAINER"] = "true"
                },
                expectedNode: "node24",
                inContainer: true
            )          
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
        public Type HandlerDataType { get; set; } 
        
        public Dictionary<string, string> Knobs { get; set; } = new();
        public bool Node20GlibcError { get; set; }
        public bool Node24GlibcError { get; set; }
        public bool InContainer { get; set; }
        public string CustomNodePath { get; set; }
        
        // Expected results (for equivalent scenarios)
        public string ExpectedNode { get; set; }
        
        // Expected results (for divergent scenarios)
        public string LegacyExpectedNode { get; set; }
        public string StrategyExpectedNode { get; set; }
        public string StrategyExpectedError { get; set; }
        public Type ExpectedErrorType { get; set; }
        
        public TestScenario(
            string name, 
            string description,
            Type handlerData,
            Dictionary<string, string> knobs = null,
            string expectedNode = null,
            string legacyExpectedNode = null,
            string strategyExpectedNode = null,
            string strategyExpectedError = null,
            Type expectedErrorType = null,
            bool node20GlibcError = false,
            bool node24GlibcError = false,
            bool inContainer = false,
            string customNodePath = null
            )
        {
            Name = name;
            Description = description;
            HandlerDataType = handlerData ?? throw new ArgumentNullException(nameof(handlerData));
            Knobs = knobs ?? new Dictionary<string, string>();
            ExpectedNode = expectedNode;
            LegacyExpectedNode = legacyExpectedNode ?? expectedNode;
            StrategyExpectedNode = strategyExpectedNode ?? expectedNode;
            StrategyExpectedError = strategyExpectedError;
            ExpectedErrorType = expectedErrorType;
            Node20GlibcError = node20GlibcError;
            Node24GlibcError = node24GlibcError;
            InContainer = inContainer;
            CustomNodePath = customNodePath;
        }
    }
}