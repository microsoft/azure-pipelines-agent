// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.Services.Agent.Util;

namespace Agent.Sdk
{
    public sealed class CommandStringConvertor
    {
        public static string Escape(string input, bool unescapePercents)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            string escaped = input;

            if (unescapePercents)
            {
                escaped = escaped.Replace("%", "%AZP25");
            }

            foreach (EscapeMapping mapping in _specialSymbolsMapping)
            {
                escaped = escaped.Replace(mapping.Token, mapping.Replacement);
            }

            return escaped;
        }

        public static string Unescape(string escaped, bool unescapePercents)
        {
            if (string.IsNullOrEmpty(escaped))
            {
                return string.Empty;
            }

            string unescaped = escaped;
            
            foreach (EscapeMapping mapping in _specialSymbolsMapping)
            {
                unescaped = unescaped.Replace(mapping.Replacement, mapping.Token);
            }

            if (unescapePercents)
            {
                unescaped = unescaped.Replace("%AZP25", "%");
            }

            return unescaped;
        }

        private static readonly EscapeMapping[] _specialSymbolsMapping = new[]
        {
            new EscapeMapping(token: ";", replacement: "%3B"),
            new EscapeMapping(token: "\r", replacement: "%0D"),
            new EscapeMapping(token: "\n", replacement: "%0A"),
            new EscapeMapping(token: "]", replacement: "%5D")
        };

        private sealed class EscapeMapping
        {
            public string Replacement { get; }
            public string Token { get; }

            public EscapeMapping(string token, string replacement)
            {
                ArgUtil.NotNullOrEmpty(token, nameof(token));
                ArgUtil.NotNullOrEmpty(replacement, nameof(replacement));
                Token = token;
                Replacement = replacement;
            }
        }

    }
}
