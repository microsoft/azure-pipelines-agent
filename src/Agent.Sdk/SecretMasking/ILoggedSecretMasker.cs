// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace Agent.Sdk.SecretMasking
{
    /// <summary>
    /// An action that publishes the given data corresonding to the given
    /// feature to a telemetry channel.
    /// </summary>
    public delegate void PublishSecretMaskerTelemetryAction(string feature, Dictionary<string, string> data);

    /// <summary>
    /// Extended ISecretMasker interface that adds support for telemetry and
    /// logging the origin of regexes, encoders and literal secret values.
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

        void StartTelemetry(int maxDetections);
        void StopAndPublishTelemetry(int maxDetectionsPerEvent, PublishSecretMaskerTelemetryAction publishAction);
    }
}
