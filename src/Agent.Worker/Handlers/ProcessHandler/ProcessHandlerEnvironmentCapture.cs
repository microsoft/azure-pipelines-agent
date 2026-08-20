// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.VisualStudio.Services.Agent.Worker.Handlers
{
    internal sealed class ProcessHandlerEnvironmentCapture
    {
        public const string LegacyStartMarker = "##ENV_DELIMITER_d8c0672b##";
        internal const string StartMarkerPrefix = "##ENV_CAPTURE_START_";
        internal const string CompletionMarkerPrefix = "##ENV_CAPTURE_COMPLETE_";

        private const string SecureArgumentPrefix = "AGENT_PH_ARGS_";
        private readonly Dictionary<string, string> _captured =
            new Dictionary<string, string>(VarUtil.EnvironmentVariableKeyComparer);
        private bool _captureStarted;

        public bool CaptureCompleted { get; private set; }
        public string CompletionMarker { get; private set; }
        public string StartMarker { get; private set; }

        public void Reset()
        {
            string markerId = Guid.NewGuid().ToString("N");
            StartMarker = $"{StartMarkerPrefix}{markerId}##";
            CompletionMarker = $"{CompletionMarkerPrefix}{markerId}##";
            _captured.Clear();
            _captureStarted = false;
            CaptureCompleted = false;
        }

        public bool TryProcessLine(string line)
        {
            if (CaptureCompleted)
            {
                return false;
            }

            if (!_captureStarted)
            {
                if (string.Equals(line, StartMarker, StringComparison.Ordinal))
                {
                    _captureStarted = true;
                    return true;
                }

                return false;
            }

            if (string.Equals(line, CompletionMarker, StringComparison.Ordinal))
            {
                CaptureCompleted = true;
                return true;
            }

            int separator = line.IndexOf('=');
            if (separator > 0)
            {
                _captured[line.Substring(0, separator)] = line.Substring(separator + 1);
            }

            return true;
        }

        public void Commit(
            TaskEnvironmentState state,
            IReadOnlyDictionary<string, string> initialEnvironment)
        {
            ArgUtil.NotNull(state, nameof(state));
            ArgUtil.NotNull(initialEnvironment, nameof(initialEnvironment));

            if (!CaptureCompleted)
            {
                return;
            }

            IEnumerable<string> excludedNames = initialEnvironment.Keys
                .Concat(_captured.Keys)
                .Where(ShouldExclude);
            state.ApplyChanges(initialEnvironment, _captured, excludedNames);
        }

        internal static Dictionary<string, string> CreateInitialEnvironment(
            IDictionary<string, string> environment)
        {
            var result = new Dictionary<string, string>(VarUtil.EnvironmentVariableKeyComparer);
            foreach (DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
            {
                string name = entry.Key as string;
                if (string.IsNullOrEmpty(name) || name.StartsWith("=", StringComparison.Ordinal))
                {
                    continue;
                }

                result[name] = entry.Value as string ?? string.Empty;
            }

            if (environment is IEnvironmentVariableRemovals removals)
            {
                foreach (string name in removals.RemovedEnvironmentVariables)
                {
                    result.Remove(name);
                }
            }

            if (environment != null)
            {
                foreach (KeyValuePair<string, string> pair in environment)
                {
                    if (!pair.Key.StartsWith("=", StringComparison.Ordinal))
                    {
                        result[pair.Key] = pair.Value;
                    }
                }
            }

            result[Constants.TFBuild] = "True";
            return result;
        }

        private static bool ShouldExclude(string name)
        {
            // Process-owned and per-attempt values must not become job state.
            return string.Equals(name, Constants.TFBuild, StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, Constants.Variables.Agent.JobStatus, StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    name,
                    VarUtil.ConvertToEnvVariableFormat(Constants.Variables.Agent.JobStatus, preserveCase: false),
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, Constants.CommandCorrelationIdEnvVar, StringComparison.OrdinalIgnoreCase)
                || name.StartsWith(SecureArgumentPrefix, StringComparison.OrdinalIgnoreCase);
        }
    }
}
