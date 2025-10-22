// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;

namespace Microsoft.VisualStudio.Services.Agent
{
    /// <summary>
    /// Interface for objects that can provide correlation IDs
    /// </summary>
    public interface ICorrelationContext
    {
        /// <summary>
        /// Builds the correlation ID for this context
        /// </summary>
        string BuildCorrelationId();
    }

    /// <summary>
    /// Manages correlation context for tracking logs across steps and tasks.
    /// </summary>
    [ServiceLocator(Default = typeof(CorrelationContextManager))]
    public interface ICorrelationContextManager : IDisposable
    {
        /// <summary>
        /// Sets the current execution context for correlation tracking
        /// </summary>
        void SetCurrentExecutionContext(ICorrelationContext executionContext);
        
        /// <summary>
        /// Clears the current execution context
        /// </summary>
        void ClearCurrentExecutionContext();
        
        /// <summary>
        /// Builds the correlation ID from the current context
        /// </summary>
        string BuildCorrelationId();
    }

    /// <summary>
    /// Implementation of correlation context manager using AsyncLocal for async flow
    /// </summary>
    internal sealed class CorrelationContextManager : ICorrelationContextManager
    {
        private readonly AsyncLocal<ICorrelationContext> _currentExecutionContext = new AsyncLocal<ICorrelationContext>();

        public void SetCurrentExecutionContext(ICorrelationContext executionContext)
        {
            _currentExecutionContext.Value = executionContext;
        }

        public void ClearCurrentExecutionContext()
        {
            _currentExecutionContext.Value = null;
        }

        public string BuildCorrelationId()
        {
            var currentContext = _currentExecutionContext.Value;
            return currentContext?.BuildCorrelationId() ?? string.Empty;
        }

        public void Dispose()
        {
            // Clear context on disposal
            _currentExecutionContext.Value = null;
        }
    }

    /// <summary>
    /// No-op implementation of ICorrelationContextManager for backward compatibility.
    /// Used when IHostContext is not available but correlation functionality is requested.
    /// This prevents breaking existing code while gracefully disabling enhanced logging correlation.
    /// </summary>
    internal sealed class NoOpCorrelationContextManager : ICorrelationContextManager
    {
        public void SetCurrentExecutionContext(ICorrelationContext executionContext)
        {
            // No-op: Do nothing when correlation context is not supported
        }

        public void ClearCurrentExecutionContext()
        {
            // No-op: Do nothing when correlation context is not supported
        }

        public string BuildCorrelationId()
        {
            // Return empty string when correlation is not available
            return string.Empty;
        }

        public void Dispose()
        {
            // No-op: Nothing to dispose
        }
    }
}
