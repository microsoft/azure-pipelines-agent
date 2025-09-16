// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using Agent.Sdk.SecretMasking;

namespace Microsoft.VisualStudio.Services.Agent
{
    public sealed class EnhancedTracing : Tracing
    {
        public EnhancedTracing(string name, ILoggedSecretMasker secretMasker, SourceSwitch sourceSwitch, HostTraceListener traceListener)
            : base(name, secretMasker, sourceSwitch, traceListener)
        {
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
            return EnhancedCorrelationContext.Build();
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

    // Ambient correlation context for enhanced logs only
    public static class EnhancedCorrelationContext
    {
        private static readonly AsyncLocal<string> _step = new AsyncLocal<string>();
        private static readonly AsyncLocal<string> _task = new AsyncLocal<string>();

        // Thread-local reference to current ExecutionContext for correlation
        private static readonly AsyncLocal<object> _currentExecutionContext = new AsyncLocal<object>();

        public static void SetStep(string stepId) => _step.Value = stepId;
        public static void ClearStep() => _step.Value = null;
        public static void SetTask(string taskId) => _task.Value = taskId;
        public static void ClearTask() => _task.Value = null;

        // Set the current ExecutionContext for this async context
        public static void SetCurrentExecutionContext(object executionContext)
        {
            _currentExecutionContext.Value = executionContext;
        }

        public static void ClearCurrentExecutionContext()
        {
            _currentExecutionContext.Value = null;
        }

        internal static string Build()
        {
            // Try to get correlation from current ExecutionContext first
            var currentContext = _currentExecutionContext.Value;
            if (currentContext != null)
            {
                // Use reflection to call BuildCorrelationId method safely
                try
                {
                    var method = currentContext.GetType().GetMethod("BuildCorrelationId");
                    if (method != null)
                    {
                        var correlationId = method.Invoke(currentContext, null) as string;
                        if (!string.IsNullOrEmpty(correlationId))
                        {
                            return correlationId;
                        }
                    }
                }
                catch
                {
                    // Fall through to legacy implementation if reflection fails
                }
            }

            // Fall back to legacy AsyncLocal implementation
            var step = _step.Value;
            var task = _task.Value;

            if (string.IsNullOrEmpty(step))
            {
                return string.IsNullOrEmpty(task) ? string.Empty : $"TASK-{task}";
            }

            return string.IsNullOrEmpty(task) ? $"STEP-{step}" : $"STEP-{step}|TASK-{task}";
        }
    }
}
