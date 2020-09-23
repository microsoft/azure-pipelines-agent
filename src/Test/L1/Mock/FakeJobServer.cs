// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using System;
using Microsoft.VisualStudio.Services.WebApi;
using System.Linq;
using Microsoft.VisualStudio.Services.Common;

namespace Microsoft.VisualStudio.Services.Agent.Tests.L1.Worker
{
    public class FakeJobServer : AgentService, IJobServer
    {
        public List<JobEvent> RecordedEvents { get; }
        public Dictionary<int, TaskLog> LogObjects { get; }
        public Dictionary<int, IList<string>> LogLines { get; }
        public Dictionary<Guid, Timeline> Timelines { get; }
        public List<string> AttachmentsCreated { get; }

        public FakeJobServer()
        {
            RecordedEvents = new List<JobEvent>();
            Timelines = new Dictionary<Guid, Timeline>();
            LogObjects = new Dictionary<int, TaskLog>();
            LogLines = new Dictionary<int, IList<string>>();
            AttachmentsCreated = new List<string>();
        }

        public Task ConnectAsync(VssConnection jobConnection)
        {
            return Task.CompletedTask;
        }

        public Task<TaskLog> AppendLogContentAsync(Guid scopeIdentifier, string hubName, Guid planId, int logId, Stream uploadStream, CancellationToken cancellationToken)
        {
            using (var reader = new StreamReader(uploadStream))
            {
                var text = reader.ReadToEnd();
                var addedLines = text.Split("\n");

                var lines = LogLines.GetValueOrDefault(logId);
                lines.AddRange(addedLines);
                return Task.FromResult(LogObjects.GetValueOrDefault(logId));
            }
        }

        public Task AppendTimelineRecordFeedAsync(Guid scopeIdentifier, string hubName, Guid planId, Guid timelineId, Guid timelineRecordId, Guid stepId, IList<string> lines, long startLine, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<TaskAttachment> CreateAttachmentAsync(Guid scopeIdentifier, string hubName, Guid planId, Guid timelineId, Guid timelineRecordId, String type, String name, Stream uploadStream, CancellationToken cancellationToken)
        {
            AttachmentsCreated.Add(name);
            return Task.FromResult(new TaskAttachment(type, name));
        }

        public Task<TaskLog> CreateLogAsync(Guid scopeIdentifier, string hubName, Guid planId, TaskLog log, CancellationToken cancellationToken)
        {
            log.Id = LogObjects.Count + 1;
            LogObjects.Add(log.Id, log);
            LogLines.Add(log.Id, new List<string>());
            return Task.FromResult(log);
        }

        public Task<Timeline> CreateTimelineAsync(Guid scopeIdentifier, string hubName, Guid planId, Guid timelineId, CancellationToken cancellationToken)
        {
            var timeline = new Timeline {
                Id = timelineId
            };
            Timelines.Add(timelineId, timeline);
            return Task.FromResult(timeline);
        }

        public Task<List<TimelineRecord>> UpdateTimelineRecordsAsync(Guid scopeIdentifier, string hubName, Guid planId, Guid timelineId, IEnumerable<TimelineRecord> records, CancellationToken cancellationToken)
        {
            var recordDictionary = records.ToDictionary(x => x.Id);
            Timeline timeline = Timelines.GetValueOrDefault(timelineId);
            foreach (var record in timeline.Records)
            {
                if (recordDictionary.ContainsKey(record.Id))
                {
                    MergeTimelineRecords(record, recordDictionary.GetValueOrDefault(record.Id));
                    recordDictionary.Remove(record.Id);
                }
            }
            timeline.Records.AddRange(recordDictionary.Values);
            return Task.FromResult(records.ToList());
        }

        public Task RaisePlanEventAsync<T>(Guid scopeIdentifier, string hubName, Guid planId, T eventData, CancellationToken cancellationToken) where T : JobEvent
        {
            RecordedEvents.Add(eventData);
            return Task.CompletedTask;
        }

        public Task<Timeline> GetTimelineAsync(Guid scopeIdentifier, string hubName, Guid planId, Guid timelineId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Timelines[timelineId]);
        }

        private void MergeTimelineRecords(TimelineRecord timelineRecord, TimelineRecord rec)
        {
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
    }
}