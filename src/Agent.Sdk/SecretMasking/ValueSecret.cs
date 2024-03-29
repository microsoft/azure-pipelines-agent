﻿using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Agent.Sdk.SecretMasking;

internal class ValueSecret
{
    public ValueSecret(String value)
    {
        ArgUtil.NotNullOrEmpty(value, nameof(value));
        m_value = value;
    }

    public override Boolean Equals(Object obj)
    {
        var item = obj as ValueSecret;
        if (item == null)
        {
            return false;
        }
        return String.Equals(m_value, item.m_value, StringComparison.Ordinal);
    }

    public override Int32 GetHashCode() => m_value.GetHashCode();

    public IEnumerable<ReplacementPosition> GetPositions(String input)
    {
        if (!String.IsNullOrEmpty(input) && !String.IsNullOrEmpty(m_value))
        {
            Int32 startIndex = 0;
            while (startIndex > -1 &&
                   startIndex < input.Length &&
                   input.Length - startIndex >= m_value.Length) // remaining substring longer than secret value
            {
                startIndex = input.IndexOf(m_value, startIndex, StringComparison.Ordinal);
                if (startIndex > -1)
                {
                    yield return new ReplacementPosition(startIndex, m_value.Length);
                    ++startIndex;
                }
            }
        }
    }

    internal readonly String m_value;
}