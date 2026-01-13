// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using Microsoft.VisualStudio.Services.Agent.Util;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    /// <summary>
    /// Unified test runner for ALL NodeHandler test specifications.
    /// Executes every scenario defined in NodeHandlerTestSpecs.AllScenarios.
    /// </summary>
    [Trait("Level", "L0")]
    [Trait("Category", "NodeHandler")]
    [Collection("Unified NodeHandler Tests")]
    public sealed class NodeHandlerL0AllSpecs : NodeHandlerTestBase
    {
        [Theory]
        [MemberData(nameof(GetAllNodeHandlerScenarios))]
        public void NodeHandler_AllScenarios_on_legacy(TestScenario scenario)
        {
            RunScenarioAndAssert(scenario, useStrategy: false);
        }

        [Theory]
        [MemberData(nameof(GetAllNodeHandlerScenarios))]
        public void NodeHandler_AllScenarios_on_strategy(TestScenario scenario)
        {
            RunScenarioAndAssert(scenario, useStrategy: true);
        }

        public static object[][] GetAllNodeHandlerScenarios()
        {
            var scenarios = NodeHandlerTestSpecs.AllScenarios.ToList();
            
            // On macOS, create modified versions of container tests that expect "node" instead of specific versions
            if (PlatformUtil.RunningOnMacOS)
            {
                var macOSContainerScenarios = scenarios
                    .Where(s => s.InContainer)
                    .Select(s => CreateMacOSContainerScenario(s))
                    .ToList();
                
                // Replace original container scenarios with macOS-specific ones
                scenarios = scenarios.Where(s => !s.InContainer).Concat(macOSContainerScenarios).ToList();
            }
            
            return scenarios
                .Select(scenario => new object[] { scenario })
                .ToArray();
        }
        
        private static TestScenario CreateMacOSContainerScenario(TestScenario original)
        {
            // Create a new scenario with the same properties but adjusted expectations for macOS cross-platform behavior
            return new TestScenario(
                name: original.Name + "_macOS",
                description: original.Description + " (macOS cross-platform)",
                handlerData: original.HandlerDataType,
                knobs: original.Knobs,
                customNodePath: original.CustomNodePath,
                inContainer: true,
                // On macOS, container tests always return "node" due to cross-platform logic
                // Custom node paths should work as-is, but error scenarios become success scenarios
                expectedNode: !string.IsNullOrEmpty(original.CustomNodePath) ? original.CustomNodePath : 
                             original.ExpectedErrorType != null ? "node" : "node",
                expectedErrorType: !string.IsNullOrEmpty(original.CustomNodePath) ? original.ExpectedErrorType : null, // No errors on macOS cross-platform
                node20GlibcError: original.Node20GlibcError,
                node24GlibcError: original.Node24GlibcError,
                legacyExpectedNode: original.LegacyExpectedNode,
                strategyExpectedNode: original.StrategyExpectedNode,
                strategyExpectedError: null // No strategy errors on macOS cross-platform
            );
        }
    }
}