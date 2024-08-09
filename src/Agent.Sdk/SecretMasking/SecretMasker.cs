// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using ValueEncoder = Microsoft.TeamFoundation.DistributedTask.Logging.ValueEncoder;
using ISecretMaskerVSO = Microsoft.TeamFoundation.DistributedTask.Logging.ISecretMasker;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace Agent.Sdk.SecretMasking;

public class SecretMasker : ISecretMasker, IDisposable
{
    public SecretMasker()
    {
        MinSecretLength = 0;
        m_originalValueSecrets = new HashSet<ValueSecret>();
        m_regexSecrets = new HashSet<RegexSecret>();
        m_valueEncoders = new HashSet<ValueEncoder>();
        m_valueSecrets = new HashSet<ValueSecret>();
        ReplacementTokens = new HashSet<string>();
    }

    public SecretMasker(int minSecretLength)
    {
        MinSecretLength = minSecretLength;
        m_originalValueSecrets = new HashSet<ValueSecret>();
        m_regexSecrets = new HashSet<RegexSecret>();
        m_valueEncoders = new HashSet<ValueEncoder>();
        m_valueSecrets = new HashSet<ValueSecret>();
        ReplacementTokens = new HashSet<string>();
    }

    private SecretMasker(SecretMasker copy)
    {
        // Read section.
        try
        {
            copy.m_lock.EnterReadLock();

            // Copy the hash sets.
            MinSecretLength = copy.MinSecretLength;
            m_originalValueSecrets = new HashSet<ValueSecret>(copy.m_originalValueSecrets);
            m_regexSecrets = new HashSet<RegexSecret>(copy.m_regexSecrets);
            m_valueEncoders = new HashSet<ValueEncoder>(copy.m_valueEncoders);
            m_valueSecrets = new HashSet<ValueSecret>(copy.m_valueSecrets);
            ReplacementTokens = copy.ReplacementTokens;
        }
        finally
        {
            if (copy.m_lock.IsReadLockHeld)
            {
                copy.m_lock.ExitReadLock();
            }
        }
    }

    /// <summary>
    /// Total time in ticks spent masking content for the lifetime of this masker instance.
    /// </summary>
    public long ElapsedMaskingTime { get; private set; }

    public ISet<string> ReplacementTokens { get; private set; }

    /// <summary>
    /// This property allows to set the minimum length of a secret for masking
    /// </summary>
    virtual public int MinSecretLength { get; set; }

    public void AddRegex(String pattern)
    {
        AddRegex(pattern, moniker: null, sniffLiterals: null, RegexOptions.Compiled | RegexOptions.ExplicitCapture);
    }

    /// <summary>
    /// This implementation assumes no more than one thread is adding regexes, values, or encoders at any given time.
    /// </summary>
    public void AddRegex(String pattern, string moniker, ISet<string> sniffLiterals, RegexOptions regexOptions)
    {
        // Test for empty.
        if (String.IsNullOrEmpty(pattern))
        {
            return;
        }

        if (pattern.Length < MinSecretLength)
        {
            return;
        }

        // Write section.
        try
        {
            m_lock.EnterWriteLock();

            // Add the value.
            m_regexSecrets.Add(new RegexSecret(pattern, moniker, sniffLiterals, regexOptions));
        }
        finally
        {
            if (m_lock.IsWriteLockHeld)
            {
                m_lock.ExitWriteLock();
            }
        }
    }


    /// <summary>
    /// This implementation assumes no more than one thread is adding regexes, values, or encoders at any given time.
    /// </summary>
    public void AddValue(String value)
    {
        // Test for empty.
        if (String.IsNullOrEmpty(value))
        {
            return;
        }

        if (value.Length < MinSecretLength)
        {
            return;
        }

        var valueSecrets = new List<ValueSecret>(new[] { new ValueSecret(value) });

        // Read section.
        ValueEncoder[] valueEncoders;
        try
        {
            m_lock.EnterReadLock();

            // Test whether already added.
            if (m_originalValueSecrets.Contains(valueSecrets[0]))
            {
                return;
            }

            // Read the value encoders.
            valueEncoders = m_valueEncoders.ToArray();
        }
        finally
        {
            if (m_lock.IsReadLockHeld)
            {
                m_lock.ExitReadLock();
            }
        }

        // Compute the encoded values.
        foreach (ValueEncoder valueEncoder in valueEncoders)
        {
            String encodedValue = valueEncoder(value);
            if (!String.IsNullOrEmpty(encodedValue) && encodedValue.Length >= MinSecretLength)
            {
                valueSecrets.Add(new ValueSecret(encodedValue));
            }
        }

        // Write section.
        try
        {
            m_lock.EnterWriteLock();

            // Add the values.
            m_originalValueSecrets.Add(valueSecrets[0]);
            foreach (ValueSecret valueSecret in valueSecrets)
            {
                m_valueSecrets.Add(valueSecret);
            }
        }
        finally
        {
            if (m_lock.IsWriteLockHeld)
            {
                m_lock.ExitWriteLock();
            }
        }
    }

    /// <summary>
    /// This implementation assumes no more than one thread is adding regexes, values, or encoders at any given time.
    /// </summary>
    public void AddValueEncoder(ValueEncoder encoder)
    {
        ValueSecret[] originalSecrets;

        // Read section.
        try
        {
            m_lock.EnterReadLock();

            // Test whether already added.
            if (m_valueEncoders.Contains(encoder))
            {
                return;
            }

            // Read the original value secrets.
            originalSecrets = m_originalValueSecrets.ToArray();
        }
        finally
        {
            if (m_lock.IsReadLockHeld)
            {
                m_lock.ExitReadLock();
            }
        }

        // Compute the encoded values.
        var encodedSecrets = new List<ValueSecret>();
        foreach (ValueSecret originalSecret in originalSecrets)
        {
            String encodedValue = encoder(originalSecret.m_value);
            if (!String.IsNullOrEmpty(encodedValue) && encodedValue.Length >= MinSecretLength)
            {
                encodedSecrets.Add(new ValueSecret(encodedValue));
            }
        }

        // Write section.
        try
        {
            m_lock.EnterWriteLock();

            // Add the encoder.
            m_valueEncoders.Add(encoder);

            // Add the values.
            foreach (ValueSecret encodedSecret in encodedSecrets)
            {
                m_valueSecrets.Add(encodedSecret);
            }
        }
        finally
        {
            if (m_lock.IsWriteLockHeld)
            {
                m_lock.ExitWriteLock();
            }
        }
    }


    public ISecretMasker Clone() => new SecretMasker(this);

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            m_lock?.Dispose();
            m_lock = null;
        }
    }

    public String MaskSecrets(String input)
    {
        if (String.IsNullOrEmpty(input))
        {
            return String.Empty;
        }

        var secretPositions = new List<Replacement>();

        // Read section.
        try
        {
            m_lock.EnterReadLock();

            var stopwatch = Stopwatch.StartNew();

            // Get indexes and lengths of all substrings that will be replaced.
            // We also persist the generated replacement tokens for secrets. In
            // some cases, this data will be put in the telemetry stream.
            foreach (RegexSecret regexSecret in m_regexSecrets)
            {
                foreach (Replacement replacement in regexSecret.GetReplacements(input))
                {
                    ReplacementTokens.Add(replacement.Token);
                    secretPositions.Add(replacement);
                }
            }

            foreach (ValueSecret valueSecret in m_valueSecrets)
            {
                secretPositions.AddRange(valueSecret.GetPositions(input));
            }

            ElapsedMaskingTime += stopwatch.ElapsedTicks;
        }
        finally
        {
            if (m_lock.IsReadLockHeld)
            {
                m_lock.ExitReadLock();
            }
        }

        // Short-circuit if nothing to replace.
        if (secretPositions.Count == 0)
        {
            return input;
        }

        // Merge positions into ranges of characters to replace.
        List<Replacement> replacementPositions = new List<Replacement>();
        Replacement currentReplacement = null;
        foreach (Replacement secretPosition in secretPositions.OrderBy(x => x.Start))
        {
            if (currentReplacement == null)
            {
                currentReplacement = new Replacement(copy: secretPosition);
                replacementPositions.Add(currentReplacement);
            }
            else
            {
                if (secretPosition.Start <= currentReplacement.End)
                {
                    // Overlap
                    currentReplacement.Length = Math.Max(currentReplacement.End, secretPosition.End) - currentReplacement.Start;
                }
                else
                {
                    // No overlap
                    currentReplacement = new Replacement(copy: secretPosition);
                    replacementPositions.Add(currentReplacement);
                }
            }
        }

        // Replace
        var stringBuilder = new StringBuilder();
        Int32 startIndex = 0;
        foreach (var replacement in replacementPositions)
        {
            stringBuilder.Append(input.Substring(startIndex, replacement.Start - startIndex));
            stringBuilder.Append(replacement.Token);
            startIndex = replacement.Start + replacement.Length;
        }

        if (startIndex < input.Length)
        {
            stringBuilder.Append(input.Substring(startIndex));
        }

        return stringBuilder.ToString();
    }

    /// <summary>
    /// Removes secrets from the dictionary shorter than the MinSecretLength property.
    /// This implementation assumes no more than one thread is adding regexes, values, or encoders at any given time.
    /// </summary>
    public virtual void RemoveShortSecretsFromDictionary()
    {
        var filteredValueSecrets = new HashSet<ValueSecret>();
        var filteredRegexSecrets = new HashSet<RegexSecret>();

        try
        {
            m_lock.EnterReadLock();

            foreach (var secret in m_valueSecrets)
            {
                if (secret.m_value.Length < MinSecretLength)
                {
                    filteredValueSecrets.Add(secret);
                }
            }

            foreach (var secret in m_regexSecrets)
            {
                if (secret.Pattern.Length < MinSecretLength)
                {
                    filteredRegexSecrets.Add(secret);
                }
            }
        }
        finally
        {
            if (m_lock.IsReadLockHeld)
            {
                m_lock.ExitReadLock();
            }
        }

        try
        {
            m_lock.EnterWriteLock();

            foreach (var secret in filteredValueSecrets)
            {
                m_valueSecrets.Remove(secret);
            }

            foreach (var secret in filteredRegexSecrets)
            {
                m_regexSecrets.Remove(secret);
            }

            foreach (var secret in filteredValueSecrets)
            {
                m_originalValueSecrets.Remove(secret);
            }
        }
        finally
        {
            if (m_lock.IsWriteLockHeld)
            {
                m_lock.ExitWriteLock();
            }
        }
    }

    ISecretMaskerVSO ISecretMaskerVSO.Clone()
    {
        return this.Clone();
    }

    private readonly HashSet<ValueSecret> m_originalValueSecrets;
    private readonly HashSet<ValueEncoder> m_valueEncoders;
    private readonly HashSet<ValueSecret> m_valueSecrets;
    private readonly HashSet<RegexSecret> m_regexSecrets;
    private ReaderWriterLockSlim m_lock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
}