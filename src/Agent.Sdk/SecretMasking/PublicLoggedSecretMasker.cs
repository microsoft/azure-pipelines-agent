// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System;
using ValueEncoder = Microsoft.TeamFoundation.DistributedTask.Logging.ValueEncoder;
using ISecretMaskerVSO = Microsoft.TeamFoundation.DistributedTask.Logging.ISecretMasker;
using Microsoft.Security.Utilities;

namespace Agent.Sdk.SecretMasking;

public sealed class PublicLoggedSecretMasker : SecretMasker, ISecretMaskerVSO
{
    private readonly ITraceWriter _trace;

    public PublicLoggedSecretMasker() { }

    public PublicLoggedSecretMasker(PublicLoggedSecretMasker copy) : base(copy)
    {
        this._trace = copy._trace;
    }

    private void Trace(string msg)
    {
        this._trace?.Info(msg);
    }

    public int MinSecretLength
    {
        get
        {
            return this.MinimumSecretLength;
        }

        set
        {
            this.MinimumSecretLength = value;
        }
    }

    public void AddRegex(string pattern)
    {
        this.AddRegex(new RegexPattern(string.Empty, string.Empty, 0, pattern));
    }

    public void AddValueEncoder(ValueEncoder encoder)
    {
        throw new NotImplementedException();
    }

    public void RemoveShortSecretsFromDictionary()
    {
        this.RemovePatternsThatDoNotMeetLengthLimits();
    }

    ISecretMaskerVSO ISecretMaskerVSO.Clone()
    {
        return new PublicLoggedSecretMasker(this);
    }
}