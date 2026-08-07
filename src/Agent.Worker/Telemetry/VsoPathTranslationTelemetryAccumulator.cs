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
        private bool? _validationEnabled;
        private readonly HashSet<string> _stepTargetTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _taskNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<(string Before, string After)> _pathSamples = new HashSet<(string, string)>();

        public bool HasData
        {
            get { lock (_lock) { return _totalCalls > 0; } }
        }

        public void Record(
            string pathBefore,
            string pathAfter,
            string stepTargetType,
            string taskName,
            bool validationEnabled)
        {
            bool translated = !string.Equals(pathBefore, pathAfter, StringComparison.OrdinalIgnoreCase);

            lock (_lock)
            {
                _totalCalls++;
                if (translated) _translatedCount++;
                _validationEnabled = validationEnabled;

                if (!string.IsNullOrEmpty(stepTargetType))
                    _stepTargetTypes.Add(stepTargetType);

                if (!string.IsNullOrEmpty(taskName))
                    _taskNames.Add(taskName);

                if (_pathSamples.Count < MaxPathSamples)
                    _pathSamples.Add((pathBefore ?? string.Empty, pathAfter ?? string.Empty));
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
                    { "ValidationEnabled", _validationEnabled ?? false },
                    { "StepTargetTypes",   string.Join(",", _stepTargetTypes) },
                    { "TaskNames",         string.Join(",", _taskNames) },
                    { "DefinitionId",      definitionId ?? string.Empty },
                    { "BuildId",           buildId ?? string.Empty },
                    // List serialized once by PublishTelemetry — no double-escaping.
                    { "PathSamples",       _pathSamples.Select(p => new { Before = p.Before, After = p.After }).ToList() }
                };
            }
        }
    }
}
