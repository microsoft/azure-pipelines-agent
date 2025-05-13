// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System;
using Microsoft.TeamFoundation.DistributedTask.Logging;

namespace Agent.Sdk.SecretMasking
{
    /// <summary>
    /// Rerpresents a raw secret masker without the logging or telemetry
    /// features that <see cref="ILoggedSecretMasker"/> adds.
    /// </summary>
    /// <remarks>
    /// This interface is quivalent to <see cref="ISecretMasker"/> without <see
    /// cref="ISecretMasker.Clone"/>, which would be problematic to implement
    /// in the agent and would defeat telemetry.
    /// </remarks>
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
