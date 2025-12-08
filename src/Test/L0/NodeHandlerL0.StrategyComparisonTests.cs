// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.Handlers;
using Moq;
using Xunit;
using Agent.Sdk;

namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    /// <summary>
    /// Equivalency and Divergence tests for NodeHandler.
    /// Runs each test scenario on both legacy and unified strategies and compares results.
    /// 
    /// Test Categories:
    /// - Equivalency: Legacy and unified produce the same result
    /// - Divergence: Legacy and unified intentionally differ (EOL policy, removed knobs)
    /// </summary>
    [Collection("Unified NodeHandler Tests")]
    public sealed class NodeHandlerL0StrategyComparisonTests : NodeHandlerTestBase
    {

        /// <summary>
        /// Test equivalency scenarios - where legacy and unified should produce the same result.
        /// </summary>
        [Theory]
        [MemberData(nameof(GetEquivalencyScenarios))]
        [Trait("Level", "L0")]
        [Trait("Category", "NodeHandler")]
        [Trait("Category", "EquivalencyTest")]
        public void NodeHandler_EquivalencyTest_LegacyAndUnifiedProduceSameResult(TestScenario scenario)
        {
            // Skip if this scenario is marked as divergent
            if (!scenario.ShouldMatchBetweenModes)
            {
                return; // This will be tested in divergence tests
            }

            // Test legacy strategy - use base class method
            var legacyResult = RunScenarioForResult(scenario, useUnifiedStrategy: false);
            
            // Test unified strategy - use base class method
            var unifiedResult = RunScenarioForResult(scenario, useUnifiedStrategy: true);

            // Assert they produce the same result
            Assert.Equal(legacyResult.Success, unifiedResult.Success);
            
            if (legacyResult.Success && unifiedResult.Success)
            {
                Assert.Equal(legacyResult.NodePath, unifiedResult.NodePath);
            }
            else if (!legacyResult.Success && !unifiedResult.Success)
            {
                // Both should fail - verify they fail for similar reasons
                Assert.Equal(legacyResult.Exception?.GetType(), unifiedResult.Exception?.GetType());
            }
        }

        /// <summary>
        /// Test divergence scenarios - where legacy and unified intentionally produce different results.
        /// </summary>
        [Theory]
        [MemberData(nameof(GetDivergenceScenarios))]
        [Trait("Level", "L0")]
        [Trait("Category", "NodeHandler")]
        [Trait("Category", "DivergenceTest")]
        public void NodeHandler_DivergenceTest_LegacyAndUnifiedProduceDifferentResults(TestScenario scenario)
        {
            // Skip if this scenario should be equivalent
            if (scenario.ShouldMatchBetweenModes)
            {
                return; // This will be tested in equivalency tests
            }

            // Test legacy strategy - use base class method
            var legacyResult = RunScenarioForResult(scenario, useUnifiedStrategy: false);
            
            // Test unified strategy - use base class method
            var unifiedResult = RunScenarioForResult(scenario, useUnifiedStrategy: true);

            // Verify legacy result matches expected legacy behavior
            if (!string.IsNullOrEmpty(scenario.LegacyExpectedNode))
            {
                Assert.True(legacyResult.Success, $"Legacy should succeed for scenario: {scenario.Name}");
                var expectedLegacyPath = GetExpectedNodePath(scenario.LegacyExpectedNode, scenario);
                Assert.Equal(expectedLegacyPath, legacyResult.NodePath);
            }
            else if (!scenario.LegacyExpectSuccess)
            {
                Assert.False(legacyResult.Success, $"Legacy should fail for scenario: {scenario.Name}");
            }

            // Verify unified result matches expected unified behavior
            if (!string.IsNullOrEmpty(scenario.UnifiedExpectedNode))
            {
                Assert.True(unifiedResult.Success, $"Unified should succeed for scenario: {scenario.Name}");
                var expectedUnifiedPath = GetExpectedNodePath(scenario.UnifiedExpectedNode, scenario);
                Assert.Equal(expectedUnifiedPath, unifiedResult.NodePath);
            }
            else if (!scenario.UnifiedExpectSuccess)
            {
                Assert.False(unifiedResult.Success, $"Unified should fail for scenario: {scenario.Name}");
                
                // Verify error message if specified
                if (!string.IsNullOrEmpty(scenario.UnifiedExpectedError))
                {
                    Assert.Contains(scenario.UnifiedExpectedError, unifiedResult.Exception?.Message ?? "", StringComparison.OrdinalIgnoreCase);
                }
            }

            // Most importantly - verify they are actually different (if both succeed)
            if (legacyResult.Success && unifiedResult.Success)
            {
                Assert.NotEqual(legacyResult.NodePath, unifiedResult.NodePath);
            }
            else if (legacyResult.Success != unifiedResult.Success)
            {
                // Different success/failure is expected for divergence
                Assert.True(true, "Legacy and unified have different success/failure as expected");
            }
        }

        /// <summary>
        /// Get expected node path based on node folder name and scenario.
        /// For custom node paths, returns the custom path exactly as specified.
        /// For standard node paths, constructs the appropriate path.
        /// </summary>
        private string GetExpectedNodePath(string nodeFolderName, TestScenario scenario)
        {
            // For custom node scenarios, return the custom path exactly as specified
            // This matches the behavior in NodeHandlerTestBase.GetExpectedNodeLocation
            if (!string.IsNullOrWhiteSpace(scenario.CustomNodePath))
            {
                return scenario.CustomNodePath;
            }

            // For standard scenarios, check if nodeFolderName looks like a full path or just a folder name
            if (nodeFolderName.Contains('/') || nodeFolderName.Contains('\\'))
            {
                // nodeFolderName is already a full path
                return nodeFolderName;
            }

            // nodeFolderName is a node folder name, build the host path
            using (TestHostContext thc = new TestHostContext(this, "MockForPath"))
            {
                return Path.Combine(
                    thc.GetDirectory(WellKnownDirectory.Externals),
                    nodeFolderName,
                    "bin",
                    $"node{IOUtil.ExeExtension}");
            }
        }

        /// <summary>
        /// Get all scenarios that should produce equivalent results between legacy and unified.
        /// </summary>
        public static object[][] GetEquivalencyScenarios()
        {
            return NodeHandlerTestSpecs.AllScenarios
                .Where(s => s.ShouldMatchBetweenModes)
                .Select(scenario => new object[] { scenario })
                .ToArray();
        }

        /// <summary>
        /// Get all scenarios that should produce different results between legacy and unified.
        /// </summary>
        public static object[][] GetDivergenceScenarios()
        {
            return NodeHandlerTestSpecs.AllScenarios
                .Where(s => !s.ShouldMatchBetweenModes)
                .Select(scenario => new object[] { scenario })
                .ToArray();
        }
    }
}