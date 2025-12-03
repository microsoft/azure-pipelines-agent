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
    /// This file defines ALL test scenarios for node version selection in a declarative format.
    /// Each scenario specifies:
    /// - Handler data type (what task declares)
    /// - Environment knobs (global overrides)
    /// - Runtime conditions (glibc errors, container context)
    /// - Expected behavior (which node version is selected)
    /// - Whether behavior differs between legacy and unified strategy
    /// 
    /// Organization:
    /// 1. Node6 scenarios (NodeHandlerData)
    /// 2. Node10 scenarios (Node10HandlerData)
    /// 3. Node16 scenarios (Node16HandlerData)
    /// 4. Node20 scenarios (Node20_1HandlerData)
    /// 5. Node24 scenarios (Node24HandlerData)
    /// 6. Cross-cutting scenarios (custom paths, container contexts, fallbacks)
    /// </summary>
    public static class NodeHandlerTestSpecs
    {
        /// <summary>
        /// All test scenarios. This is the single source of truth for NodeHandler behavior.
        /// </summary>
        public static readonly TestScenario[] AllScenarios = new[]
        {
            // ============================================
            // GROUP 2: Node10 (Node10HandlerData) Scenarios - END-OF-LIFE
            // ============================================
            new TestScenario(
                name: "Node10_DefaultBehavior_EOLPolicyDisabled",
                description: "Node10 handler uses Node10 when EOL policy is disabled",
                handlerData: typeof(Node10HandlerData),
                knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "false" },
                expectedNode: "node10",
                expectSuccess: true,
                shouldMatchBetweenModes: true,
                category: TestCategory.DefaultBehavior
            ),

            new TestScenario(
                name: "Node10_EOLPolicyEnabled_UpgradesToNode24",
                description: "Node10 handler automatically upgrades to Node24 when EOL policy is enabled",
                handlerData: typeof(Node10HandlerData),
                knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true" },
                expectedNode: "node24", // Should upgrade to Node24
                expectSuccess: true,
                shouldMatchBetweenModes: false, // Different upgrade logic
                category: TestCategory.EOLOverride
            ),

            // ============================================
            // GROUP 1: Node6 (NodeHandlerData) Scenarios - END-OF-LIFE
            // ============================================
            new TestScenario(
                name: "Node6_DefaultBehavior_EOLPolicyDisabled",
                description: "Node6 handler uses Node6 when EOL policy is disabled",
                handlerData: typeof(NodeHandlerData),
                knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "false" },
                expectedNode: "node",
                expectSuccess: true,
                shouldMatchBetweenModes: true,
                category: TestCategory.DefaultBehavior
            ),

            new TestScenario(
                name: "Node6_EOLPolicyEnabled_UpgradesToNode24",
                description: "Node6 handler automatically upgrades to Node24 when EOL policy is enabled",
                handlerData: typeof(NodeHandlerData),
                knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true" },
                expectedNode: "node24", // Should upgrade to Node24
                expectSuccess: true,
                shouldMatchBetweenModes: false, // Different upgrade logic
                category: TestCategory.EOLOverride
            ),

            // ============================================
            // GROUP 4: Node20 (Node20_1HandlerData) Scenarios
            // ============================================
            // right
            new TestScenario(
                name: "Node20_DefaultBehavior_WithHandler",
                description: "Node20 handler uses Node20 by default",
                handlerData: typeof(Node20_1HandlerData),
                knobs: new() { },
                expectedNode: "node20_1",
                expectSuccess: true,
                shouldMatchBetweenModes: true,
                category: TestCategory.DefaultBehavior
            ),
            // right
            new TestScenario(
                name: "Node20_WithGlobalUseNode20Knob", // checks
                description: "AGENT_USE_NODE20_1=true forces Node20 regardless of handler data",
                handlerData: typeof(Node20_1HandlerData), // Different handler
                knobs: new() { ["AGENT_USE_NODE20_1"] = "true" },
                expectedNode: "node20_1",
                expectSuccess: true,
                shouldMatchBetweenModes: true,
                category: TestCategory.KnobOverride
            ),
            // right
            new TestScenario(
                name: "Node20_GlibcError_FallsBackToNode16",
                description: "Node20 with glibc error falls back to Node16",
                handlerData: typeof(Node20_1HandlerData),
                knobs: new() { },
                node20GlibcError: true,
                expectedNode: "node16",
                expectSuccess: true,
                shouldMatchBetweenModes: true,
                category: TestCategory.GlibcFallback
            ),
            // right
            // new TestScenario(
            //     name: "Node20_GlibcError_EOLPolicy_ThrowsError",
            //     description: "Node20 with glibc error and EOL policy enabled throws error (cannot fallback to Node16)",
            //     handlerData: typeof(Node20_1HandlerData),
            //     knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true" },
            //     node20GlibcError: true,
            //     expectSuccess: false,
            //     expectedErrorType: typeof(NotSupportedException),
            //     unifiedExpectedError: "would fallback to Node16 (EOL) but EOL policy is enabled",
            //     shouldMatchBetweenModes: false, // Different EOL policy behavior
            //     category: TestCategory.EOLDivergence
            // ),
            // Fix the Node20_GlibcError_EOLPolicy_ThrowsError test specification
            new TestScenario(
                name: "Node20_GlibcError_EOLPolicy_ThrowsError",
                description: "Node20 with glibc error and EOL policy enabled throws error (cannot fallback to Node16)",
                handlerData: typeof(Node20_1HandlerData),
                knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true" },
                node20GlibcError: true,
                expectSuccess: false,
                expectedErrorType: typeof(NotSupportedException),
                unifiedExpectedError: "would fallback to Node16 (EOL) but EOL policy is enabled",
                shouldMatchBetweenModes: false, // Different EOL policy behavior
                category: TestCategory.EOLDivergence
            ),
            // wrong - node 20 handler tpye, case of an old task with node 10 handler
            // either remove this test
            // or expected whould be not supported error
            new TestScenario(
                name: "Node20_EOLUpgrade_FromOldHandler",
                description: "Node10HandlerData with EOL policy upgrades to Node24 (highest priority)",
                handlerData: typeof(Node10HandlerData), // Old handler
                knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true" },
                expectedNode: "node24", // Should upgrade to Node24 (highest priority)
                expectSuccess: true,
                shouldMatchBetweenModes: false, // Different upgrade logic
                category: TestCategory.EOLOverride
            ),

            new TestScenario(
                name: "Node20_WithGlobalUseNode24Knob",
                description: "AGENT_USE_NODE24=true global override forces Node24 even with Node20 handler",
                handlerData: typeof(Node20_1HandlerData),
                knobs: new() { ["AGENT_USE_NODE24"] = "true" },
                expectedNode: "node24", // Global Node24 knob has highest priority
                expectSuccess: true,
                shouldMatchBetweenModes: true,
                category: TestCategory.KnobOverride
            ),

            new TestScenario(
                name: "Node20_WithUseNode10Knob",
                description: "Node20 handler ignores deprecated AGENT_USE_NODE10 knob in unified strategy",
                handlerData: typeof(Node20_1HandlerData),
                knobs: new() { ["AGENT_USE_NODE10"] = "true" },
                expectedNode: "node20_1", // Should stay with Node20 (deprecated knob ignored)
                expectSuccess: true,
                legacyExpectedNode: "node10", // Legacy honored deprecated knob
                legacyExpectSuccess: true,
                unifiedExpectedNode: "node20_1", // Unified ignores deprecated knob
                unifiedExpectSuccess: true,
                shouldMatchBetweenModes: false,
                category: TestCategory.EOLDivergence
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
                shouldMatchBetweenModes: true,
                category: TestCategory.KnobPriority
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
                shouldMatchBetweenModes: true,
                category: TestCategory.KnobPriority
            ),

            // ============================================
            // GROUP 3: Node16 (Node16HandlerData) Scenarios - END-OF-LIFE
            // ============================================
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
                shouldMatchBetweenModes: true,
                category: TestCategory.DefaultBehavior
            ),

            new TestScenario(
                name: "Node16_DefaultEOLPolicy_AllowsNode16",
                description: "Node16 handler uses Node16 when EOL policy is default (disabled)",
                handlerData: typeof(Node16HandlerData),
                knobs: new() { }, // Default EOL policy is false
                expectedNode: "node16",
                expectSuccess: true,
                shouldMatchBetweenModes: true,
                category: TestCategory.DefaultBehavior
            ),

            new TestScenario(
                name: "Node16_EOLPolicyEnabled_UpgradesToNode24",
                description: "Node16 handler automatically upgrades to Node24 when EOL policy is enabled",
                handlerData: typeof(Node16HandlerData),
                knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true" },
                expectedNode: "node24", // Should upgrade to Node24
                expectSuccess: true, // Should succeed with upgrade
                shouldMatchBetweenModes: false, // Different upgrade logic
                category: TestCategory.EOLOverride
            ),

            new TestScenario(
                name: "Node16_InContainer_EOLPolicyDisabled",
                description: "Node16 works in containers when EOL policy is disabled",
                handlerData: typeof(Node16HandlerData),
                knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "false" },
                expectedNode: "node16", 
                expectSuccess: true,
                inContainer: true,
                shouldMatchBetweenModes: true,
                category: TestCategory.Container
            ),

            new TestScenario(
                name: "Node16_InContainer_EOLPolicyEnabled_UpgradesToNode24",
                description: "Node16 upgrades to Node24 in containers when EOL policy is enabled",
                handlerData: typeof(Node16HandlerData),
                knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true" },
                expectedNode: "node24", // Should upgrade to Node24
                expectSuccess: true, // Should succeed with upgrade
                inContainer: true,
                shouldMatchBetweenModes: false, // Different upgrade logic
                category: TestCategory.Container
            ),
            
            // new TestScenario(
                
            // ),


            // ============================================
            // GROUP 5: Node24 (Node24HandlerData) Scenarios
            // ============================================
            
            new TestScenario(
                name: "Node24_DefaultBehavior_WithKnobEnabled",
                description: "Node24 handler uses Node24 when AGENT_USE_NODE24_WITH_HANDLER_DATA=true",
                handlerData: typeof(Node24HandlerData),
                knobs: new() { ["AGENT_USE_NODE24_WITH_HANDLER_DATA"] = "true" },
                expectedNode: "node24",
                expectSuccess: true,
                shouldMatchBetweenModes: true,
                category: TestCategory.DefaultBehavior
            ),
            
            new TestScenario(
                name: "Node24_WithHandlerDataKnobDisabled_FallsBackToNode20",
                description: "Node24 handler falls back to Node20 when AGENT_USE_NODE24_WITH_HANDLER_DATA=false",
                handlerData: typeof(Node24HandlerData),
                knobs: new() { ["AGENT_USE_NODE24_WITH_HANDLER_DATA"] = "false" },
                expectedNode: "node20_1", // Should fallback to Node20
                expectSuccess: true,
                shouldMatchBetweenModes: true,
                category: TestCategory.HandlerDataKnob
            ),
            
            new TestScenario(
                name: "Node24_WithGlobalUseNode24Knob",
                description: "AGENT_USE_NODE24=true forces Node24 regardless of handler data knob",
                handlerData: typeof(Node24HandlerData),
                knobs: new() { ["AGENT_USE_NODE24"] = "true" },
                expectedNode: "node24",
                expectSuccess: true,
                shouldMatchBetweenModes: true,
                category: TestCategory.KnobOverride
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
                expectedNode: "node24", // Should use Node24 (deprecated knob ignored)
                expectSuccess: true,
                legacyExpectedNode: "node10", // Legacy behavior honored deprecated knob
                legacyExpectSuccess: true,
                unifiedExpectedNode: "node24", // Unified ignores deprecated knob
                unifiedExpectSuccess: true,
                shouldMatchBetweenModes: false,
                category: TestCategory.EOLDivergence
            ),
            
            new TestScenario(
                name: "Node24_WithUseNode20Knob",
                description: "Node24 handler with AGENT_USE_NODE20_1=true still uses Node24 (handler knob takes precedence)",
                handlerData: typeof(Node24HandlerData),
                knobs: new() 
                { 
                    ["AGENT_USE_NODE24_WITH_HANDLER_DATA"] = "true",
                    ["AGENT_USE_NODE20_1"] = "true"
                },
                expectedNode: "node24", // Handler knob + handler data should override global knob
                expectSuccess: true,
                shouldMatchBetweenModes: true,
                category: TestCategory.KnobPriority
            ),
            
            new TestScenario(
                name: "Node24_GlibcError_FallsBackToNode20",
                description: "Node24 with glibc error falls back to Node20",
                handlerData: typeof(Node24HandlerData),
                knobs: new() { ["AGENT_USE_NODE24_WITH_HANDLER_DATA"] = "true" },
                node24GlibcError: true,
                expectedNode: "node20_1",
                expectSuccess: true,
                shouldMatchBetweenModes: true,
                category: TestCategory.GlibcFallback
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
                shouldMatchBetweenModes: true,
                category: TestCategory.GlibcFallback
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
                expectSuccess: false,
                expectedErrorType: typeof(NotSupportedException),
                unifiedExpectedError: "No compatible Node.js version available for host execution. Handler type: Node24HandlerData. This may occur if all available versions are blocked by EOL policy. Please update your pipeline to use Node20 or Node24 tasks. To temporarily disable EOL policy: Set AGENT_ENABLE_EOL_NODE_VERSION_POLICY=false",
                shouldMatchBetweenModes: false, // Legacy doesn't have this EOL policy check
                category: TestCategory.EOLDivergence
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
                shouldMatchBetweenModes: true,
                category: TestCategory.KnobPriority
            ),

            new TestScenario(
                name: "Node24_EOLUpgrade_FromOldHandler",
                description: "Node10HandlerData with EOL policy upgrades to Node24 (highest priority strategy)",
                handlerData: typeof(Node10HandlerData), // Old handler
                knobs: new() { ["AGENT_ENABLE_EOL_NODE_VERSION_POLICY"] = "true" },
                expectedNode: "node24", // Should upgrade to Node24
                expectSuccess: true,
                shouldMatchBetweenModes: false, // Different upgrade logic
                category: TestCategory.EOLOverride
            ),
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
    }

    /// <summary>
    /// Test scenario specification.
    /// </summary>
    public class TestScenario
    {
        // Identification
        public string Name { get; set; }
        public string Description { get; set; }
        public TestCategory Category { get; set; }
        
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
            bool allNodesUnavailable = false,
            TestCategory category = TestCategory.DefaultBehavior)
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
            Category = category;
        }
        
        public override string ToString() => Name; // For test display names
    }

    /// <summary>
    /// Test categories for organization and filtering.
    /// </summary>
    public enum TestCategory
    {
        DefaultBehavior,       // Basic handler → version mapping
        KnobOverride,          // Global knobs override handler data
        KnobPriority,          // Knob precedence testing
        HandlerDataKnob,       // AGENT_USE_NODE24_WITH_HANDLER_DATA specific
        HandlerOwnership,      // Strategy ownership of specific handler types
        EOLPolicy,             // AGENT_ENABLE_EOL_NODE_VERSION_POLICY scenarios
        EOLDivergence,         // Scenarios where EOL policy causes different behavior
        EOLOverride,           // AGENT_ENABLE_EOL_NODE_VERSION_POLICY scenarios
        GlibcFallback,         // Glibc error fallback scenarios
        Container,             // Container-specific scenarios
        CustomPath,            // Custom node path scenarios
        UseNodeFallback,       // AGENT_USE_NODE=LTS/UPGRADE scenarios
        ErrorHandling,         // Exception/error scenarios
        EdgeCase               // Edge case scenarios (single handler, unusual configurations)
    }
}
