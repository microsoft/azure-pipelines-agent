// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;

namespace Microsoft.VisualStudio.Services.Agent
{
    [ServiceLocator(Default = typeof(JobServerQueue))]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1711: Identifiers should not have incorrect suffix")]
    public interface IJobServerQueue : IAgentService, IThrottlingReporter
    {
        bool ForceDrainWebConsoleQueue { get; set; }
        bool ForceDrainTimelineQueue { get; set; }
        event EventHandler<ThrottlingEventArgs> JobServerQueueThrottling;
        Task ShutdownAsync();
        Task SendTimelineRecordUpdateAsync(Guid timelineId, TimelineRecord timelineRecord);
        void Start(Pipelines.AgentJobRequestMessage jobRequest);
        void QueueWebConsoleLine(Guid stepRecordId, string line, long lineNumber);
        void QueueFileUpload(Guid timelineId, Guid timelineRecordId, string type, string name, string path, bool deleteSource);
        void QueueTimelineRecordUpdate(Guid timelineId, TimelineRecord timelineRecord);
        void UpdateWebConsoleLineRate(Int32 rateInMillis);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1711: Identifiers should not have incorrect suffix")]
    public sealed class JobServerQueue : AgentService, IJobServerQueue
    {
        // Default delay for Dequeue process
        private static readonly TimeSpan _aggressiveDelayForWebConsoleLineDequeue = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan _delayForWebConsoleLineDequeueDefault = TimeSpan.FromMilliseconds(1000);
        private static readonly TimeSpan _delayForTimelineUpdateDequeue = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan _delayForFileUploadDequeue = TimeSpan.FromMilliseconds(1000);

        // Job message information
        private Guid _scopeIdentifier;
        private string _hubName;
        private Guid _planId;
        private Guid _jobTimelineId;
        private Guid _jobTimelineRecordId;

        // queue for web console line
        private readonly ConcurrentQueue<ConsoleLineInfo> _webConsoleLineQueue = new ConcurrentQueue<ConsoleLineInfo>();

        // queue for file upload (log file or attachment)
        private readonly ConcurrentQueue<UploadFileInfo> _fileUploadQueue = new ConcurrentQueue<UploadFileInfo>();

        // queue for timeline or timeline record update (one queue per timeline)
        private readonly ConcurrentDictionary<Guid, ConcurrentQueue<TimelineRecord>> _timelineUpdateQueue = new ConcurrentDictionary<Guid, ConcurrentQueue<TimelineRecord>>();

        // indicate how many timelines we have, we will process _timelineUpdateQueue base on the order of timeline in this list
        private readonly List<Guid> _allTimelines = new List<Guid>();

        // bufferd timeline records that fail to update
        private readonly Dictionary<Guid, List<TimelineRecord>> _bufferedRetryRecords = new Dictionary<Guid, List<TimelineRecord>>();

        // Task for each queue's dequeue process
        private Task _webConsoleLineDequeueTask;
        private Task _fileUploadDequeueTask;
        private Task _timelineUpdateDequeueTask;

        // common
        private IJobServer _jobServer;
        private Task[] _allDequeueTasks;
        private readonly TaskCompletionSource<int> _jobCompletionSource = new TaskCompletionSource<int>();
        private bool _queueInProcess = false;

        public event EventHandler<ThrottlingEventArgs> JobServerQueueThrottling;

        // Web console dequeue will start with process queue every 500ms for the first 15*2 times (~15 seconds).
        // Then the dequeue will happen every 1s or whatever the server tells us
        // In this way, customer still can get instance live console output on job start,
        // at the same time we can cut the load to server after the build run for more than 60s
        private int _webConsoleLineAggressiveDequeueCount = 0;
        private int _webConsoleLineUpdateRate = (int)_delayForWebConsoleLineDequeueDefault.TotalMilliseconds;
        private const int _webConsoleLineAggressiveDequeueLimit = 2 * 15;
        private bool _webConsoleLineAggressiveDequeue = true;
        private TaskCompletionSource<object> _webConsoleLinesDequeueNow = new TaskCompletionSource<object>();
        private bool _firstConsoleOutputs = true;
        private bool _writeToBlobStoreLogs = false;
        private bool _writeToBlobStoreAttachments = false;
        private bool _debugMode = false;

        public bool ForceDrainWebConsoleQueue { get; set; }
        public bool ForceDrainTimelineQueue { get; set; }

        public override void Initialize(IHostContext hostContext)
        {
            base.Initialize(hostContext);
            _jobServer = hostContext.GetService<IJobServer>();
        }

        public void Start(Pipelines.AgentJobRequestMessage jobRequest)
        {
            Trace.Entering();

            if (_queueInProcess)
            {
                Trace.Info("No-opt, all queue process tasks are running.");
                return;
            }

            ArgUtil.NotNull(jobRequest, nameof(jobRequest));
            ArgUtil.NotNull(jobRequest.Plan, nameof(jobRequest.Plan));
            ArgUtil.NotNull(jobRequest.Timeline, nameof(jobRequest.Timeline));

            _scopeIdentifier = jobRequest.Plan.ScopeIdentifier;
            _hubName = jobRequest.Plan.PlanType;
            _planId = jobRequest.Plan.PlanId;
            _jobTimelineId = jobRequest.Timeline.Id;
            _jobTimelineRecordId = jobRequest.JobId;

            if (jobRequest.Variables.TryGetValue(WellKnownDistributedTaskVariables.LogToBlobstorageService, out var logToBlob))
            {
                Boolean.TryParse(logToBlob.Value, out _writeToBlobStoreLogs);
            }

            if (jobRequest.Variables.TryGetValue(WellKnownDistributedTaskVariables.UploadTimelineAttachmentsToBlob, out var attachToBlob))
            {
                Boolean.TryParse(attachToBlob.Value, out _writeToBlobStoreAttachments);
            }

            if (jobRequest.Variables.TryGetValue(WellKnownDistributedTaskVariables.PostLinesSpeed, out var postLinesSpeed))
            {
                if (!Int32.TryParse(postLinesSpeed.Value, out _webConsoleLineUpdateRate))
                {
                    _webConsoleLineUpdateRate = (int)_delayForWebConsoleLineDequeueDefault.TotalMilliseconds;
                }
            }

            if (jobRequest.Variables.TryGetValue(Constants.Variables.System.Debug, out var debug))
            {
                Boolean.TryParse(debug.Value, out _debugMode);
            }

            // Server already create the job timeline
            _timelineUpdateQueue[_jobTimelineId] = new ConcurrentQueue<TimelineRecord>();
            _allTimelines.Add(_jobTimelineId);

            // Start three dequeue task
            Trace.Info("Start process web console line queue.");
            _webConsoleLineDequeueTask = ProcessWebConsoleLinesQueueAsync();

            Trace.Info("Start process file upload queue.");
            _fileUploadDequeueTask = ProcessFilesUploadQueueAsync();

            Trace.Info("Start process timeline update queue.");
            _timelineUpdateDequeueTask = ProcessTimelinesUpdateQueueAsync();

            _allDequeueTasks = new Task[] { _webConsoleLineDequeueTask, _fileUploadDequeueTask, _timelineUpdateDequeueTask };
            _queueInProcess = true;
        }

        // WebConsoleLine queue and FileUpload queue are always best effort
        // TimelineUpdate queue error will become critical when timeline records contain output variabls.
        public async Task ShutdownAsync()
        {

            if (!_queueInProcess)
            {
                Trace.Info("No-op, all queue process tasks have been stopped.");
            }

            Trace.Info("Fire signal to shutdown all queues.");
            _jobCompletionSource.TrySetResult(0);

            await Task.WhenAll(_allDequeueTasks);
            _queueInProcess = false;
            Trace.Info("All queue process task stopped.");

            // Drain the queue
            // ProcessWebConsoleLinesQueueAsync() will never throw exception, live console update is always best effort.
            Trace.Verbose("Draining web console line queue.");
            await ProcessWebConsoleLinesQueueAsync(runOnce: true);
            Trace.Info("Web console line queue drained.");

            // ProcessFilesUploadQueueAsync() will never throw exception, log file upload is always best effort.
            Trace.Verbose("Draining file upload queue.");
            await ProcessFilesUploadQueueAsync(runOnce: true);
            Trace.Info("File upload queue drained.");

            // ProcessTimelinesUpdateQueueAsync() will throw exception during shutdown
            // if there is any timeline records that failed to update contains output variables.
            Trace.Verbose("Draining timeline update queue.");
            await ProcessTimelinesUpdateQueueAsync(runOnce: true);
            Trace.Info("Timeline update queue drained.");

            Trace.Info("All queue process tasks have been stopped, and all queues are drained.");
        }

        public void QueueWebConsoleLine(Guid stepRecordId, string line, long lineNumber)
        {
            Trace.Verbose("Enqueue web console line queue: {0}", line);

            _webConsoleLineQueue.Enqueue(new ConsoleLineInfo(stepRecordId, line, lineNumber));
        }

        public void QueueFileUpload(Guid timelineId, Guid timelineRecordId, string type, string name, string path, bool deleteSource)
        {
            ArgUtil.NotEmpty(timelineId, nameof(timelineId));
            ArgUtil.NotEmpty(timelineRecordId, nameof(timelineRecordId));

            // all parameter not null, file path exist.
            var newFile = new UploadFileInfo()
            {
                TimelineId = timelineId,
                TimelineRecordId = timelineRecordId,
                Type = type,
                Name = name,
                Path = path,
                DeleteSource = deleteSource
            };

            Trace.Verbose(StringUtil.Format("Enqueue file upload queue: file '{0}' attach to record {1}", newFile.Path, timelineRecordId));
            _fileUploadQueue.Enqueue(newFile);
        }

        public void QueueTimelineRecordUpdate(Guid timelineId, TimelineRecord timelineRecord)
        {
            ArgUtil.NotEmpty(timelineId, nameof(timelineId));
            ArgUtil.NotNull(timelineRecord, nameof(timelineRecord));
            ArgUtil.NotEmpty(timelineRecord.Id, nameof(timelineRecord.Id));

            _timelineUpdateQueue.TryAdd(timelineId, new ConcurrentQueue<TimelineRecord>());

            Trace.Verbose(StringUtil.Format("Enqueue timeline {0} update queue: {1}", timelineId, timelineRecord.Id));
            _timelineUpdateQueue[timelineId].Enqueue(timelineRecord.Clone());
        }
        public async Task SendTimelineRecordUpdateAsync(Guid timelineId, TimelineRecord timelineRecord)
        {
            ArgUtil.NotEmpty(timelineId, nameof(timelineId));
            ArgUtil.NotNull(timelineRecord, nameof(timelineRecord));
            ArgUtil.NotEmpty(timelineRecord.Id, nameof(timelineRecord.Id));
            var immediateUpdate = new List<TimelineRecord> { timelineRecord.Clone() };
            await _jobServer.UpdateTimelineRecordsAsync(_scopeIdentifier, _hubName, _planId, timelineId, immediateUpdate, CancellationToken.None);
            Trace.Info($"Job timeline record {timelineRecord.Id} state successfully updated to {timelineRecord.State} on server");
        }

        public void ReportThrottling(TimeSpan delay, DateTime expiration)
        {
            Trace.Info($"Receive server throttling report, expect delay {delay} milliseconds till {expiration}");
            var throttlingEvent = JobServerQueueThrottling;
            if (throttlingEvent != null)
            {
                throttlingEvent(this, new ThrottlingEventArgs(delay, expiration));
            }
        }

        private async Task ProcessWebConsoleLinesQueueAsync(bool runOnce = false)
        {
            while (!_jobCompletionSource.Task.IsCompleted || runOnce)
            {
                bool shouldDrain = ForceDrainWebConsoleQueue;
                if (ForceDrainWebConsoleQueue)
                {
                    ForceDrainWebConsoleQueue = false;
                }

                if (_webConsoleLineAggressiveDequeue && ++_webConsoleLineAggressiveDequeueCount > _webConsoleLineAggressiveDequeueLimit)
                {
                    Trace.Info("Stop aggressive process web console line queue.");
                    _webConsoleLineAggressiveDequeue = false;
                }

                // Group consolelines by timeline record of each step
                Dictionary<Guid, List<TimelineRecordLogLine>> stepsConsoleLines = new Dictionary<Guid, List<TimelineRecordLogLine>>();
                List<Guid> stepRecordIds = new List<Guid>(); // We need to keep lines in order
                int linesCounter = 0;
                ConsoleLineInfo lineInfo;
                while (_webConsoleLineQueue.TryDequeue(out lineInfo))
                {
                    if (!stepsConsoleLines.ContainsKey(lineInfo.StepRecordId))
                    {
                        stepsConsoleLines[lineInfo.StepRecordId] = new List<TimelineRecordLogLine>();
                        stepRecordIds.Add(lineInfo.StepRecordId);
                    }

                    if (lineInfo.Line?.Length > 1024)
                    {
                        Trace.Verbose("Web console line is more than 1024 chars, truncate to first 1024 chars");
                        lineInfo.Line = $"{lineInfo.Line.Substring(0, 1024)}...";
                    }

                    stepsConsoleLines[lineInfo.StepRecordId].Add(new TimelineRecordLogLine(lineInfo.Line, lineInfo.LineNumber));
                    linesCounter++;

                    // process at most about 500 lines of web console line during regular timer dequeue task.
                    // Send the first line of output to the customer right away
                    // It might take a while to reach 500 line outputs, which would cause delays before customers see the first line
                    if ((!runOnce && !shouldDrain && linesCounter > 500) || _firstConsoleOutputs)
                    {
                        break;
                    }
                }

                // Batch post consolelines for each step timeline record
                foreach (var stepRecordId in stepRecordIds)
                {
                    // Split consolelines into batch, each batch will container at most 100 lines.
                    int batchCounter = 0;
                    List<List<TimelineRecordLogLine>> batchedLines = new List<List<TimelineRecordLogLine>>();
                    foreach (var line in stepsConsoleLines[stepRecordId])
                    {
                        var currentBatch = batchedLines.ElementAtOrDefault(batchCounter);
                        if (currentBatch == null)
                        {
                            batchedLines.Add(new List<TimelineRecordLogLine>());
                            currentBatch = batchedLines.ElementAt(batchCounter);
                        }

                        currentBatch.Add(line);

                        if (currentBatch.Count >= 100)
                        {
                            batchCounter++;
                        }
                    }

                    if (batchedLines.Count > 0)
                    {
                        // When job finish, web console lines becomes less interesting to customer
                        // We batch and produce 500 lines of web console output every 500ms
                        // If customer's task produce massive of outputs, then the last queue drain run might take forever.
                        // So we will only upload the last 200 lines of each step from all buffered web console lines.
                        if ((runOnce || shouldDrain) && batchedLines.Count > 2)
                        {
                            Trace.Info($"Skip {batchedLines.Count - 2} batches web console lines for last run");
                            batchedLines = batchedLines.TakeLast(2).ToList();
                        }

                        int errorCount = 0;
                        foreach (var batch in batchedLines)
                        {
                            try
                            {
                                // we will not requeue failed batch, since the web console lines are time sensitive.
                                await _jobServer.AppendTimelineRecordFeedAsync(_scopeIdentifier, _hubName, _planId, _jobTimelineId, _jobTimelineRecordId, stepRecordId, batch.Select(x => x.Line).ToList(), batch[0].LineNumber, default(CancellationToken));
                                if (_firstConsoleOutputs)
                                {
                                    _firstConsoleOutputs = false;
                                    HostContext.WritePerfCounter("WorkerJobServerQueueAppendFirstConsoleOutput");
                                }
                            }
                            catch (Exception ex)
                            {
                                Trace.Info("Catch exception during append web console line, keep going since the process is best effort.");
                                Trace.Error(ex);
                                errorCount++;
                            }
                        }

                        Trace.Info(StringUtil.Format("Try to append {0} batches web console lines for record '{1}', success rate: {2}/{3}.", batchedLines.Count, stepRecordId, batchedLines.Count - errorCount, batchedLines.Count));
                    }
                }

                if (runOnce)
                {
                    break;
                }
                else
                {
                    _webConsoleLinesDequeueNow = new TaskCompletionSource<object>();
                    await Task.WhenAny(
                        Task.Delay(_webConsoleLineAggressiveDequeue ? _aggressiveDelayForWebConsoleLineDequeue : TimeSpan.FromMilliseconds(_webConsoleLineUpdateRate)),
                        _webConsoleLinesDequeueNow.Task);
                }
            }
        }

        public void UpdateWebConsoleLineRate(Int32 rateInMillis)
        {
            _webConsoleLineUpdateRate = rateInMillis;
            // Start running the dequeue task immediately
            _webConsoleLinesDequeueNow?.SetResult(true);
        }

        private async Task ProcessFilesUploadQueueAsync(bool runOnce = false)
        {
            while (!_jobCompletionSource.Task.IsCompleted || runOnce)
            {
                List<UploadFileInfo> filesToUpload = new List<UploadFileInfo>();
                UploadFileInfo dequeueFile;
                while (_fileUploadQueue.TryDequeue(out dequeueFile))
                {
                    filesToUpload.Add(dequeueFile);
                    // process at most 10 file upload.
                    if (!runOnce && filesToUpload.Count > 10)
                    {
                        break;
                    }
                }

                if (filesToUpload.Count > 0)
                {
                    if (runOnce)
                    {
                        Trace.Info($"Uploading {filesToUpload.Count} files in one shot.");
                    }

                    // TODO: upload all file in parallel
                    int errorCount = 0;
                    foreach (var file in filesToUpload)
                    {
                        try
                        {
                            await UploadFile(file);
                        }
                        catch (Exception ex)
                        {
                            Trace.Info("Catch exception during log or attachment file upload, keep going since the process is best effort.");
                            Trace.Error(ex);
                            errorCount++;

                            // put the failed upload file back to queue.
                            // TODO: figure out how should we retry paging log upload.
                            //lock (_fileUploadQueueLock)
                            //{
                            //    _fileUploadQueue.Enqueue(file);
                            //}
                        }
                    }

                    Trace.Info(StringUtil.Format("Try to upload {0} log files or attachments, success rate: {1}/{2}.", filesToUpload.Count, filesToUpload.Count - errorCount, filesToUpload.Count));
                }

                if (runOnce)
                {
                    break;
                }
                else
                {
                    await Task.Delay(_delayForFileUploadDequeue);
                }
            }
        }

        private async Task ProcessTimelinesUpdateQueueAsync(bool runOnce = false)
        {
            while (!_jobCompletionSource.Task.IsCompleted || runOnce)
            {
                bool shouldDrain = ForceDrainTimelineQueue;

                var pendingUpdates = new List<PendingTimelineRecord>();
                foreach (var timeline in _allTimelines)
                {
                    if (_timelineUpdateQueue.TryGetValue(timeline, out ConcurrentQueue<TimelineRecord> recordQueue))
                    {
                        var records = new List<TimelineRecord>();
                        while (recordQueue.TryDequeue(out TimelineRecord record))
                        {
                            records.Add(record);
                            // process at most 25 timeline records update for each timeline.
                            if (!runOnce && !shouldDrain && records.Count > 25)
                            {
                                break;
                            }
                        }

                        if (records.Count > 0)
                        {
                            pendingUpdates.Add(new PendingTimelineRecord() { TimelineId = timeline, PendingRecords = records.ToList() });
                        }
                    }
                }

                // we need track whether we have new sub-timeline been created on the last run.
                // if so, we need continue update timeline record even we on the last run.
                bool pendingSubtimelineUpdate = false;
                List<Exception> mainTimelineRecordsUpdateErrors = new List<Exception>();
                if (pendingUpdates.Count > 0)
                {
                    foreach (var update in pendingUpdates)
                    {
                        if (_bufferedRetryRecords.TryGetValue(update.TimelineId, out List<TimelineRecord> bufferedRecords))
                        {
                            update.PendingRecords.InsertRange(0, bufferedRecords);
                        }

                        update.PendingRecords = MergeTimelineRecords(update.PendingRecords);

                        foreach (var detailTimeline in update.PendingRecords.Where(r => r.Details != null))
                        {
                            if (!_allTimelines.Contains(detailTimeline.Details.Id))
                            {
                                try
                                {
                                    Timeline newTimeline = await _jobServer.CreateTimelineAsync(_scopeIdentifier, _hubName, _planId, detailTimeline.Details.Id, CancellationToken.None);
                                    _allTimelines.Add(newTimeline.Id);
                                    pendingSubtimelineUpdate = true;
                                }
                                catch (TimelineExistsException)
                                {
                                    Trace.Info("Catch TimelineExistsException during timeline creation. Ignore the error since server already had this timeline.");
                                    _allTimelines.Add(detailTimeline.Details.Id);
                                }
                                catch (Exception ex)
                                {
                                    Trace.Error(ex);
                                }
                            }
                        }

                        try
                        {
                            await _jobServer.UpdateTimelineRecordsAsync(_scopeIdentifier, _hubName, _planId, update.TimelineId, update.PendingRecords, CancellationToken.None);
                            if (_bufferedRetryRecords.Remove(update.TimelineId))
                            {
                                Trace.Verbose(StringUtil.Format("Cleanup buffered timeline record for timeline: {0}.", update.TimelineId));
                            }
                        }
                        catch (Exception ex)
                        {
                            Trace.Info("Catch exception during update timeline records, try to update these timeline records next time.");
                            Trace.Error(ex);
                            _bufferedRetryRecords[update.TimelineId] = update.PendingRecords;
                            if (update.TimelineId == _jobTimelineId)
                            {
                                mainTimelineRecordsUpdateErrors.Add(ex);
                            }
                        }
                    }
                }

                if (runOnce || shouldDrain)
                {
                    // continue process timeline records update,
                    // we might have more records need update,
                    // since we just create a new sub-timeline
                    if (pendingSubtimelineUpdate)
                    {
                        continue;
                    }
                    else
                    {
                        if (ForceDrainTimelineQueue)
                        {
                            ForceDrainTimelineQueue = false;
                        }
                    }
                }

                if (runOnce)
                {
                    if (mainTimelineRecordsUpdateErrors.Count > 0 &&
                            _bufferedRetryRecords.ContainsKey(_jobTimelineId) &&
                            _bufferedRetryRecords[_jobTimelineId] != null &&
                            _bufferedRetryRecords[_jobTimelineId].Any(r => r.Variables.Count > 0))
                    {
                        Trace.Info("Fail to update timeline records with output variables. Throw exception to fail the job since output variables are critical to downstream jobs.");
                        throw new AggregateException(StringUtil.Loc("OutputVariablePublishFailed"), mainTimelineRecordsUpdateErrors);
                    }

                    break;
                }

                await Task.Delay(_delayForTimelineUpdateDequeue);
            }
        }

        private List<TimelineRecord> MergeTimelineRecords(List<TimelineRecord> timelineRecords)
        {
            if (timelineRecords == null || timelineRecords.Count <= 1)
            {
                return timelineRecords;
            }

            Dictionary<Guid, TimelineRecord> dict = new Dictionary<Guid, TimelineRecord>();
            foreach (TimelineRecord rec in timelineRecords)
            {
                if (rec == null)
                {
                    continue;
                }

                TimelineRecord timelineRecord;
                if (dict.TryGetValue(rec.Id, out timelineRecord))
                {
                    // Merge rec into timelineRecord
                    timelineRecord.CurrentOperation = rec.CurrentOperation ?? timelineRecord.CurrentOperation;
                    timelineRecord.Details = rec.Details ?? timelineRecord.Details;
                    timelineRecord.FinishTime = rec.FinishTime ?? timelineRecord.FinishTime;
                    timelineRecord.Log = rec.Log ?? timelineRecord.Log;
                    timelineRecord.Name = rec.Name ?? timelineRecord.Name;
                    timelineRecord.RefName = rec.RefName ?? timelineRecord.RefName;
                    timelineRecord.PercentComplete = rec.PercentComplete ?? timelineRecord.PercentComplete;
                    timelineRecord.RecordType = rec.RecordType ?? timelineRecord.RecordType;
                    timelineRecord.Result = rec.Result ?? timelineRecord.Result;
                    timelineRecord.ResultCode = rec.ResultCode ?? timelineRecord.ResultCode;
                    timelineRecord.StartTime = rec.StartTime ?? timelineRecord.StartTime;
                    timelineRecord.State = rec.State ?? timelineRecord.State;
                    timelineRecord.WorkerName = rec.WorkerName ?? timelineRecord.WorkerName;

                    if (rec.ErrorCount != null && rec.ErrorCount > 0)
                    {
                        timelineRecord.ErrorCount = rec.ErrorCount;
                    }

                    if (rec.WarningCount != null && rec.WarningCount > 0)
                    {
                        timelineRecord.WarningCount = rec.WarningCount;
                    }

                    if (rec.Issues.Count > 0)
                    {
                        timelineRecord.Issues.Clear();
                        timelineRecord.Issues.AddRange(rec.Issues.Select(i => i.Clone()));
                    }

                    if (rec.Variables.Count > 0)
                    {
                        foreach (var variable in rec.Variables)
                        {
                            timelineRecord.Variables[variable.Key] = variable.Value.Clone();
                        }
                    }
                }
                else
                {
                    dict.Add(rec.Id, rec);
                }
            }

            var mergedRecords = dict.Values.ToList();

            Trace.Verbose("Merged Timeline records");
            foreach (var record in mergedRecords)
            {
                Trace.Verbose($"    Record: t={record.RecordType}, n={record.Name}, s={record.State}, st={record.StartTime}, {record.PercentComplete}%, ft={record.FinishTime}, r={record.Result}: {record.CurrentOperation}");
                if (record.Issues != null && record.Issues.Count > 0)
                {
                    foreach (var issue in record.Issues)
                    {
                        String source;
                        issue.Data.TryGetValue("sourcepath", out source);
                        Trace.Verbose($"        Issue: c={issue.Category}, t={issue.Type}, s={source ?? string.Empty}, m={issue.Message}");
                    }
                }

                if (record.Variables != null && record.Variables.Count > 0)
                {
                    foreach (var variable in record.Variables)
                    {
                        Trace.Verbose($"        Variable: n={variable.Key}, secret={variable.Value.IsSecret}");
                    }
                }
            }

            return mergedRecords;
        }

        private async Task UploadFile(UploadFileInfo file)
        {
            bool uploadSucceed = false;
            try
            {
                if (String.Equals(file.Type, CoreAttachmentType.Log, StringComparison.OrdinalIgnoreCase))
                {
                    // Create the log
                    var taskLog = await _jobServer.CreateLogAsync(_scopeIdentifier, _hubName, _planId, new TaskLog(String.Format(@"logs\{0:D}", file.TimelineRecordId)), default(CancellationToken));

                    using (FileStream fs = File.Open(file.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        if (_writeToBlobStoreLogs)
                        {
                            try
                            {
                                var blobBlockId = await _jobServer.UploadLogToBlobStore(fs, _hubName, _planId, taskLog.Id);
                                int lineCount = File.ReadLines(file.Path).Count();

                                // Notify TFS
                                await _jobServer.AssociateLogAsync(_scopeIdentifier, _hubName, _planId, taskLog.Id, blobBlockId, lineCount, default(CancellationToken));
                            }
                            catch
                            {
                                // Fall back to FCS
                                fs.Position = 0;
                                await _jobServer.AppendLogContentAsync(_scopeIdentifier, _hubName, _planId, taskLog.Id, fs, default(CancellationToken));
                            }
                        }
                        else
                        {
                            await _jobServer.AppendLogContentAsync(_scopeIdentifier, _hubName, _planId, taskLog.Id, fs, default(CancellationToken));
                        }
                    }

                    // Create a new record and only set the Log field
                    var attachmentUpdataRecord = new TimelineRecord() { Id = file.TimelineRecordId, Log = taskLog };
                    QueueTimelineRecordUpdate(file.TimelineId, attachmentUpdataRecord);
                }
                else
                {
                    if (_writeToBlobStoreAttachments)
                    {
                        try
                        {
                            var (dedupId, length) = await _jobServer.UploadAttachmentToBlobStore(_debugMode, file.Path, _planId, _jobTimelineRecordId, default(CancellationToken));
                            // Notify TFS
                            await _jobServer.AssosciateAttachmentAsync(_scopeIdentifier, _hubName, _planId, file.TimelineId, file.TimelineRecordId, file.Type, file.Name, dedupId, (long)length, default(CancellationToken));
                        }
                        catch
                        {
                            // Fall back to file-based FCS
                            using (FileStream fs = File.Open(file.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            {
                                var result = await _jobServer.CreateAttachmentAsync(_scopeIdentifier, _hubName, _planId, file.TimelineId, file.TimelineRecordId, file.Type, file.Name, fs, default(CancellationToken));
                            }
                        }
                    }
                    else
                    {
                        // Create attachment
                        using (FileStream fs = File.Open(file.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            var result = await _jobServer.CreateAttachmentAsync(_scopeIdentifier, _hubName, _planId, file.TimelineId, file.TimelineRecordId, file.Type, file.Name, fs, default(CancellationToken));
                        }
                    }
                }

                uploadSucceed = true;
            }
            finally
            {
                if (uploadSucceed && file.DeleteSource)
                {
                    try
                    {
                        File.Delete(file.Path);
                    }
                    catch (Exception ex)
                    {
                        Trace.Info("Catch exception during delete success uploaded file.");
                        Trace.Error(ex);
                    }
                }
            }
        }
    }

    internal class PendingTimelineRecord
    {
        public Guid TimelineId { get; set; }
        public List<TimelineRecord> PendingRecords { get; set; }
    }

    internal class UploadFileInfo
    {
        public Guid TimelineId { get; set; }
        public Guid TimelineRecordId { get; set; }
        public string Type { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public bool DeleteSource { get; set; }
    }


    internal class ConsoleLineInfo
    {
        public ConsoleLineInfo(Guid recordId, string line, long lineNumber)
        {
            this.StepRecordId = recordId;
            this.Line = line;
            this.LineNumber = lineNumber;
        }

        public Guid StepRecordId { get; set; }
        public string Line { get; set; }
        public long LineNumber { get; set; }
    }
}
