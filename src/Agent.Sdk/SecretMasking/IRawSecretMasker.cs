// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System;

namespace Agent.Sdk.SecretMasking
{
    /// <summary>
    /// Rerpresents a raw secret masker without the features that <see
    /// cref="ILoggedSecretMasker"/> adds.
    /// </summary>
    public interface IRawSecretMasker : IDisposable
    {
        int MinSecretLength { get; set; }

        void AddRegex(string pattern);
        void AddValue(string value);
        void AddValueEncoder(Func<string, string> encoder);
        string MaskSecrets(string input);
        void RemoveShortSecretsFromDictionary();
    }
}
