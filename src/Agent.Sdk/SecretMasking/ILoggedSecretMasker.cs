// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace Agent.Sdk.SecretMasking
{
    /// <summary>
    /// Extended ISecretMasker interface that adds support for logging the origin of
    /// regexes, encoders and literal secret values.
    /// </summary>
    public interface ILoggedSecretMasker : IDisposable
    {
        int MinSecretLength { get; set; }

        void AddRegex(string pattern, string origin);
        void AddValue(string value, string origin);
        void AddValueEncoder(Func<string, string> encoder, string origin);
        string MaskSecrets(string input);
        void RemoveShortSecretsFromDictionary();
        void SetTrace(ITraceWriter trace);
    }
}
