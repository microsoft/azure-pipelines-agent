// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Xunit;
using Agent.Worker.Handlers.Helpers;

namespace Test.L0.Worker.Handlers
{
    public sealed class ProcessHandlerHelperTelemetryL0
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker.Handlers")]
        public void FoundPrefixesTest()
        {
            string argsLine = "% % %";
            var (_, resultTelemetry) = ProcessHandlerHelper.ExpandCmdEnv(argsLine, new());

            Assert.Equal(2, resultTelemetry.FoundPrefixes);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker.Handlers")]
        public void NotClosedEnv()
        {
            string argsLine = "%1";

            var (_, resultTelemetry) = ProcessHandlerHelper.ExpandCmdEnv(argsLine, new());

            Assert.Equal(0, resultTelemetry.NotClosedEnvSyntaxPosition);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker.Handlers")]
        public void NotClosedEnv2()
        {
            string argsLine = "\"%\" %";

            var (_, resultTelemetry) = ProcessHandlerHelper.ExpandCmdEnv(argsLine, new());

            Assert.Equal(4, resultTelemetry.NotClosedEnvSyntaxPosition);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker.Handlers")]
        public void NotClosedQuotes()
        {
            string argsLine = "\" %var%";

            var (_, resultTelemetry) = ProcessHandlerHelper.ExpandCmdEnv(argsLine, new());

            Assert.Equal(1, resultTelemetry.QuotesNotEnclosed);
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
        public void QuotedBlocksCount()
        {
            // We're ignoring quote blocks where no any env variables
            string argsLine = "\"%VAR1%\" \"%VAR2%\" \"3\"";

            var (_, resultTelemetry) = ProcessHandlerHelper.ExpandCmdEnv(argsLine, new());

            Assert.Equal(2, resultTelemetry.QuottedBlocks);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker.Handlers")]
        public void CountsVariablesStartFromEscSymbol()
        {
            string argsLine = "%^VAR1% \"%^VAR2%\" %^VAR3%";

            var (_, resultTelemetry) = ProcessHandlerHelper.ExpandCmdEnv(argsLine, new());

            Assert.Equal(2, resultTelemetry.VariablesStartsFromES);
        }
    }
}
