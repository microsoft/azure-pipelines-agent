// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Agent.Sdk.SecretMasking;

namespace Microsoft.VisualStudio.Services.Agent
{
    public sealed class EnhancedTracing : Tracing
    {
        private readonly ICorrelationContextManager _correlationContextManager;

        public EnhancedTracing(string name, ILoggedSecretMasker secretMasker, ICorrelationContextManager correlationContextManager, SourceSwitch sourceSwitch, HostTraceListener traceListener)
            : base(name, secretMasker, sourceSwitch, traceListener)
        {
            _correlationContextManager = correlationContextManager ?? throw new ArgumentNullException(nameof(correlationContextManager));
        }

        // Override ALL base methods to ensure enhanced logging for any call signature
        public override void Info(string message, [CallerMemberName] string operation = "")
        {
            LogWithOperation(TraceEventType.Information, message, operation);
        }

        public override void Info(object item, [CallerMemberName] string operation = "")
        {
            LogWithOperation(TraceEventType.Information, item?.ToString() ?? "null", operation);
        }

        // Override ALL Error methods to ensure enhanced logging
        public override void Error(Exception exception, [CallerMemberName] string operation = "")
        {
            ArgUtil.NotNull(exception, nameof(exception));
            LogWithOperation(TraceEventType.Error, exception.ToString(), operation);
        }

        public override void Error(string message, [CallerMemberName] string operation = "")
        {
            LogWithOperation(TraceEventType.Error, message, operation);
        }

        // Override ALL Warning methods to ensure enhanced logging
        public override void Warning(string message, [CallerMemberName] string operation = "")
        {
            LogWithOperation(TraceEventType.Warning, message, operation);
        }

        // Override ALL Verbose methods to ensure enhanced logging
        public override void Verbose(string message, [CallerMemberName] string operation = "")
        {
            LogWithOperation(TraceEventType.Verbose, message, operation);
        }

        public override void Verbose(object item, [CallerMemberName] string operation = "")
        {
            LogWithOperation(TraceEventType.Verbose, item?.ToString() ?? "null", operation);
        }

        public override void Entering([CallerMemberName] string name = "")
        {
            LogWithOperation(TraceEventType.Verbose, $"Entering {name}", name);
        }

        public override IDisposable EnteringWithDuration([CallerMemberName] string name = "")
        {
            LogWithOperation(TraceEventType.Verbose, $"Entering {name}", name);
            return new MethodTimer(this, name);
        }

        public override void Leaving([CallerMemberName] string name = "")
        {
            LogWithOperation(TraceEventType.Verbose, $"Leaving {name}", name);
        }

        internal void LogLeavingWithDuration(string methodName, TimeSpan duration)
        {
            var formattedDuration = FormatDuration(duration);
            var message = $"Leaving {methodName} (Duration: {formattedDuration})";
            LogWithOperation(TraceEventType.Verbose, message, methodName);
        }

        private void LogWithOperation(TraceEventType eventType, string message, string operation)
        {
            var enhancedMessage = FormatEnhancedLogMessage(message, operation);
            base.Trace(eventType, enhancedMessage);
        }

        private string FormatEnhancedLogMessage(string message, string operation)
        {
            var correlation = GetCorrelationId();
            var correlationPart = !string.IsNullOrEmpty(correlation) ? $"[{correlation}]" : "";
            var operationPart = !string.IsNullOrEmpty(operation) ? $"[{operation}]" : "";
            return $"{operationPart} {message} {correlationPart}".TrimEnd();
        }

        private string GetCorrelationId()
        {
            return _correlationContextManager.BuildCorrelationId();
        }

        private string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalHours >= 1)
                return $"{(int)duration.TotalHours}h {duration.Minutes}m {duration.Seconds}.{duration.Milliseconds:D3}s";
            if (duration.TotalMinutes >= 1)
                return $"{duration.Minutes}m {duration.Seconds}.{duration.Milliseconds:D3}s";
            if (duration.TotalSeconds >= 1)
                return $"{duration.TotalSeconds:F3}s";
            return $"{duration.TotalMilliseconds:F2}ms";
        }

        private sealed class MethodTimer : IDisposable
        {
            private readonly EnhancedTracing _tracing;
            private readonly string _methodName;
            private readonly Stopwatch _stopwatch;
            private bool _disposed = false;

            public MethodTimer(EnhancedTracing tracing, string methodName)
            {
                _tracing = tracing;
                _methodName = methodName;
                _stopwatch = Stopwatch.StartNew();
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    _disposed = true;
                    _stopwatch.Stop();
                    _tracing.LogLeavingWithDuration(_methodName, _stopwatch.Elapsed);
                }
            }
        }

    }
}
