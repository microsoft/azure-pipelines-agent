using Agent.Plugins.Log.TestResultParser.Contracts;
using Agent.Sdk;

namespace Agent.Plugins.Log.TestResultParser.Plugin
{
    public class TraceLogger : ITraceLogger
    {
        public TraceLogger(IAgentLogPluginContext context, bool debugLoggingEnabled)
        {
            _context = context;
            _debug = debugLoggingEnabled;
        }

        #region interface implementation

        /// <inheritdoc />
        void ITraceLogger.Warning(string text)
        {
            _context.Output($"Warning: {text}");
        }

        /// <inheritdoc />
        void ITraceLogger.Error(string error)
        {
            _context.Output($"Error: {error}");
        }

        /// <inheritdoc />
        void ITraceLogger.Verbose(string text)
        {
            if (_debug)
            {
                _context.Output($"Debug: {text}");
            }
        }

        /// <inheritdoc />
        void ITraceLogger.Info(string text)
        {
            _context.Output(text);
        }

        #endregion

        private readonly IAgentLogPluginContext _context;
        private readonly bool _debug;
    }
}
