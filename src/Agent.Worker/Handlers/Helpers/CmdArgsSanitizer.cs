// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Agent.Worker.Handlers.Helpers
{
    public static class CmdArgsSanitizer
    {
        private const string _removedSymbolSign = "_#removed#_";
        private const string _argsSplitSymbols = "^^";
        private static readonly Regex _sanitizeRegExp = new("(?<!\\^)([^a-zA-Z0-9\\\\` _''\"\\-=\\/:\\.*,+~?^%])");

        public static (string, CmdArgsSanitizingTelemetry) SanitizeArguments(string inputArgs)
        {
            if (inputArgs == null)
            {
                return (null, null);
            }

            var argsChunks = inputArgs.Split(_argsSplitSymbols);
            var matchesChunks = new List<MatchCollection>();

            for (int i = 0; i < argsChunks.Length; i++)
            {
                var matches = _sanitizeRegExp.Matches(argsChunks[i]);
                if (matches.Count > 0)
                {
                    matchesChunks.Add(matches);
                    argsChunks[i] = _sanitizeRegExp.Replace(argsChunks[i], _removedSymbolSign);
                }
            }

            var resultArgs = string.Join(_argsSplitSymbols, argsChunks);

            CmdArgsSanitizingTelemetry telemetry = null;

            if (resultArgs != inputArgs)
            {
                var symbolsCount = matchesChunks
                                    .Select(chunk => chunk.Count)
                                    .Aggregate(0, (acc, mc) => acc + mc);
                telemetry = new CmdArgsSanitizingTelemetry
                    (
                        RemovedSymbols: CmdArgsSanitizingTelemetry.ToSymbolsDictionary(matchesChunks),
                        RemovedSymbolsCount: symbolsCount
                    );
            }

            return (resultArgs, telemetry);
        }
    }

    public record CmdArgsSanitizingTelemetry
    (
        Dictionary<string, int> RemovedSymbols,
        int RemovedSymbolsCount
    )
    {
        public static Dictionary<string, int> ToSymbolsDictionary(List<MatchCollection> matches)
        {
            ArgUtil.NotNull(matches, nameof(matches));

            var symbolsDict = new Dictionary<string, int>();
            foreach (var mc in matches)
            {
                foreach (var m in mc.Cast<Match>())
                {
                    var symbol = m.Value;
                    if (symbolsDict.TryGetValue(symbol, out _))
                    {
                        symbolsDict[symbol] += 1;
                    }
                    else
                    {
                        symbolsDict[symbol] = 1;
                    }
                }
            }

            return symbolsDict;
        }

        public Dictionary<string, object> ToDictionary()
        {
            return new()
            {
                ["removedSymbols"] = RemovedSymbols,
                ["removedSymbolsCount"] = RemovedSymbolsCount,
            };
        }
    }
}
