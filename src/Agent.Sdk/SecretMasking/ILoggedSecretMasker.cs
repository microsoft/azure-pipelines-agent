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

        /// <summary>
        /// Begin collecting data for secret masking telemetry.
        /// </summary>
        /// <remarks>
        /// This is a no-op if <see cref="LegacySecretMasker"/> is being used,
        /// only <see cref="OssSecretMasker"/> supports telemetry. Also, the
        /// agent will only call this if a feature flag that opts in to secret
        /// masking telemetry is enabled..
        /// </remarks>
        /// <param name="maxUniqueCorrelatingIds">
        /// The maximum number of unique correlating IDs to collect.
        /// </param>
        void StartTelemetry(int maxUniqueCorrelatingIds);

        /// <summary>
        /// Stop collecting data for secret masking telemetry and publish the
        /// telemetry events.
        /// </summary>
        /// <remarks>
        /// This is a no-op if <see cref="LegacySecretMasker"/> is being used,
        /// only <see cref="OssSecretMasker"/> supports telemetry.
        /// <param name="maxCorrelatingIdsPerEvent">
        /// The maximum number of correlating IDs to report in a single
        /// telemetry event.
        /// <param name="publishAction">
        /// Callback to publish the telemetry data.
        /// </param>
        void StopAndPublishTelemetry(int maxCorrelatingIdsPerEvent, PublishSecretMaskerTelemetryAction publishAction);
    }
}
