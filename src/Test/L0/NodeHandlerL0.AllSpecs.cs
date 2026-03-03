// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using System.Runtime.InteropServices;
using Agent.Sdk;
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
            
            // Skip container tests on macOS since they always use cross-platform logic
            // This is expected behavior - macOS agent binaries cannot run in typical Linux containers
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                scenarios = scenarios.Where(s => !s.InContainer).ToList();
            }
            
            return scenarios
                .Select(scenario => new object[] { scenario })
                .ToArray();
        }
    }
}