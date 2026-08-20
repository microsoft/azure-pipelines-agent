// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.Services.Agent.Util;
using System;
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

        public void Commit(TaskEnvironmentState state)
        {
            ArgUtil.NotNull(state, nameof(state));

            if (!CaptureCompleted)
            {
                return;
            }

            state.SetRange(_captured.Where(pair => ShouldPersist(pair.Key)));
        }

        private static bool ShouldPersist(string name)
        {
            return !string.Equals(name, Constants.TFBuild, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(name, Constants.Variables.Agent.JobStatus, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(
                    name,
                    VarUtil.ConvertToEnvVariableFormat(Constants.Variables.Agent.JobStatus, preserveCase: false),
                    StringComparison.OrdinalIgnoreCase)
                && !string.Equals(name, Constants.CommandCorrelationIdEnvVar, StringComparison.OrdinalIgnoreCase)
                && !name.StartsWith(SecureArgumentPrefix, StringComparison.OrdinalIgnoreCase);
        }
    }
}
