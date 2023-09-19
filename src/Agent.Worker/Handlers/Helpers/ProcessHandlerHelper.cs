// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace Agent.Worker.Handlers.Helpers
{
    public static class ProcessHandlerHelper
    {
        public static (string, CmdTelemetry) ExpandCmdEnv(string inputArgs)
        {
            const char quote = '"';
            const char escapingSymbol = '^';
            const string envPrefix = "%";
            const string envPostfix = "%";

            string result = inputArgs;
            int startIndex = 0;
            var telemetry = new CmdTelemetry();

            while (true)
            {
                int prefixIndex = result.IndexOf(envPrefix, startIndex);
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

                int envStartIndex = prefixIndex + envPrefix.Length;
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
                    var sanitizedEnvName = envPrefix + envName[1..] + envPostfix;

                    result = result[..prefixIndex] + sanitizedEnvName + result[(envEndIndex + envPostfix.Length)..];
                    startIndex = prefixIndex + sanitizedEnvName.Length;

                    telemetry.VariablesStartsFromES++;

                    continue;
                }

                var head = result[..prefixIndex];
                if (envName.Contains(escapingSymbol, StringComparison.Ordinal))
                {
                    head += envName.Split(escapingSymbol)[1];
                    envName = envName.Split(escapingSymbol)[0];

                    telemetry.VariablesWithESInside++;
                }

                var envValue = Environment.GetEnvironmentVariable(envName);
                // In case we don't have such variable, we just leave it as is
                if (string.IsNullOrEmpty(envValue))
                {
                    telemetry.NotExistingEnv++;
                    startIndex = envEndIndex;
                    continue;
                }

                var tail = result[(envEndIndex + envPostfix.Length)..];

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
        public int NotExistingEnv { get; set; } = 0;

        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>
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
                ["notClosedBraceSyntaxPosition"] = NotClosedEnvSyntaxPosition,
                ["notExistingEnv"] = NotExistingEnv
            };
        }
    };
}
