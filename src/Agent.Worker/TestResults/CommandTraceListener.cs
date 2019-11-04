// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;

namespace Microsoft.VisualStudio.Services.Agent.Worker.TestResults
{
    class CommandTraceListener : TraceListener
    {
        private readonly IExecutionContext _context;

        public CommandTraceListener(IExecutionContext executionContext)
        {
            _context = executionContext;
        }

        public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id, string message)
        {
            switch (eventType)
            {
                case TraceEventType.Warning:
                    _context.Warning(message);
                    break;
                case TraceEventType.Information:
                    _context.Output(message);
                    break;
                case TraceEventType.Error:
                    _context.Error(message);
                    break;
                default:
                    _context.Debug(message);
                    break;
            }
        }

        public override void Write(string message)
        {
            _context.Debug(message);
        }

        public override void WriteLine(string message)
        {
            _context.Debug(message);
        }
    }
}
