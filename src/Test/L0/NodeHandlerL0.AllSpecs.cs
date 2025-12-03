// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    /// <summary>
    /// Unified test runner for ALL NodeHandler test specifications.
    /// Executes every scenario defined in NodeHandlerTestSpecs.AllScenarios.
    /// 
    /// This provides a single place to run all Node strategy tests covering:
    /// - Node24, Node20, Node16, Node10, Node6 strategies
    /// - EOL policy behavior
    /// - Global knob overrides
    /// - Glibc fallback scenarios
    /// - Container vs host execution
    /// </summary>
    [Trait("Level", "L0")]
    [Trait("Category", "NodeHandler")]
    [Collection("Unified NodeHandler Tests")]
    public sealed class NodeHandlerL0AllSpecs : NodeHandlerTestBase
    {
        /// <summary>
        /// Execute ALL NodeHandler test scenarios from the centralized specifications.
        /// Each test runs independently with clean environment setup/teardown.
        /// </summary>
        [Theory]
        [MemberData(nameof(GetAllNodeHandlerScenarios))]
        public void NodeHandler_AllScenarios(TestScenario scenario)
        {
            // Simple test execution - NodeHandlerTestBase handles all the setup/teardown
            RunScenarioAndAssert(scenario);
        }

        /// <summary>
        /// Provide ALL NodeHandler test scenarios for the theory test.
        /// This includes Node24, Node20, Node16, Node10, Node6, and cross-cutting scenarios.
        /// </summary>
        public static object[][] GetAllNodeHandlerScenarios()
        {
            return NodeHandlerTestSpecs.AllScenarios
                .Select(scenario => new object[] { scenario })
                .ToArray();
        }
    }
}