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

        public override void Leaving([CallerMemberName] string name = "")
        {
            LogWithOperation(TraceEventType.Verbose, $"Leaving {name}", name);
        }

        private void LogWithOperation(TraceEventType eventType, string message, string operation)
        {
            var enhancedMessage = FormatEnhancedLogMessage(message, operation);
            base.Trace(eventType, enhancedMessage);
        }

        private string FormatEnhancedLogMessage(string message, string operation)
        {
            var operationPart = !string.IsNullOrEmpty(operation) ? $"[{operation}]" : "";
            return $"{operationPart} {message}".TrimEnd();
        }
    }
}