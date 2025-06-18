// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;

using Microsoft.Security.Utilities;

namespace Agent.Sdk.SecretMasking;

public sealed class OssSecretMasker : IRawSecretMasker
{
    private SecretMasker _secretMasker;
    private Telemetry _telemetry;

    public OssSecretMasker(IEnumerable<RegexPattern> patterns = null)
    {
        _secretMasker = new SecretMasker(patterns,
                                         generateCorrelatingIds: true,
                                         defaultRegexRedactionToken: "***");
    }

    /// <summary>
    /// This property allows to set the minimum length of a secret for masking
    /// </summary>
    public int MinSecretLength
    {
        get => _secretMasker.MinimumSecretLength;
        set => _secretMasker.MinimumSecretLength = value;
    }

    public void AddRegex(string pattern)
    {
        // NOTE: This code path is used for regexes sent to the agent via
        // `AgentJobRequestMessage.MaskHints`. The regexes are effectively
        // arbitrary from our perspective at this layer and therefore we cannot
        // use regex options like 'NonBacktracking' that may not be compatible
        // with them. 
        var regexPattern = new RegexPattern(
            id: string.Empty,
            name: string.Empty,
            label: string.Empty,
            pattern: pattern,
            patternMetadata: DetectionMetadata.None,
            regexOptions: RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture);

        _secretMasker.AddRegex(regexPattern);
    }

    public void AddValue(string test)
    {
        _secretMasker.AddValue(test);
    }

    public void AddValueEncoder(Func<string, string> encoder)
    {
        _secretMasker.AddLiteralEncoder(x => encoder(x));
    }

    public void Dispose()
    {
        _secretMasker?.Dispose();
        _secretMasker = null;
        _telemetry = null;
    }

    public string MaskSecrets(string input)
    {
        _secretMasker.SyncObject.EnterReadLock();
        try
        {
            _telemetry?.ProcessInput(input);
            return _secretMasker.MaskSecrets(input, _telemetry?.ProcessDetection);
        }
        finally
        {
            _secretMasker.SyncObject.ExitReadLock();
        }
    }

    public void StartTelemetry(int maxUniqueCorrelatingIds)
    {
        _secretMasker.SyncObject.EnterWriteLock();
        try
        {
            _telemetry ??= new Telemetry(maxUniqueCorrelatingIds);
        }
        finally
        {
            _secretMasker.SyncObject.ExitWriteLock();
        }
    }

    public void StopAndPublishTelemetry(PublishSecretMaskerTelemetryAction publishAction, int maxCorrelatingIdsPerEvent)
    {
        Telemetry telemetry;

        _secretMasker.SyncObject.EnterWriteLock();
        try
        {
            telemetry = _telemetry;
            _telemetry = null;
        }
        finally
        {
            _secretMasker.SyncObject.ExitWriteLock();
        }

        telemetry?.Publish(publishAction, _secretMasker.ElapsedMaskingTime, maxCorrelatingIdsPerEvent);
    }

    private sealed class Telemetry
    {
        // NOTE: Telemetry does not fit into the reader-writer lock model of the
        // SecretMasker API because we *write* telemetry during *read*
        // operations. We therefore use separate interlocked operations and a
        // concurrent dictionary when writing to telemetry.

        // Key=CrossCompanyCorrelatingId (C3ID), Value=Rule Moniker C3ID is a
        // non-reversible seeded hash and only available when detection is made
        // by a high-confidence rule that matches secrets with high entropy.
        private readonly ConcurrentDictionary<string, string> _correlationData;
        private readonly int _maxUniqueCorrelatingIds;
        private long _charsScanned;
        private long _stringsScanned;
        private long _totalDetections;

        public Telemetry(int maxDetections)
        {
            _correlationData = new ConcurrentDictionary<string, string>();
            _maxUniqueCorrelatingIds = maxDetections;
            ProcessDetection = ProcessDetectionImplementation;
        }

        public void ProcessInput(string input)
        {
            Interlocked.Add(ref _charsScanned, input.Length);
            Interlocked.Increment(ref _stringsScanned);
        }

        public Action<Detection> ProcessDetection { get; }

        private void ProcessDetectionImplementation(Detection detection)
        {
            Interlocked.Increment(ref _totalDetections);

            // NOTE: We cannot prevent the concurrent dictionary from exceeding
            // the maximum detection count when multiple threads add detections
            // in parallel. The condition here is therefore a best effort to
            // constrain the memory consumed by excess detections that will not
            // be published. Furthermore, it is deliberate that we use <=
            // instead of < here as it allows us to detect the case where the
            // maximum number of events have been exceeded without adding any
            // additional state.
            if (_correlationData.Count <= _maxUniqueCorrelatingIds &&
                detection.CrossCompanyCorrelatingId != null)
            {
                _correlationData.TryAdd(detection.CrossCompanyCorrelatingId, detection.Moniker);
            }
        }

        public void Publish(PublishSecretMaskerTelemetryAction publishAction, TimeSpan elapsedMaskingTime, int maxCorrelatingIdsPerEvent)
        {
            Dictionary<string, string> correlationData = null;
            int uniqueCorrelatingIds = 0;
            bool correlationDataIsIncomplete = false;

            // Publish 'SecretMaskerCorrelation' events mapping unique C3IDs to
            // rule moniker. No more than 'maxCorrelatingIdsPerEvent' are
            // published in a single event.
            foreach (var pair in _correlationData)
            {
                if (uniqueCorrelatingIds >= _maxUniqueCorrelatingIds)
                {
                    correlationDataIsIncomplete = true;
                    break;
                }

                correlationData ??= new Dictionary<string, string>(maxCorrelatingIdsPerEvent);
                correlationData.Add(pair.Key, pair.Value);
                uniqueCorrelatingIds++;

                if (correlationData.Count >= maxCorrelatingIdsPerEvent)
                {
                    publishAction("SecretMaskerCorrelation", correlationData);
                    correlationData = null;
                }
            }

            if (correlationData != null)
            {
                publishAction("SecretMaskerCorrelation", correlationData);
                correlationData = null;
            }

            // Send overall information in a 'SecretMasker' event.
            var overallData = new Dictionary<string, string> {
                // The version of Microsoft.Security.Utilities.Core used.
                { "Version", SecretMasker.Version.ToString() },

                // The total number number of characters scanned by the secret masker.
                { "CharsScanned", _charsScanned.ToString(CultureInfo.InvariantCulture) },
                
                // The total number of strings scanned by the secret masker.
                { "StringsScanned", _stringsScanned.ToString(CultureInfo.InvariantCulture) },

                // The total number of detections made by the secret masker.
                // This includes duplicate detections and detections without
                // correlating IDs such as those made by literal values.
                { "TotalDetections", _totalDetections.ToString(CultureInfo.InvariantCulture) },

                // The total amount of time spent masking secrets.
                { "ElapsedMaskingTimeInMilliseconds", elapsedMaskingTime.TotalMilliseconds.ToString(CultureInfo.InvariantCulture) },

                // Whether the 'maxUniqueCorrelatingIds' limit was exceeded and
                // therefore the 'SecretMaskerDetectionCorrelation' events does
                // not contain all unique correlating IDs detected.
                { "CorrelationDataIsIncomplete", correlationDataIsIncomplete.ToString(CultureInfo.InvariantCulture) },

                // The total number of unique correlating IDs reported in
                // 'SecretMaskerCorrelation' events.
                //
                // NOTE: This may be less than the total number of unique
                // correlating IDs if the maximum was exceeded. See above.
                { "UniqueCorrelatingIds", uniqueCorrelatingIds.ToString(CultureInfo.InvariantCulture) },
            };

            publishAction("SecretMasker", overallData);
        }
    }

    // This is a no-op for the OSS SecretMasker because it respects
    // MinimumSecretLength immediately without requiring an extra API call.
    void IRawSecretMasker.RemoveShortSecretsFromDictionary() { }
}