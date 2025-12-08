// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
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
        public void NodeHandler_AllScenarios(TestScenario scenario)
        {
            RunScenarioAndAssert(scenario);
        }

        public static object[][] GetAllNodeHandlerScenarios()
        {
            return NodeHandlerTestSpecs.AllScenarios
                .Select(scenario => new object[] { scenario })
                .ToArray();
        }
    }
}