using Agent.Worker.Handlers.Helpers;
using Xunit;

namespace Test.L0.Worker.Handlers
{
    public sealed class ProcessHandlerHelperTelemetryL0
    {
        [Fact]
        public void FoundPrefixesTest()
        {
            string argsLine = "% % %";
            // we're thinking that whitespaces are also may be env variables, so here the '% %' and '%' env enterances.
            var expectedTelemetry = new { foundPrefixes = 2 };

            var (_, resultTelemetry) = ProcessHandlerHelper.ExpandCmdEnv(argsLine);

            Assert.Equal(expectedTelemetry.foundPrefixes, resultTelemetry.FoundPrefixes);
        }

        [Fact]
        public void NotClosedEnv()
        {
            string argsLine = "%1";
            var expectedTelemetry = new { NotClosedEnvSyntaxPosition = 0 };

            var (_, resultTelemetry) = ProcessHandlerHelper.ExpandCmdEnv(argsLine);

            Assert.Equal(expectedTelemetry.NotClosedEnvSyntaxPosition, resultTelemetry.NotClosedEnvSyntaxPosition);
        }

        [Fact]
        public void NotClosedEnv2()
        {
            string argsLine = "\"%\" %";
            var expectedTelemetry = new { NotClosedEnvSyntaxPosition = 4 };

            var (_, resultTelemetry) = ProcessHandlerHelper.ExpandCmdEnv(argsLine);

            Assert.Equal(expectedTelemetry.NotClosedEnvSyntaxPosition, resultTelemetry.NotClosedEnvSyntaxPosition);
        }

        [Fact]
        public void NotClosedQuotes()
        {
            string argsLine = "\" %var%";
            var expectedTelemetry = new { quotesNotEnclosed = 1 };

            var (_, resultTelemetry) = ProcessHandlerHelper.ExpandCmdEnv(argsLine);

            Assert.Equal(expectedTelemetry.quotesNotEnclosed, resultTelemetry.QuotesNotEnclosed);
        }

        [Fact]
        public void NotClosedQuotes_Ignore_if_no_envVar()
        {
            string argsLine = "\" 1";
            var expectedTelemetry = new { quotesNotEnclosed = 0 };

            var (_, resultTelemetry) = ProcessHandlerHelper.ExpandCmdEnv(argsLine);

            Assert.Equal(expectedTelemetry.quotesNotEnclosed, resultTelemetry.QuotesNotEnclosed);
        }

        [Fact]
        public void QuotedBlocksCount()
        {
            // We're ignoring quote blocks where no any env variables
            string argsLine = "\"%VAR1%\" \"%VAR2%\" \"3\"";
            var expectedTelemetry = new { quottedBlocks = 2 };

            var (_, resultTelemetry) = ProcessHandlerHelper.ExpandCmdEnv(argsLine);

            Assert.Equal(expectedTelemetry.quottedBlocks, resultTelemetry.QuottedBlocks);
        }

        [Fact]
        public void CountsVariablesStartFromEscSymbol()
        {
            string argsLine = "%^VAR1% \"%^VAR2%\" %^VAR3%";
            var expectedTelemetry = new { variablesStartsFromES = 2 };

            var (_, resultTelemetry) = ProcessHandlerHelper.ExpandCmdEnv(argsLine);

            Assert.Equal(expectedTelemetry.variablesStartsFromES, resultTelemetry.VariablesStartsFromES);
        }
    }
}
