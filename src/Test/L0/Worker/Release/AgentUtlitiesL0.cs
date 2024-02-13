// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.Release;

using Xunit;

namespace Test.L0.Worker.Release
{
    public sealed class AgentUtlitiesL0
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void VetGetPrintableEnvironmentVariables()
        {
            List<Variable> variables = new List<Variable>
            {
                new Variable("key.B", "value1", secret: false, readOnly: false, preserveCase: false),
                new Variable("key A", "value2", secret: false, readOnly: false, preserveCase: false),
                new Variable("keyC", "value3", secret: false, readOnly: false, preserveCase: false),
            };
            string expectedResult =
                $"{Environment.NewLine}\t\t\t\t[{FormatVariable(variables[1].Name)}] --> [{variables[1].Value}]"
                + $"{Environment.NewLine}\t\t\t\t[{FormatVariable(variables[0].Name)}] --> [{variables[0].Value}]"
                + $"{Environment.NewLine}\t\t\t\t[{FormatVariable(variables[2].Name)}] --> [{variables[2].Value}]";

            string result = AgentUtilities.GetPrintableEnvironmentVariables(variables);
            Assert.Equal(expectedResult, result);
        }

        private string FormatVariable(string key)
        {
            return key.ToUpperInvariant().Replace(".", "_").Replace(" ", "_");
        }
    }
}