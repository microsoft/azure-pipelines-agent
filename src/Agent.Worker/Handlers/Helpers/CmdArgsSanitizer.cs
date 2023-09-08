using Microsoft.VisualStudio.Services.Agent.Util;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Agent.Worker.Handlers.Helpers
{
    public static class CmdArgsSanitizer
    {
        public static (string, CmdArgsSanitizingTelemetry) SanitizeArguments(string inputArgs)
        {
            if (inputArgs == null)
            {
                return (null, null);
            }

            const string removedSymbolSign = "_#removed#_";
            const string argsSplitSymbols = "^^";

            var argsChunks = inputArgs.Split(argsSplitSymbols);
            var matchesChunks = new List<MatchCollection>();

            var saniziteRegExp = new Regex("(?<!\\^)([^a-zA-Z0-9\\\\` _''\"\\-=\\/:\\.*,+~?%^])");

            for (int i = 0; i < argsChunks.Length; i++)
            {
                var matches = saniziteRegExp.Matches(argsChunks[i]);
                if (matches.Count > 0)
                {
                    matchesChunks.Add(matches);
                    argsChunks[i] = saniziteRegExp.Replace(argsChunks[i], removedSymbolSign);
                }
            }

            var resultArgs = string.Join(argsSplitSymbols, argsChunks);

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
