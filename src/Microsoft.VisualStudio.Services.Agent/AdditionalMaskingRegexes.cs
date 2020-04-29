// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.VisualStudio.Services.Agent
{
    internal static partial class AdditionalMaskingRegexes
    {
        // URLs can contain secrets if they have a userinfo part
        // in the authority. example: https://user:pass@example.com
        // (see https://tools.ietf.org/html/rfc3986#section-3.2)
        // This regex will help filter those out of the output.
        // It uses a zero-width positive lookbehind to find the scheme,
        // the user, and the ":" and skip them. Similarly, it uses
        // a zero-width positive lookahead to find the "@".
        // It only matches on the password part.
        private const string urlSecretPattern
            = "(?<=//[^:/?#\\n]+:)" // lookbehind
            + "[^@\n]+"             // actual match
            + "(?=@)";              // lookahead

        public static string UrlSecretPattern => urlSecretPattern;
    }
}