// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

using Microsoft.TeamFoundation.DistributedTask.Logging;

namespace Agent.Sdk.SecretMasking
{
    /// <summary>
    /// Extended ISecretMasker interface that adds support for logging the origin of
    /// regexes, encoders and literal secret values.
    /// </summary>
    public interface ILoggedSecretMasker : ISecretMasker, IDisposable
    {
        static int MinSecretLengthLimit { get; }

        void AddRegex(String pattern, string origin);
        void AddValue(String value, string origin);
        void AddValueEncoder(ValueEncoder encoder, string origin);
        void SetTrace(ITraceWriter trace);
    }
}
