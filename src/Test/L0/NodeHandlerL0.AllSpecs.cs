// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Agent.Sdk;
using Agent.Sdk.Knob;
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

        [Fact]
        public void ScenarioExecutionContext_IgnoresAmbientNodeOverrides()
        {
            string previousUseNode24 = Environment.GetEnvironmentVariable("AGENT_USE_NODE24");

            try
            {
                Environment.SetEnvironmentVariable("AGENT_USE_NODE24", "true");

                using TestHostContext thc = new TestHostContext(this);
                var executionContext = CreateTestExecutionContext(
                    thc,
                    new Dictionary<string, string>
                    {
                        ["AGENT_USE_NODE20_1"] = "true"
                    });

                Assert.True(AgentKnobs.UseNode20_1.GetValue(executionContext.Object).AsBoolean());
                Assert.False(AgentKnobs.UseNode24.GetValue(executionContext.Object).AsBoolean());
            }
            finally
            {
                Environment.SetEnvironmentVariable("AGENT_USE_NODE24", previousUseNode24);
            }
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