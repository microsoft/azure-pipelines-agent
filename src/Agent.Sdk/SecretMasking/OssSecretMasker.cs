// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using Microsoft.Security.Utilities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using ISecretMasker = Microsoft.TeamFoundation.DistributedTask.Logging.ISecretMasker;
using ValueEncoder = Microsoft.TeamFoundation.DistributedTask.Logging.ValueEncoder;

namespace Agent.Sdk.SecretMasking;

public sealed class OssSecretMasker : ISecretMasker, IDisposable
{
    private SecretMasker _secretMasker;
    private Telemetry _telemetry;

    /// <summary>
    /// The maximum number properties to report in a single
    /// 'SecretMaskerDetections' telemetry event. This value was chosen to keep
    /// the size in the range of existing routine telemetry events.
    /// </summary>
    public const int MaxDetectionsPerTelemetryEvent = 20;

    /// <summary>
    /// The maximum number of 'SecreMaskerDetections' telemetry events to send.
    /// If this would be have to be exceeded to record all unique C3Id, the
    /// overall 'SecretMasker' event will indicate this by including a
    /// 'DetectionDataIsIncomplete' property set to 'true'.
    /// </summary>
    public const int MaxTelemetryDetectionEvents = 5;

    /// <summary>
    /// The maximum number of key=C3ID, value=Moniker properties that can be
    /// sent across all events.
    /// </summary>
    public const int MaxTelemetryDetections = MaxDetectionsPerTelemetryEvent * MaxTelemetryDetectionEvents;

    public OssSecretMasker() : this(Array.Empty<RegexPattern>())
    {
    }

    public OssSecretMasker(IEnumerable<RegexPattern> patterns)
    {
        _secretMasker = new SecretMasker(patterns, generateCorrelatingIds: true);
        _secretMasker.DefaultRegexRedactionToken = "***";
    }

    private OssSecretMasker(OssSecretMasker copy)
    {
        _secretMasker = copy._secretMasker.Clone();
        _telemetry = copy._telemetry?.Clone();
    }

    /// <summary>
    /// This property allows to set the minimum length of a secret for masking
    /// </summary>
    public int MinSecretLength
    {
        get => _secretMasker.MinimumSecretLength;
        set => _secretMasker.MinimumSecretLength = value;
    }

    /// <summary>
    /// This implementation assumes no more than one thread is adding regexes, values, or encoders at any given time.
    /// </summary>
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
        _telemetry = null;
    }

    public string MaskSecrets(string input)
    {
        _telemetry?.ProcessInput(input);
        return _secretMasker.MaskSecrets(input, _telemetry?.DetectionAction);
    }

    public void EnableTelemetry()
    {
        _telemetry ??= new Telemetry();
    }

    /// <summary>
    /// If enabled via <see cref="TelemetryEnabled"/> opt-in, publishes
    /// telemetry via the provided callback that handles sending it over the
    /// wire.
    ///
    /// Always publishes a 'SecretMasker' event with overall stats, and may
    /// publish 'SecretMaskerDetections' events mapping C3ID (12 byte
    /// non-reversible seeded hash) to pattern moniker ('name'.'id') for
    /// rule-based patterns with high entropy if any such detections are found.
    /// </summary>
    public void PublishTelemetry(PublishSecretMaskerTelemetryAction publishAction)
    {
        if (_telemetry == null)
        {
            return;
        }

        Dictionary<string, string> detectionData = null;
        int events = 0;

        foreach (var pair in _telemetry.Detections)
        {
            detectionData ??= new Dictionary<string, string>(MaxDetectionsPerTelemetryEvent);
            detectionData.Add(pair.Key, pair.Value);

            if (detectionData.Count >= MaxDetectionsPerTelemetryEvent)
            {
                publishAction("SecretMaskerDetections", detectionData);
                events++;
                detectionData = null;

                if (events >= MaxTelemetryDetectionEvents)
                {
                    break;
                }
            }
        }

        if (events < MaxTelemetryDetectionEvents && detectionData != null)
        {
            publishAction("SecretMaskerDetections", detectionData);
            detectionData = null;
        }

        var overallData = new Dictionary<string, string>(GetOverallTelemetry());
        if (_telemetry.Detections.Count > MaxTelemetryDetections)
        {
            overallData.Add("DetectionDataIsIncomplete", "true");
        }

        publishAction("SecretMasker", overallData);
    }

    private KeyValuePair<string, string>[] GetOverallTelemetry()
    {
        double elapsedMaskingTimeInMilliseconds = (double)_secretMasker.ElapsedMaskingTime / TimeSpan.TicksPerMillisecond;
        return new KeyValuePair<string, string>[] {
            new("Version", SecretMasker.Version.ToString()),
            new("CharsScanned", _telemetry.CharsScanned.ToString(CultureInfo.InvariantCulture)),
            new("StringsScanned", _telemetry.StringsScanned.ToString(CultureInfo.InvariantCulture)),
            new("TotalDetections", _telemetry.TotalDetections.ToString(CultureInfo.InvariantCulture)),
            new("UniqueCorrelatingIds", _telemetry.Detections.Count.ToString(CultureInfo.InvariantCulture)),
            new("ElapsedMaskingTimeInMilliseconds", elapsedMaskingTimeInMilliseconds.ToString(CultureInfo.InvariantCulture)),
        };
    }

    private sealed class Telemetry
    {
        private readonly ConcurrentDictionary<string, string> _detections;
        private long _charsScanned;
        private long _stringsScanned;
        private long _totalDetections;

        public IReadOnlyDictionary<string, string> Detections => _detections;
        public long CharsScanned => _charsScanned;
        public long StringsScanned => _stringsScanned;
        public long TotalDetections => _totalDetections;
        public readonly Action<Detection> DetectionAction;

        public Telemetry()
        {
            _detections = new ConcurrentDictionary<string, string>();
            DetectionAction = ProcessDetection;
        }

        private Telemetry(Telemetry copy)
        {
            _detections = new ConcurrentDictionary<string, string>(copy.Detections);
            DetectionAction = ProcessDetection;

            _charsScanned = copy.CharsScanned;
            _stringsScanned = copy.StringsScanned;
            _totalDetections = copy.TotalDetections;
        }

        public Telemetry Clone() => new(this);

        public void ProcessInput(string input)
        {
            Interlocked.Add(ref _charsScanned, input.Length);
            Interlocked.Increment(ref _stringsScanned);
        }

        private void ProcessDetection(Detection detection)
        {
            Interlocked.Increment(ref _totalDetections);

            if (detection.CrossCompanyCorrelatingId != null)
            {
                _detections.TryAdd(detection.CrossCompanyCorrelatingId, detection.Moniker);
            }
        }
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

    ISecretMasker ISecretMasker.Clone() => this.Clone();
}