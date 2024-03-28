// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Xunit;
using System.Collections.Generic;
using Microsoft.VisualStudio.Services.Agent.Worker.Handlers;

namespace Test.L0.Worker.Handlers
{
    public sealed class ProcessHandlerHelperTelemetryL0
    {
        [Theory]
        [InlineData("% % %", 3)]
        [InlineData("%var% %", 2)]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker.Handlers")]
        public void FoundPrefixesTest(string inputArgs, int expectedCount)
        {
            var env = new Dictionary<string, string>
            {
                { "var", "test" }
            };
            var (_, resultTelemetry) = ProcessHandlerHelper.ExpandCmdEnv(inputArgs, env);

            Assert.Equal(expectedCount, resultTelemetry.FoundPrefixes);
        }

        [Theory]
        [InlineData("%1", 0)]
        [InlineData("  %1", 2)]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker.Handlers")]
        public void NotClosedEnv(string inputArgs, int expectedPosition)
        {
            var (_, resultTelemetry) = ProcessHandlerHelper.ExpandCmdEnv(inputArgs, new());

            Assert.Equal(expectedPosition, resultTelemetry.NotClosedEnvSyntaxPosition);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker.Handlers")]
        public void NotClosedQuotes_Ignore_if_no_envVar()
        {
            string argsLine = "\" 1";

            var (_, resultTelemetry) = ProcessHandlerHelper.ExpandCmdEnv(argsLine, new());

            Assert.Equal(0, resultTelemetry.QuotesNotEnclosed);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker.Handlers")]
        public void CountsVariablesStartFromEscSymbol()
        {
            string argsLine = "%^VAR1% \"%^VAR2%\" %^VAR3%";

            var (_, resultTelemetry) = ProcessHandlerHelper.ExpandCmdEnv(argsLine, new());

            Assert.Equal(3, resultTelemetry.VariablesStartsFromES);
        }
    }
}
