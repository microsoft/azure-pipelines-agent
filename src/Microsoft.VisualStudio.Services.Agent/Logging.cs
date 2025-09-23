// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Microsoft.VisualStudio.Services.Agent
{
    [ServiceLocator(Default = typeof(PagingLogger))]
    public interface IPagingLogger : IAgentService
    {
        long TotalLines { get; }
        void Setup(Guid timelineId, Guid timelineRecordId);

        void Write(string message);

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1716: Identifiers should not match keywords")]
        void End();
    }

    public class PagingLogger : AgentService, IPagingLogger, IDisposable
    {
        public static string PagingFolder = "pages";

        // 8 MB
        public const int PageSize = 8 * 1024 * 1024;

        private Guid _timelineId;
        private Guid _timelineRecordId;
        private string _pageId;
        private StreamWriter _pageWriter;
        private int _byteCount;
        private int _pageCount;
        private long _totalLines;
        private string _dataFileName;
        private string _pagesFolder;
        private IJobServerQueue _jobServerQueue;
        private const string groupStartTag = "##[group]";
        private const string groupEndTag = "##[endgroup]";
        private bool _groupOpened = false;
        public long TotalLines => _totalLines;

        public override void Initialize(IHostContext hostContext)
        {
            ArgUtil.NotNull(hostContext, nameof(hostContext));
            base.Initialize(hostContext);
            _totalLines = 0;
            _pageId = Guid.NewGuid().ToString();
            _pagesFolder = Path.Combine(hostContext.GetDiagDirectory(), PagingFolder);
            _jobServerQueue = HostContext.GetService<IJobServerQueue>();
            Directory.CreateDirectory(_pagesFolder);
        }

        public void Setup(Guid timelineId, Guid timelineRecordId)
        {
            _timelineId = timelineId;
            _timelineRecordId = timelineRecordId;
        }

        //
        // Write a metadata file with id etc, point to pages on disk.
        // Each page is a guid_#.  As a page rolls over, it events it's done
        // and the consumer queues it for upload
        // Ensure this is lazy.  Create a page on first write
        //
        public void Write(string message)
        {
            // lazy creation on write
            if (_pageWriter == null)
            {
                Create();
            }

            if (message.Contains(groupStartTag, StringComparison.OrdinalIgnoreCase))
            {
                _groupOpened = true;
            } 
            if (_groupOpened && message.Contains(groupEndTag, StringComparison.OrdinalIgnoreCase))
            {
                // Ignore group end tag only if group was opened, otherwise it is a normal message 
                // because in web console ##[endgroup] becomes empty line without ##[group] tag
                _groupOpened = false;
                _totalLines--;
            } 

            string line = $"{DateTime.UtcNow.ToString("O")} {message}";
            _pageWriter.WriteLine(line);

            _totalLines++;
            if (line.IndexOf('\n') != -1)
            {
                foreach (char c in line)
                {
                    if (c == '\n')
                    {
                        _totalLines++;
                    }
                }
            }

            _byteCount += System.Text.Encoding.UTF8.GetByteCount(line);
            if (_byteCount >= PageSize)
            {
                NewPage();
            }
        }

        public void End()
        {
            // Prevent multiple disposal attempts - only call EndPage if writer still exists
            // This is important because both End() and Dispose() can be called during cleanup
            if (_pageWriter != null)
            {
                EndPage();
            }
        }

        private void Create()
        {
            NewPage();
        }

        private void NewPage()
        {
            EndPage();
            _byteCount = 0;
            _dataFileName = Path.Combine(_pagesFolder, $"{_pageId}_{++_pageCount}.log");
            // Create StreamWriter directly with file path - it will handle the FileStream internally
            _pageWriter = new StreamWriter(_dataFileName, append: false, System.Text.Encoding.UTF8);
        }

        private void EndPage()
        {
            if (_pageWriter != null)
            {
                // StreamWriter manages the underlying file handle across all platforms
                // This avoids platform-specific disposal timing issues (like "Bad file descriptor" on macOS)
                try
                {
                    _pageWriter.Flush();
                }
                catch (ObjectDisposedException)
                {
                    // StreamWriter was already disposed - this is safe to ignore
                    // Can happen during shutdown or cleanup scenarios
                }
                catch (IOException)
                {
                    // File handle may be invalid (e.g., "Bad file descriptor" on POSIX systems)
                    // This can happen if the underlying file was closed externally
                    // Safe to ignore as we're disposing anyway
                }
                
                _pageWriter.Dispose();
                _pageWriter = null;
                
                _jobServerQueue.QueueFileUpload(_timelineId, _timelineRecordId, "DistributedTask.Core.Log", "CustomToolLog", _dataFileName, true);
            }
        }
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing && _pageWriter != null)
            {
                // Only call EndPage if we haven't already disposed the writer
                // This prevents double-disposal which causes "Bad file descriptor" on macOS/Linux
                EndPage();
            }
        }

    }
}
