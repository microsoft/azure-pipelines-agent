// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.


using Microsoft.VisualStudio.Services.Agent.Util;
using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Agent.Sdk;
using Agent.Sdk.SecretMasking;

namespace Microsoft.VisualStudio.Services.Agent
{
    public class Tracing : ITraceWriter, IDisposable
    {
        private readonly ILoggedSecretMasker _secretMasker;
        private readonly TraceSource _traceSource;
        protected string _componentName;

        public Tracing(string name, ILoggedSecretMasker secretMasker, SourceSwitch sourceSwitch, HostTraceListener traceListener)
        {
            ArgUtil.NotNull(secretMasker, nameof(secretMasker));
            _secretMasker = secretMasker;
            _traceSource = new TraceSource(name);
            _traceSource.Switch = sourceSwitch;

            // Remove the default trace listener.
            if (_traceSource.Listeners.Count > 0 &&
                _traceSource.Listeners[0] is DefaultTraceListener)
            {
                _traceSource.Listeners.RemoveAt(0);
            }

            _traceSource.Listeners.Add(traceListener);
        }

        public virtual void Info(string message, [CallerMemberName] string operation = "")
        {
            Trace(TraceEventType.Information, message);
        }

        // public virtual void Info(string format, [CallerMemberName] string operation = "", params object[] args)
        // {
        //     Trace(TraceEventType.Information, StringUtil.Format(format, args));
        // }

        public virtual void Info(object item, [CallerMemberName] string operation = "")
        {
            string json = JsonConvert.SerializeObject(item, Formatting.Indented);
            Trace(TraceEventType.Information, json);
        }

#pragma warning disable CA1716 // Identifiers should not match keywords - maintaining compatibility
        public virtual void Error(Exception exception, [CallerMemberName] string operation = "")
        {
            ArgUtil.NotNull(exception, nameof(exception));
            Trace(TraceEventType.Error, exception.ToString());
        }

        // Do not remove the non-format overload.
        public virtual void Error(string message, [CallerMemberName] string operation = "")
        {
            Trace(TraceEventType.Error, message);
        }

        // public virtual void Error(string format, [CallerMemberName] string operation = "", params object[] args)
        // {
        //     Trace(TraceEventType.Error, StringUtil.Format(format, args));
        // }

        // Do not remove the non-format overload.
        public virtual void Warning(string message, [CallerMemberName] string operation = "")
        {
            Trace(TraceEventType.Warning, message);
        }

        // public virtual void Warning(string format, [CallerMemberName] string operation = "", params object[] args)
        // {
        //     Trace(TraceEventType.Warning, StringUtil.Format(format, args));
        // }

        // Do not remove the non-format overload.
        public virtual void Verbose(string message, [CallerMemberName] string operation = "")
        {
            Trace(TraceEventType.Verbose, message);
        }

        // public virtual void Verbose(string format, [CallerMemberName] string operation = "", params object[] args)
        // {
        //     Trace(TraceEventType.Verbose, StringUtil.Format(format, args));
        // }

        public virtual void Verbose(object item, [CallerMemberName] string operation = "")
        {
            string json = item?.ToString() ?? "null";
            Trace(TraceEventType.Verbose, json);
        }

        public virtual void Entering([CallerMemberName] string name = "")
        {
            Trace(TraceEventType.Verbose, $"Entering {name}");
        }

        public virtual void Leaving([CallerMemberName] string name = "")
        {
            Trace(TraceEventType.Verbose, $"Leaving {name}");
        }

        public virtual IDisposable EnteringWithDuration([CallerMemberName] string name = "")
        {
            Entering(name);
            return null;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Trace(TraceEventType eventType, string message)
        {
            ArgUtil.NotNull(_traceSource, nameof(_traceSource));
            _traceSource.TraceEvent(
                eventType: eventType,
                id: 0,
                message: _secretMasker.MaskSecrets(message));
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Important: do not close the TraceSource here because it will close
                // the shared HostTraceListener used across the entire process.
                // Closing the shared listener from one Tracing instance would stop all logging.
                _traceSource.Flush();
            }
        }
    }
}
