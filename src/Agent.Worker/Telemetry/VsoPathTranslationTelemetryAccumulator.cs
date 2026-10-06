// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.VisualStudio.Services.Agent.Worker.Telemetry
{
    /// <summary>
    /// Thread-safe accumulator for VsoPathTranslation telemetry.
    /// Collects stats across all <c>TranslateToHostPath</c> calls in a job
    /// and exposes them as a flat dictionary for a single CI event at job completion.
    /// </summary>
    internal sealed class VsoPathTranslationTelemetryAccumulator
    {
        private const int MaxPathSamples = 20;

        private readonly object _lock = new object();
        private int _totalCalls;
        private int _translatedCount;
        private int _blockedCount;
        private bool? _validationEnabled;
        private readonly HashSet<string> _stepTargetTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<(string Before, string After, VsoPathTranslationSource Source)> _pathSamples =
            new HashSet<(string, string, VsoPathTranslationSource)>();

        public bool HasData
        {
            get { lock (_lock) { return _totalCalls > 0; } }
        }

        public int TotalCalls
        {
            get { lock (_lock) { return _totalCalls; } }
        }

        public int TranslatedCount
        {
            get { lock (_lock) { return _translatedCount; } }
        }

        /// <summary>
        /// Count of TranslateToHostPath calls where ValidateContainerPath actually threw
        /// (i.e. was truly blocked), as opposed to inferring block outcome from path patterns.
        /// </summary>
        public int BlockedCount
        {
            get { lock (_lock) { return _blockedCount; } }
        }

        public bool ValidationEnabled
        {
            get { lock (_lock) { return _validationEnabled ?? false; } }
        }

        public void Record(
            string pathBefore,
            string pathAfter,
            string stepTargetType,
            bool validationEnabled,
            VsoPathTranslationSource source)
        {
            bool translated = !string.Equals(pathBefore, pathAfter, StringComparison.OrdinalIgnoreCase);

            lock (_lock)
            {
                _totalCalls++;
                if (translated) _translatedCount++;
                _validationEnabled = validationEnabled;

                if (!string.IsNullOrEmpty(stepTargetType))
                    _stepTargetTypes.Add(stepTargetType);

                if (_pathSamples.Count < MaxPathSamples)
                    _pathSamples.Add((pathBefore ?? string.Empty, pathAfter ?? string.Empty, source));
            }
        }

        /// <summary>
        /// Records whether a given TranslateToHostPath call was actually blocked by
        /// ValidateContainerPath (i.e. it threw), as reported by the caller immediately
        /// after the validation attempt. This closes the gap where Record() above only
        /// captures pre-validation path samples and FF state, not the real outcome.
        /// </summary>
        public void RecordValidationOutcome(bool blocked)
        {
            if (!blocked) return;

            lock (_lock)
            {
                _blockedCount++;
            }
        }

        public Dictionary<string, object> ToTelemetryProperties(string definitionId, string buildId)
        {
            lock (_lock)
            {
                return new Dictionary<string, object>
                {
                    { "TotalCalls",        _totalCalls },
                    { "TranslatedCount",   _translatedCount },
                    { "BlockedCount",      _blockedCount },
                    { "ValidationEnabled", _validationEnabled ?? false },
                    { "StepTargetTypes",   string.Join(",", _stepTargetTypes) },
                    { "DefinitionId",      definitionId ?? string.Empty },
                    { "BuildId",           buildId ?? string.Empty },
                    // List serialized once by PublishTelemetry — no double-escaping.
                    { "PathSamples",       _pathSamples.Select(p => new
                        {
                            Before = p.Before,
                            After = p.After,
                            TranslationSource = p.Source.ToString()
                        }).ToList() }
                };
            }
        }
    }
}
