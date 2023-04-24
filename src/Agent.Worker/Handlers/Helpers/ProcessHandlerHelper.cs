using System;
using System.Collections.Generic;
using System.Text;

namespace Agent.Worker.Handlers.Helpers
{
    public static class ProcessHandlerHelper
    {
        private static string CmdEnvPrefix { get; } = "%";
        private static string CmdEnvPostfix { get; } = "%";

        public static (string, CmdTelemetry) ProcessInputArguments(string inputArgs)
        {
            char quote = '"';
            char escapingSymbol = '^';

            string result = inputArgs;
            int startIndex = 0;
            var telemetry = new CmdTelemetry();

            while (true)
            {
                int prefixIndex = result.IndexOf(CmdEnvPrefix, startIndex);
                if (prefixIndex < 0)
                {
                    break;
                }

                telemetry.FoundPrefixes++;

                if (prefixIndex > 0 && result[prefixIndex - 1] == escapingSymbol)
                {
                    if (result[prefixIndex - 2] == 0 || result[prefixIndex - 2] != escapingSymbol)
                    {
                        startIndex++;
                        result = result[..(prefixIndex - 1)] + result[prefixIndex..];

                        telemetry.EscapedVariables++;

                        continue;
                    }

                    telemetry.EscapedEscapingSymbols++;
                }

                // We possibly should simplify that part -> if just no close quote, then break
                int quoteIndex = result.IndexOf(quote, startIndex);
                if (quoteIndex >= 0 && prefixIndex > quoteIndex)
                {
                    int nextQuoteIndex = result.IndexOf(quote, quoteIndex + 1);
                    if (nextQuoteIndex < 0)
                    {
                        telemetry.QuotesNotEnclosed = 1;
                        break;
                    }

                    startIndex = nextQuoteIndex + 1;

                    telemetry.QuottedBlocks++;

                    continue;
                }

                int envStartIndex = prefixIndex + CmdEnvPrefix.Length;
                int envEndIndex = FindEnclosingIndex(result, prefixIndex);
                if (envEndIndex == 0)
                {
                    telemetry.NotClosedEnvSyntaxPosition = prefixIndex;
                    break;
                }

                string envName = result[envStartIndex..envEndIndex];

                telemetry.BracedVariables++;

                if (envName.StartsWith(escapingSymbol))
                {
                    var sanitizedEnvName = CmdEnvPrefix + envName[1..] + CmdEnvPostfix;

                    result = result[..prefixIndex] + sanitizedEnvName + result[(envEndIndex + CmdEnvPostfix.Length)..];
                    startIndex = prefixIndex + sanitizedEnvName.Length;

                    telemetry.VariablesStartsFromES++;

                    continue;
                }

                var head = result[..prefixIndex];
                if (envName.Contains(escapingSymbol))
                {
                    head += envName.Split(escapingSymbol)[1];
                    envName = envName.Split(escapingSymbol)[0];

                    telemetry.VariablesWithESInside++;
                }

                var envValue = System.Environment.GetEnvironmentVariable(envName) ?? "";
                var tail = result[(envEndIndex + CmdEnvPostfix.Length)..];

                result = head + envValue + tail;
                startIndex = prefixIndex + envValue.Length;

                telemetry.VariablesExpanded++;

                continue;
            }

            return (result, telemetry);
        }

        private static int FindEnclosingIndex(string input, int targetIndex)
        {
            for (int i = 0; i < input.Length; i++)
            {
                if (input[i] == '%' && i > targetIndex)
                {
                    return i;
                }
            }

            return 0;
        }
    }

    public class CmdTelemetry
    {
        public int FoundPrefixes { get; set; } = 0;
        public int QuottedBlocks { get; set; } = 0;
        public int VariablesExpanded { get; set; } = 0;
        public int EscapedVariables { get; set; } = 0;
        public int EscapedEscapingSymbols { get; set; } = 0;
        public int VariablesStartsFromES { get; set; } = 0;
        public int BraceSyntaxEntries { get; set; } = 0;
        public int BracedVariables { get; set; } = 0;
        public int VariablesWithESInside { get; set; } = 0;
        public int QuotesNotEnclosed { get; set; } = 0;
        public int NotClosedEnvSyntaxPosition { get; set; } = 0;

        public Dictionary<string, int> ToDictionary()
        {
            return new Dictionary<string, int>
            {
                ["foundPrefixes"] = FoundPrefixes,
                ["quottedBlocks"] = QuottedBlocks,
                ["variablesExpanded"] = VariablesExpanded,
                ["escapedVariables"] = EscapedVariables,
                ["escapedEscapingSymbols"] = EscapedEscapingSymbols,
                ["variablesStartsFromES"] = VariablesStartsFromES,
                ["braceSyntaxEntries"] = BraceSyntaxEntries,
                ["bracedVariables"] = BracedVariables,
                ["bariablesWithESInside"] = VariablesWithESInside,
                ["quotesNotEnclosed"] = QuotesNotEnclosed,
                ["notClosedBraceSyntaxPosition"] = NotClosedEnvSyntaxPosition
            };
        }
    };
}
