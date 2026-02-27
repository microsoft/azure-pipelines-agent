// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace Microsoft.VisualStudio.Services.Agent
{
    public sealed class HostTraceListener : TextWriterTraceListener
    {
        public bool DisableConsoleReporting { get; set; }
        private const string _logFileNamingPattern = "{0}_{1:yyyyMMdd-HHmmss}-utc.log";
        private string _logFileDirectory;
        private string _logFilePrefix;
        private bool _enablePageLog = false;
        private bool _enableLogRetention = false;
        private int _currentPageSize;
        private int _pageSizeLimit;
        private int _retentionDays;
        private bool _diagErrorDetected = false;
        private string _logFilePath;
        private bool _disposed = false;

        public HostTraceListener(string logFileDirectory, string logFilePrefix, int pageSizeLimit, int retentionDays)
            : base()
        {
            ArgUtil.NotNullOrEmpty(logFileDirectory, nameof(logFileDirectory));
            ArgUtil.NotNullOrEmpty(logFilePrefix, nameof(logFilePrefix));
            _logFileDirectory = logFileDirectory;
            _logFilePrefix = logFilePrefix;

            Directory.CreateDirectory(_logFileDirectory);

            if (pageSizeLimit > 0)
            {
                _enablePageLog = true;
                _pageSizeLimit = pageSizeLimit * 1024 * 1024;
                _currentPageSize = 0;
            }

            if (retentionDays > 0)
            {
                _enableLogRetention = true;
                _retentionDays = retentionDays;
            }

            Writer = CreatePageLogWriter();
        }

        public HostTraceListener(string logFile)
            : base()
        {
            ArgUtil.NotNullOrEmpty(logFile, nameof(logFile));
            _logFilePath = logFile;
            Directory.CreateDirectory(Path.GetDirectoryName(_logFilePath));
            // Use StreamWriter constructor that handles FileStream internally
            Writer = new StreamWriter(_logFilePath, append: false, Encoding.UTF8, bufferSize: 4096);
        }

        // Copied and modified slightly from .Net Core source code. Modification was required to make it compile.
        // There must be some TraceFilter extension class that is missing in this source code.
        public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id, string message)
        {
            if (Filter != null && !Filter.ShouldTrace(eventCache, source, eventType, id, message, null, null, null))
            {
                return;
            }

            WriteHeader(source, eventType, id);
            WriteLine(message);
            WriteFooter(eventCache);

            if (!_diagErrorDetected && !DisableConsoleReporting && eventType < TraceEventType.Warning)
            {
                Console.WriteLine(StringUtil.Loc("FoundErrorInTrace", eventType.ToString(), _logFilePath));
                _diagErrorDetected = true;
            }
        }

        public override void WriteLine(string message)
        {
            base.WriteLine(message);
            if (_enablePageLog)
            {
                int messageSize = UTF8Encoding.UTF8.GetByteCount(message);
                _currentPageSize += messageSize;
                if (_currentPageSize > _pageSizeLimit)
                {
                    Flush();
                    if (Writer != null && !_disposed)
                    {
                        Writer.Dispose();
                        Writer = null;
                    }

                    if (!_disposed)
                    {
                        Writer = CreatePageLogWriter();
                        _currentPageSize = 0;
                    }
                }
            }

            Flush();
        }

        public override void Write(string message)
        {
            base.Write(message);
            if (_enablePageLog)
            {
                int messageSize = UTF8Encoding.UTF8.GetByteCount(message);
                _currentPageSize += messageSize;
            }

            Flush();
        }

        internal bool IsEnabled(TraceOptions opts)
        {
            return (opts & TraceOutputOptions) != 0;
        }

        // Altered from the original .Net Core implementation.
        private void WriteHeader(string source, TraceEventType eventType, int id)
        {
            string type = null;
            switch (eventType)
            {
                case TraceEventType.Critical:
                    type = "CRIT";
                    break;
                case TraceEventType.Error:
                    type = "ERR ";
                    break;
                case TraceEventType.Warning:
                    type = "WARN";
                    break;
                case TraceEventType.Information:
                    type = "INFO";
                    break;
                case TraceEventType.Verbose:
                    type = "VERB";
                    break;
                default:
                    type = eventType.ToString();
                    break;
            }

            Write(StringUtil.Format("[{0:u} {1} {2}] ", DateTime.UtcNow, type, source));
        }

        // Copied and modified slightly from .Net Core source code to make it compile. The original code
        // accesses a private indentLevel field. In this code it has been modified to use the getter/setter.
        private void WriteFooter(TraceEventCache eventCache)
        {
            if (eventCache == null)
                return;

            IndentLevel++;
            if (IsEnabled(TraceOptions.ProcessId))
                WriteLine("ProcessId=" + eventCache.ProcessId);

            if (IsEnabled(TraceOptions.ThreadId))
                WriteLine("ThreadId=" + eventCache.ThreadId);

            if (IsEnabled(TraceOptions.DateTime))
                WriteLine("DateTime=" + eventCache.DateTime.ToString("o", CultureInfo.InvariantCulture));

            if (IsEnabled(TraceOptions.Timestamp))
                WriteLine("Timestamp=" + eventCache.Timestamp);

            IndentLevel--;
        }

        private StreamWriter CreatePageLogWriter()
        {
            if (_enableLogRetention)
            {
                DirectoryInfo diags = new DirectoryInfo(_logFileDirectory);
                var logs = diags.GetFiles($"{_logFilePrefix}*.log");
                foreach (var log in logs)
                {
                    if (log.LastWriteTimeUtc.AddDays(_retentionDays) < DateTime.UtcNow)
                    {
                        try
                        {
                            log.Delete();
                        }
                        catch (Exception)
                        {
                            // catch Exception and continue
                            // we shouldn't block logging and fail the agent if the agent can't delete an older log file.
                        }
                    }
                }
            }

            string fileName = StringUtil.Format(_logFileNamingPattern, _logFilePrefix, DateTime.UtcNow);
            _logFilePath = Path.Combine(_logFileDirectory, fileName);
            
            // Use StreamWriter constructor that handles FileStream internally
            // This eliminates the dual resource management issue
            if (File.Exists(_logFilePath))
            {
                return new StreamWriter(_logFilePath, append: true, Encoding.UTF8, bufferSize: 4096);
            }
            else
            {
                return new StreamWriter(_logFilePath, append: false, Encoding.UTF8, bufferSize: 4096);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _disposed = true;
                
                // Safely dispose the current writer if it exists
                // No exception handling needed - we control the state
                Writer?.Dispose();
                Writer = null;
            }
            base.Dispose(disposing);
        }
    }
}