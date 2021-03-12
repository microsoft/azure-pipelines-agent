// Copyright (c) Microsoft Corporation.  All rights reserved.

using System;
using System.Diagnostics;
using System.Globalization;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    /// <summary>
    /// This is a utitily class used for recording timing
    /// information in the verbose trace. Its usage is 
    /// using (SimpleTimer timer = new SimpleTimer("MyOperation"))
    ///  {
    ///     MyOperation...
    ///  }
    ///  A   message is recorded in the verbose trace with the time taken
    ///  for myoperation.
    /// </summary>
    internal class SimpleTimer : IDisposable
    {
        #region Public Methods
        /// <summary>
        /// Constructor that takes the name of the timer to be 
        /// printed in the trace.
        /// </summary>
        public SimpleTimer(string timerName, Action<string> debug) : this(timerName, debug, 0)
        {

        }

        /// <summary>
        /// Creates a timer with threshold. A perf message is logged only if
        /// the time elapsed is more than the threshold.
        /// </summary>
        public SimpleTimer(string timerName, Action<string> debug, long thresholdInMilliseconds = Int32.MaxValue)
        {
            _name = timerName;
            _debug = debug;
            _threshold = thresholdInMilliseconds;
            _timer = Stopwatch.StartNew();
        }

        /// <summary>
        /// Implement IDisposable
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Stop the watch and log the trace message with the elapsed time.
        /// </summary>
        private void StopAndLog()
        {
            _timer.Stop();
            _debug(string.Format(CultureInfo.InvariantCulture, "PERF: {0}: took {1} ms", _name, _timer.Elapsed.TotalMilliseconds));

            // TODO : Currently Telemetry is not support in PublishTestResults Library. Uncomment following line of code when we start supporting Telemetry.
            //TelemetryLogger.AddProperties(_name + ":PerfCounter", _timer.Elapsed.TotalMilliseconds);

            if (_timer.Elapsed.TotalMilliseconds >= _threshold)
            {
                _debug(string.Format(CultureInfo.InvariantCulture, "PERF WARNING: {0}: took {1} ms", _name, _timer.Elapsed.TotalMilliseconds));
            }
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }
            if (disposing)
            {
                StopAndLog();
                GC.SuppressFinalize(this);
            }
            _disposed = true;
        }
        #endregion

        #region Private Members
        private readonly Stopwatch _timer;
        private readonly string _name;
        private readonly long _threshold;
        private bool _disposed;
        private readonly Action<string> _debug;
        #endregion
    }
}
