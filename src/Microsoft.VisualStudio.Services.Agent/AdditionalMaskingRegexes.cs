// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.VisualStudio.Services.Agent
{
    public static partial class AdditionalMaskingRegexes
    {
        /// <summary>
        /// Regexp for unreserved characters - for more details see https://datatracker.ietf.org/doc/html/rfc3986#section-2.3
        /// </summary>
        private const string unreservedCharacters = @"[\w\.~\-]";
        /// <summary>
        /// Regexp for percent encoded characters - for more details see https://datatracker.ietf.org/doc/html/rfc3986#section-2.1
        /// </summary>
        private const string percentEncoded = @"(%|%AZP25)[0-9a-fA-F]{2}";
        /// <summary>
        /// Regexp for delimeters - for more details see https://datatracker.ietf.org/doc/html/rfc3986#section-2.2
        /// </summary>
        private const string subDelims = @"[!\$&'\(\)\*\+,;=]";
        /// <summary>
        /// Match regexp for url
        /// </summary>
        private static string urlMatch = string.Format("({0}|{1}|{2}|:)+", unreservedCharacters, percentEncoded, subDelims);

        // URLs can contain secrets if they have a userinfo part
        // in the authority. example: https://user:pass@example.com
        // (see https://tools.ietf.org/html/rfc3986#section-3.2)
        // This regex will help filter those out of the output.
        // It uses a zero-width positive lookbehind to find the scheme,
        // the user, and the ":" and skip them. Similarly, it uses
        // a zero-width positive lookahead to find the "@".
        // It only matches on the password part.
        private static string urlSecretPattern
            = "(?<=//[^:/?#\\n]+:)" // lookbehind
            + urlMatch       // actual match
            + "(?=@)";              // lookahead

        public static string UrlSecretPattern => urlSecretPattern;
    }
}