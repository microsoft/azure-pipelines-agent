﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System;
using System.Collections.Generic;
using ValueEncoder = Microsoft.TeamFoundation.DistributedTask.Logging.ValueEncoder;
using ISecretMaskerVSO = Microsoft.TeamFoundation.DistributedTask.Logging.ISecretMasker;

using Microsoft.Security.Utilities;

namespace Agent.Sdk.SecretMasking;

public sealed class OssSecretMasker : ISecretMaskerVSO, IDisposable
{
    private SecretMasker _secretMasker;

    public OssSecretMasker() : this(0)
    {
    }

    public OssSecretMasker(int minSecretLength) : base()
    {
        _secretMasker = new SecretMasker(regexSecrets: WellKnownRegexPatterns.PreciselyClassifiedSecurityKeys,
                                         generateCorrelatingIds: true);

        _secretMasker.MinimumSecretLength = minSecretLength;
        _secretMasker.DefaultRegexRedactionToken = "***";
    }

    private OssSecretMasker(OssSecretMasker copy)
    {
        _secretMasker = copy._secretMasker.Clone();
    }

    /// <summary>
    /// This property allows to set the minimum length of a secret for masking
    /// </summary>
    public int MinSecretLength
    {
        get { return _secretMasker.MinimumSecretLength; }
        set { _secretMasker.MinimumSecretLength = value; }
    }

    /// <summary>
    /// This implementation assumes no more than one thread is adding regexes, values, or encoders at any given time.
    /// </summary>
    public void AddRegex(string pattern)
    {
        _secretMasker.AddRegex(new RegexPattern(id: string.Empty, name: string.Empty, DetectionMetadata.None, pattern));
    }

    /// <summary>
    /// This implementation assumes no more than one thread is adding regexes, values, or encoders at any given time.
    /// </summary>
    public void AddValue(string test)
    {
        _secretMasker.AddValue(test);
    }

    /// <summary>
    /// This implementation assumes no more than one thread is adding regexes, values, or encoders at any given time.
    /// </summary>
    public void AddValueEncoder(ValueEncoder encoder)
    {
       _secretMasker.AddLiteralEncoder(x => encoder(x));
    }

    public OssSecretMasker Clone() => new OssSecretMasker(this);

    public void Dispose()
    {
        _secretMasker?.Dispose();
        _secretMasker = null;
    }

    public String MaskSecrets(string input)
    {
        return _secretMasker.MaskSecrets(input);
    }

    /// <summary>
    /// Removes secrets from the dictionary shorter than the MinSecretLength property.
    /// This implementation assumes no more than one thread is adding regexes, values, or encoders at any given time.
    /// </summary>
    public void RemoveShortSecretsFromDictionary()
    {
        var filteredValueSecrets = new HashSet<SecretLiteral>();
        var filteredRegexSecrets = new HashSet<RegexPattern>();

        try
        {
            _secretMasker.SyncObject.EnterReadLock();

            foreach (var secret in _secretMasker.EncodedSecretLiterals)
            {
                if (secret.Value.Length < MinSecretLength)
                {
                    filteredValueSecrets.Add(secret);
                }
            }

            foreach (var secret in _secretMasker.RegexPatterns)
            {
                if (secret.Pattern.Length < MinSecretLength)
                {
                    filteredRegexSecrets.Add(secret);
                }
            }
        }
        finally
        {
            if (_secretMasker.SyncObject.IsReadLockHeld)
            {
                _secretMasker.SyncObject.ExitReadLock();
            }
        }

        try
        {
            _secretMasker.SyncObject.EnterWriteLock();

            foreach (var secret in filteredValueSecrets)
            {
                _secretMasker.EncodedSecretLiterals.Remove(secret);
            }

            foreach (var secret in filteredRegexSecrets)
            {
                _secretMasker.RegexPatterns.Remove(secret);
            }

            foreach (var secret in filteredValueSecrets)
            {
                _secretMasker.ExplicitlyAddedSecretLiterals.Remove(secret);
            }
        }
        finally
        {
            if (_secretMasker.SyncObject.IsWriteLockHeld)
            {
                _secretMasker.SyncObject.ExitWriteLock();
            }
        }
    }

    ISecretMaskerVSO ISecretMaskerVSO.Clone() => this.Clone();
}