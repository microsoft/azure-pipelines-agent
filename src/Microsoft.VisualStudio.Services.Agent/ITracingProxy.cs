// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;

namespace Microsoft.VisualStudio.Services.Agent
{
    // Interface for TracingProxy to allow TraceManager to use an abstraction.
    [ServiceLocator(Default = typeof(TracingProxy))]
    public interface ITracingProxy : IDisposable
    {
        Tracing ExchangeInner(Tracing newInner);
        void ReplaceInner(Func<Tracing> factory);
        void Info(string message, string operation = "");
        void Info(object item, string operation = "");

#pragma warning disable CA1716 // Identifiers should not match keywords - maintaining compatibility
        void Error(Exception exception, string operation = "");
        void Error(string message, string operation = "");
        void Warning(string message, string operation = "");
        void Verbose(string message, string operation = "");
        void Verbose(object item, string operation = "");
        void Entering(string name = "");
        void Leaving(string name = "");
    }
}
