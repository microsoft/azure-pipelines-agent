// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Agent.Sdk.SecretMasking;

namespace Microsoft.VisualStudio.Services.Agent
{
    // A stable Tracing handle that forwards to a swappable inner Tracing implementation.
    // This lets callers keep their Tracing reference while TraceManager switches
    // between standard and enhanced tracing at runtime.
    public sealed class TracingProxy : Tracing, ITracingProxy
    {
        private volatile Tracing _inner;
        private readonly object _swapLock = new object();

        public TracingProxy(string name, ILoggedSecretMasker secretMasker, SourceSwitch sourceSwitch, HostTraceListener traceListener)
            : base(name, secretMasker, sourceSwitch, traceListener)
        {
        }

        // Create and swap inner using a factory, ensuring proper disposal on all paths.
        public void ReplaceInner(Func<Tracing> factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            Tracing newInner = null;
            Tracing oldInner = null;
            try
            {
                newInner = factory();
                oldInner = ExchangeInner(newInner);
                // Ownership transferred to proxy
                newInner = null;
            }
            finally
            {
                newInner?.Dispose();
            }

            oldInner?.Dispose();
        }

        // Swap the inner implementation and return the previous one for disposal by the caller.
        public Tracing ExchangeInner(Tracing newInner)
        {
            lock (_swapLock)
            {
                var prev = _inner;
                _inner = newInner;
                return prev;
            }
        }

        // Override all public logging methods to forward to the current inner implementation
        // so that enhanced formatting (or standard) is applied consistently.
        public override void Info(string message, [CallerMemberName] string operation = "")
        {
            var inner = _inner;
            if (inner is null)
            {
                base.Info(message, operation);
                return;
            }
            inner.Info(message, operation);
        }

        public override void Info(object item, [CallerMemberName] string operation = "")
        {
            var inner = _inner;
            if (inner is null)
            {
                base.Info(item, operation);
                return;
            }
            inner.Info(item, operation);
        }

        public override void Error(Exception exception, [CallerMemberName] string operation = "")
        {
            var inner = _inner;
            if (inner is null)
            {
                base.Error(exception, operation);
                return;
            }
            inner.Error(exception, operation);
        }

        public override void Error(string message, [CallerMemberName] string operation = "")
        {
            var inner = _inner;
            if (inner is null)
            {
                base.Error(message, operation);
                return;
            }
            inner.Error(message, operation);
        }

        public override void Warning(string message, [CallerMemberName] string operation = "")
        {
            var inner = _inner;
            if (inner is null)
            {
                base.Warning(message, operation);
                return;
            }
            inner.Warning(message, operation);
        }

        public override void Verbose(string message, [CallerMemberName] string operation = "")
        {
            var inner = _inner;
            if (inner is null)
            {
                base.Verbose(message, operation);
                return;
            }
            inner.Verbose(message, operation);
        }

        public override void Verbose(object item, [CallerMemberName] string operation = "")
        {
            var inner = _inner;
            if (inner is null)
            {
                base.Verbose(item, operation);
                return;
            }
            inner.Verbose(item, operation);
        }

        public override void Entering([CallerMemberName] string name = "")
        {
            var inner = _inner;
            if (inner is null)
            {
                base.Entering(name);
                return;
            }
            inner.Entering(name);
        }

        public override void Leaving([CallerMemberName] string name = "")
        {
            var inner = _inner;
            if (inner is null)
            {
                base.Leaving(name);
                return;
            }
            inner.Leaving(name);
        }

        public override IDisposable EnteringWithDuration([CallerMemberName] string name = "")
        {
            var inner = _inner;
            if (inner is null)
            {
                return base.EnteringWithDuration(name);
            }
            return inner.EnteringWithDuration(name);
        }

        protected override void Dispose(bool disposing)
        {
            // Do not dispose the inner here; TraceManager owns inner lifetimes.
            base.Dispose(disposing);
        }
    }
}
