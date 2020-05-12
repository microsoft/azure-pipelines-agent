using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CredScanRegexes
{
    static class PatternProcessor
    {
        // if a CredScan pattern has a named group, then the credential
        // is assumed to be in that group. otherwise, the entire pattern
        // is the credential. the azure pipelines agent assumes the whole
        // pattern is the credential to suppress, so we need to doctor up
        // CredScan patterns with non-matching groups.
        public static (string Prematch, string Match, string Postmatch) Convert(string name, string pattern)
        {
            // TODO: "(?<" also starts things other than named groups
            // so this should probably be a regex looking for that
            // pattern followed by [A-Za-z]

            // if there's no named group or this shouldn't be processed,
            // use the pattern as-is
            if (pattern.IndexOf("(?<") == -1 || unprocessedPatternNames.Contains(name))
            {
                return
                (
                    string.Empty,
                    EscapeQuotesForLiteralString(pattern),
                    string.Empty
                );
            }

            // finding the beginning of the named capture group is easy
            int startNamedGroup = pattern.IndexOf("(?<");

            // finding the end means looking for the matching close-paren
            int endNamedGroup = FindClosingParen(pattern, startNamedGroup);

            string prematch = string.Empty;
            if (startNamedGroup > 0)
            {
                // nonmatching lookbehind
                prematch = $"(?<={pattern.Substring(0, startNamedGroup)})";
            }

            // the matching group
            string match = $"{pattern.Substring(startNamedGroup, endNamedGroup - startNamedGroup)}";

            string postmatch = string.Empty;
            if (endNamedGroup < pattern.Length)
            {
                // nonmatching lookahead
                postmatch = $"(?={pattern.Substring(endNamedGroup)})";
            }

            return
            (
                EscapeQuotesForLiteralString(prematch),
                EscapeQuotesForLiteralString(match),
                EscapeQuotesForLiteralString(postmatch)
            );
        }

        private static int FindClosingParen(string pattern, int startIndex)
        {
            int closingParenIndex = startIndex + 1;
            int parenCount = 1;
            while (parenCount > 0 && closingParenIndex < pattern.Length)
            {
                string letter = pattern.Substring(closingParenIndex, 1);
                if (letter == "(")
                {
                    parenCount++;
                }
                else if (letter == ")")
                {
                    parenCount--;
                }
                closingParenIndex++;
            }

            return closingParenIndex;
        }

        private static string EscapeQuotesForLiteralString(string input)
        {
            return input.Replace("\"", "\"\"");
        }

        private static List<string> unprocessedPatternNames = new List<string>
        {
            // uses a named capture group twice, which confuses the rudimentary
            // processing implemented here. JsonWebToken doesn't over-match, so
            // no need to process it
            "JsonWebToken",
        };
    }
}