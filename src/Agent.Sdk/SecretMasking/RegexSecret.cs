using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Agent.Sdk.SecretMasking;

internal sealed class RegexSecret : ISecret
{
    public RegexSecret(String pattern)
    {
        ArgUtil.NotNullOrEmpty(pattern, nameof(pattern));
        m_pattern = pattern;
        m_regex = new Regex(pattern, _regexOptions);
    }

    public override Boolean Equals(Object obj)
    {
        var item = obj as RegexSecret;
        if (item == null)
        {
            return false;
        }
        return String.Equals(m_pattern, item.m_pattern, StringComparison.Ordinal);
    }

    public override int GetHashCode() => m_pattern.GetHashCode();

    public IEnumerable<ReplacementPosition> GetPositions(String input)
    {
        Int32 startIndex = 0;
        while (startIndex < input.Length)
        {
            var match = m_regex.Match(input, startIndex);
            if (match.Success)
            {
                startIndex = match.Index + 1;
                yield return new ReplacementPosition(match.Index, match.Length);
            }
            else
            {
                yield break;
            }
        }
    }

    public string Pattern { get { return m_pattern; } }
    private readonly String m_pattern;
    private readonly Regex m_regex;
    private static readonly RegexOptions _regexOptions = RegexOptions.Compiled | RegexOptions.ExplicitCapture;
}