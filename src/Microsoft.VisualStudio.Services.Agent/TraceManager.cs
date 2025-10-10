// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using Agent.Sdk.SecretMasking;
using Agent.Sdk.Knob;

namespace Microsoft.VisualStudio.Services.Agent
{
    [ServiceLocator(Default = typeof(TraceManager))]
    public interface ITraceManager : IAgentService, IDisposable
    {
        SourceSwitch Switch { get; }
        Tracing this[string name] { get; }
        void SetEnhancedLoggingEnabled(bool enabled);
    }

    public sealed class TraceManager : AgentService, ITraceManager
    {
        private readonly ConcurrentDictionary<string, ITracingProxy> _sources = new(StringComparer.OrdinalIgnoreCase);
        private readonly HostTraceListener _hostTraceListener;
        private readonly TraceSetting _traceSetting;
        private readonly ILoggedSecretMasker _secretMasker;
        private readonly IKnobValueContext _knobValueContext;
        private readonly ICorrelationContextManager _correlationContextManager;

        // Enhanced logging state (affects new and existing trace sources)
        private volatile bool _enhancedLoggingEnabled;
        private readonly object _switchLock = new();

        public TraceManager(HostTraceListener traceListener, ILoggedSecretMasker secretMasker, IKnobValueContext knobValueContext)
            : this(traceListener, new TraceSetting(), secretMasker, knobValueContext)
        {
        }

        public TraceManager(HostTraceListener traceListener, TraceSetting traceSetting, ILoggedSecretMasker secretMasker, IKnobValueContext knobValueContext)
        {
            ArgUtil.NotNull(traceListener, nameof(traceListener));
            ArgUtil.NotNull(traceSetting, nameof(traceSetting));
            ArgUtil.NotNull(secretMasker, nameof(secretMasker));
            ArgUtil.NotNull(knobValueContext, nameof(knobValueContext));

            _hostTraceListener = traceListener;
            _traceSetting = traceSetting;
            _secretMasker = secretMasker;
            _knobValueContext = knobValueContext;

            // Get correlation context manager from HostContext
            if (knobValueContext is IHostContext hostContext)
            {
                _correlationContextManager = hostContext.CorrelationContextManager;
            }
            else
            {
                throw new ArgumentException("knobValueContext must be IHostContext to support correlation", nameof(knobValueContext));
            }

            // Initialize from knob (which may be set via environment at process start)
            _enhancedLoggingEnabled = AgentKnobs.UseEnhancedLogging.GetValue(_knobValueContext).AsBoolean();

            Switch = new SourceSwitch("VSTSAgentSwitch")
            {
                Level = _traceSetting.DefaultTraceLevel.ToSourceLevels()
            };
        }

        public SourceSwitch Switch { get; private set; }

        public Tracing this[string name] => (Tracing)_sources.GetOrAdd(name, CreateTracingProxy);

        /// <summary>
        /// Toggle enhanced logging across all existing sources if state changed.
        /// </summary>
        public void SetEnhancedLoggingEnabled(bool enabled)
        {
            lock (_switchLock)
            {
                if (_enhancedLoggingEnabled == enabled)
                {
                    return; // no-op
                }

                _enhancedLoggingEnabled = enabled;
                SwitchExistingTraceSources(enabled);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (!disposing)
            {
                return;
            }

            foreach (var traceSource in _sources.Values)
            {
                var oldInner = traceSource.ExchangeInner(null);
                oldInner?.Dispose();
                traceSource.Dispose();
            }

            _sources.Clear();

            // Dispose the HostTraceListener to prevent "Bad file descriptor" errors on POSIX systems
            _hostTraceListener?.Dispose();
        }

        private ITracingProxy CreateTracingProxy(string name)
        {
            var sourceSwitch = GetSourceSwitch(name);
            var proxy = new TracingProxy(name, _secretMasker, sourceSwitch, _hostTraceListener);
            var inner = CreateInnerTracing(name, sourceSwitch, _enhancedLoggingEnabled);
            proxy.ExchangeInner(inner);
            return proxy;
        }

        private Tracing CreateInnerTracing(string name, SourceSwitch sourceSwitch, bool enhanced)
        {
            return enhanced
                ? new EnhancedTracing(name, _secretMasker, _correlationContextManager, sourceSwitch, _hostTraceListener)
                : new Tracing(name, _secretMasker, sourceSwitch, _hostTraceListener);
        }

        private SourceSwitch GetSourceSwitch(string name)
        {
            if (_traceSetting.DetailTraceSetting.TryGetValue(name, out TraceLevel sourceTraceLevel))
            {
                return new SourceSwitch("VSTSAgentSubSwitch")
                {
                    Level = sourceTraceLevel.ToSourceLevels()
                };
            }

            return Switch;
        }

        /// <summary>
        /// Switches existing trace sources to match the specified enhanced logging state.
        /// </summary>
        private void SwitchExistingTraceSources(bool shouldUseEnhanced)
        {
            foreach (var kvp in _sources)
            {
                var name = kvp.Key;
                var proxy = kvp.Value;
                var sourceSwitch = GetSourceSwitch(name);
                
                proxy.ReplaceInner(() => CreateInnerTracing(name, sourceSwitch, shouldUseEnhanced));
            }
        }
    }
}
