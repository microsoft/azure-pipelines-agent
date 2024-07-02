// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Agent.Sdk.SecretMasking;

internal sealed class RegexSecret : ISecret
{
    public RegexSecret(String pattern,
                       string moniker = null,
                       ISet<string> sniffLiterals = null,
                       RegexOptions regexOptions = RegexOptions.Compiled | RegexOptions.ExplicitCapture)
    {
        ArgUtil.NotNullOrEmpty(pattern, nameof(pattern));

        Pattern = pattern;
        Moniker = moniker;
        m_regexOptions = regexOptions;
        m_sniffLiterals = sniffLiterals;
        m_regex = new Regex(pattern, regexOptions);
    }

    public override Boolean Equals(Object obj)
    {
        var item = obj as RegexSecret;
        if (item == null)
        {
            return false;
        }

        if (!String.Equals(Pattern, item.Pattern, StringComparison.Ordinal) ||
            !String.Equals(Moniker, item.Moniker, StringComparison.Ordinal) ||
            m_regexOptions != item.m_regexOptions)
        {
            return false;
        }

        if (m_sniffLiterals == null && item.m_sniffLiterals == null)
        {
            return true;
        }

        if (m_sniffLiterals.Count !=  item.m_sniffLiterals.Count)
        {
            return false; 
        }

        foreach (string sniffLiteral in m_sniffLiterals) 
        { 
            if (!item.m_sniffLiterals.Contains(sniffLiteral))
            {
                return false;
            }
        }

        return true;
    }

    public override int GetHashCode()
    {
        int result = 17;
        unchecked
        {
            result = (result * 31) + Pattern.GetHashCode();

            if (Moniker != null)
            {
                result = (result * 31) + Moniker.GetHashCode();
            }

            result = (result * 31) + m_regexOptions.GetHashCode();

            // Use xor for set values to be order-independent.
            if (m_sniffLiterals != null)
            {
                int xor_0 = 0;
                foreach (var sniffLiteral in m_sniffLiterals)
                {
                    xor_0 ^= sniffLiteral.GetHashCode();
                }
                result = (result * 31) + xor_0;
            }
        }

        return result;
    }

    public IEnumerable<Replacement> GetReplacements(String input)
    {
        bool runRegexes = m_sniffLiterals == null ? true : false;

        if (m_sniffLiterals != null)
        {
            foreach (string sniffLiteral in m_sniffLiterals)
            {
                if (input.IndexOf(sniffLiteral, StringComparison.Ordinal) != -1)
                {
                    runRegexes = true;
                    break;
                }
            }
        }

        if (runRegexes)
        {
            Int32 startIndex = 0;
            while (startIndex < input.Length)
            {
                var match = m_regex.Match(input, startIndex);
                if (match.Success)
                {
                    startIndex = match.Index + 1;
                    string token = Moniker != null
                        ? CreateTelemetryForMatch(Moniker, match.Value)
                        : "+++";
                    yield return new Replacement(match.Index, match.Length, token);
                }
                else
                {
                    yield break;
                }
            }
        }
    }

    private string CreateTelemetryForMatch(string moniker, string value)
    {
        using var sha = SHA256.Create();
        byte[] byteHash = Encoding.UTF8.GetBytes(value);
        byte[] checksum = sha.ComputeHash(byteHash);

        string hashedValue = BitConverter.ToString(checksum).Replace("-", string.Empty, StringComparison.Ordinal);

        return $"{moniker}:{hashedValue}";
    }

    internal static string HashString(string value)
    {
        byte[] byteHash = Encoding.UTF8.GetBytes(value);
        byte[] checksum = s_sha256.ComputeHash(byteHash);
        return BitConverter.ToString(checksum).Replace("-", string.Empty, StringComparison.Ordinal);
    }

    public string Pattern { get; private set; }
    public string Moniker { get; private set; }

    private readonly ISet<string> m_sniffLiterals;
    private readonly RegexOptions m_regexOptions;
    private readonly Regex m_regex;

    private static readonly SHA256 s_sha256 = SHA256.Create();
}