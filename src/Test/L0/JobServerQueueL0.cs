// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Moq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;

namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    public sealed class JobServerQueueL0
    {
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public async Task FinalTimelineUpdateIsSentOnceWhenItSucceeds()
        {
            using (TestHostContext hc = new TestHostContext(this))
            {
                var jobServer = new FakeTimelineJobServer();
                var queue = StartQueue(hc, jobServer, out Pipelines.AgentJobRequestMessage message);
                TimelineRecord jobRecord = await PublishJobRecordAsync(queue, jobServer, message);

                CompleteJob(hc, queue, message, jobRecord, stepsWithPendingLogs: 0);

                await queue.ShutdownAsync();

                TimelineUpdateCall finalUpdate = Assert.Single(jobServer.FinalTimelineUpdates());
                Assert.True(finalUpdate.Succeeded);
                Assert.Empty(hc.CapturedDelays);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public async Task FinalTimelineUpdateIsRetriedUntilItSucceeds()
        {
            using (TestHostContext hc = new TestHostContext(this))
            {
                var jobServer = new FakeTimelineJobServer();
                var queue = StartQueue(hc, jobServer, out Pipelines.AgentJobRequestMessage message);
                TimelineRecord jobRecord = await PublishJobRecordAsync(queue, jobServer, message);

                jobServer.FailFinalTimelineUpdates(1);
                List<Guid> stepIds = CompleteJob(hc, queue, message, jobRecord, stepsWithPendingLogs: 2);

                await queue.ShutdownAsync();

                var finalUpdates = jobServer.FinalTimelineUpdates();
                Assert.Equal(new[] { false, true }, finalUpdates.Select(u => u.Succeeded));
                Assert.Equal(new[] { RetryDelay }, hc.CapturedDelays);
                // The retry resends the records of the failed update, including every log link, and the job ends up completed on the server.
                Assert.Equal(finalUpdates[0].Records.Select(r => r.Id).OrderBy(id => id), finalUpdates[1].Records.Select(r => r.Id).OrderBy(id => id));
                Assert.All(stepIds.Append(message.JobId), id => Assert.Contains(finalUpdates[1].Records, r => r.Id == id && r.Log != null));
                Assert.Contains(jobServer.SucceededTimelineUpdates().SelectMany(u => u.Records), r => r.Id == message.JobId && r.State == TimelineRecordState.Completed);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public async Task FinalTimelineUpdateFailsShutdownAfterFiveTries()
        {
            using (TestHostContext hc = new TestHostContext(this))
            {
                var jobServer = new FakeTimelineJobServer();
                var queue = StartQueue(hc, jobServer, out Pipelines.AgentJobRequestMessage message);
                TimelineRecord jobRecord = await PublishJobRecordAsync(queue, jobServer, message);

                jobServer.FailFinalTimelineUpdates(int.MaxValue);
                CompleteJob(hc, queue, message, jobRecord, stepsWithPendingLogs: 2);

                var ex = await Assert.ThrowsAsync<AggregateException>(() => queue.ShutdownAsync());

                Assert.StartsWith(StringUtil.Loc("OutputVariablePublishFailed"), ex.Message);
                var finalUpdates = jobServer.FinalTimelineUpdates();
                Assert.Equal(5, finalUpdates.Count);
                Assert.All(finalUpdates, u => Assert.False(u.Succeeded));
                Assert.Equal(Enumerable.Repeat(RetryDelay, 4), hc.CapturedDelays);
                Assert.Equal(5, ex.InnerExceptions.Count);
            }
        }

        [Theory]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        [InlineData(nameof(TaskOrchestrationPlanNotFoundException))]
        [InlineData(nameof(TaskOrchestrationPlanSecurityException))]
        public async Task FinalTimelineUpdateIsNotRetriedWhenThePlanIsNotAccessible(string exceptionName)
        {
            using (TestHostContext hc = new TestHostContext(this))
            {
                var jobServer = new FakeTimelineJobServer();
                var queue = StartQueue(hc, jobServer, out Pipelines.AgentJobRequestMessage message);
                TimelineRecord jobRecord = await PublishJobRecordAsync(queue, jobServer, message);

                Exception planError = exceptionName == nameof(TaskOrchestrationPlanNotFoundException)
                    ? new TaskOrchestrationPlanNotFoundException("No plan found.")
                    : new TaskOrchestrationPlanSecurityException("Access denied.");
                jobServer.TimelineUpdateError = () => planError;
                jobServer.FailFinalTimelineUpdates(int.MaxValue);
                CompleteJob(hc, queue, message, jobRecord, stepsWithPendingLogs: 0);

                var ex = await Assert.ThrowsAsync<AggregateException>(() => queue.ShutdownAsync());

                Assert.StartsWith(StringUtil.Loc("OutputVariablePublishFailed"), ex.Message);
                Assert.Single(jobServer.FinalTimelineUpdates());
                Assert.Empty(hc.CapturedDelays);
                Assert.Same(planError, Assert.Single(ex.InnerExceptions));
            }
        }

        private static JobServerQueue StartQueue(TestHostContext hc, FakeTimelineJobServer jobServer, out Pipelines.AgentJobRequestMessage message)
        {
            hc.SetSingleton<IJobServer>(jobServer.Object);
            message = CreateJobRequestMessage();

            var queue = new JobServerQueue();
            queue.Initialize(hc);
            queue.Start(message);
            return queue;
        }

        // The job record is published at job start like ExecutionContext does, carrying the built-in agent.version variable.
        private static async Task<TimelineRecord> PublishJobRecordAsync(JobServerQueue queue, FakeTimelineJobServer jobServer, Pipelines.AgentJobRequestMessage message)
        {
            TimelineRecord jobRecord = CreateRecord(message.JobId, parentId: null, "Job", message.JobDisplayName);
            jobRecord.State = TimelineRecordState.InProgress;
            queue.QueueTimelineRecordUpdate(message.Timeline.Id, jobRecord);
            await jobServer.WaitForSucceededTimelineUpdateAsync(r => r.Id == message.JobId);
            return jobRecord;
        }

        // Mirrors the end of a job: the last steps complete with their logs still to upload, then ExecutionContext.Complete()
        // queues the completed job record and the last job log page right before ShutdownAsync(). Returns the step record ids.
        private static List<Guid> CompleteJob(TestHostContext hc, JobServerQueue queue, Pipelines.AgentJobRequestMessage message, TimelineRecord jobRecord, int stepsWithPendingLogs)
        {
            var stepIds = new List<Guid>();
            for (int i = 1; i <= stepsWithPendingLogs; i++)
            {
                TimelineRecord stepRecord = CreateRecord(Guid.NewGuid(), message.JobId, "Task", $"Step {i}");
                stepRecord.State = TimelineRecordState.Completed;
                stepRecord.Result = TaskResult.Succeeded;
                queue.QueueTimelineRecordUpdate(message.Timeline.Id, stepRecord);
                QueueLogUpload(hc, queue, message, stepRecord.Id, $"step {i} log");
                stepIds.Add(stepRecord.Id);
            }

            jobRecord.State = TimelineRecordState.Completed;
            jobRecord.Result = TaskResult.Succeeded;
            jobRecord.FinishTime = DateTime.UtcNow;
            queue.QueueTimelineRecordUpdate(message.Timeline.Id, jobRecord);
            QueueLogUpload(hc, queue, message, message.JobId, "Finishing: job");
            return stepIds;
        }

        private static void QueueLogUpload(TestHostContext hc, JobServerQueue queue, Pipelines.AgentJobRequestMessage message, Guid recordId, string content)
        {
            string logFile = Path.Combine(hc.GetDirectory(WellKnownDirectory.Work), $"{Guid.NewGuid()}.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logFile));
            File.WriteAllText(logFile, content);
            queue.QueueFileUpload(message.Timeline.Id, recordId, "DistributedTask.Core.Log", "CustomToolLog", logFile, deleteSource: true);
        }

        private static TimelineRecord CreateRecord(Guid id, Guid? parentId, string recordType, string name)
        {
            var record = new TimelineRecord()
            {
                Id = id,
                ParentId = parentId,
                RecordType = recordType,
                Name = name,
                State = TimelineRecordState.Pending,
            };
            record.Variables.Add(TaskWellKnownItems.AgentVersionTimelineVariable, "4.999.999");
            return record;
        }

        private static Pipelines.AgentJobRequestMessage CreateJobRequestMessage()
        {
            TaskOrchestrationPlanReference plan = new TaskOrchestrationPlanReference() { PlanId = Guid.NewGuid(), PlanType = "Build", ScopeIdentifier = Guid.NewGuid(), Version = 8 };
            TimelineReference timeline = new Timeline(Guid.NewGuid());
            JobEnvironment environment = new JobEnvironment();
            environment.SystemConnection = new ServiceEndpoint()
            {
                Name = WellKnownServiceEndpointNames.SystemVssConnection,
                Url = new Uri("https://test.visualstudio.com"),
                Authorization = new EndpointAuthorization() { Scheme = "Test" }
            };
            environment.SystemConnection.Authorization.Parameters["AccessToken"] = "token";

            return Pipelines.AgentJobRequestMessageUtil.Convert(new AgentJobRequestMessage(plan, timeline, Guid.NewGuid(), "job", "job", environment, new List<TaskInstance>()));
        }
    }

    internal sealed class TimelineUpdateCall
    {
        public Guid TimelineId { get; set; }
        public List<TimelineRecord> Records { get; set; }
        public bool Succeeded { get; set; }
    }

    // IJobServer fake that records timeline updates and fails the ones selected by ShouldFailTimelineUpdate.
    internal sealed class FakeTimelineJobServer
    {
        private readonly Mock<IJobServer> _mock = new Mock<IJobServer>();
        private readonly ConcurrentQueue<TimelineUpdateCall> _timelineUpdates = new ConcurrentQueue<TimelineUpdateCall>();
        private int _nextLogId;

        public FakeTimelineJobServer()
        {
            _mock.Setup(x => x.UpdateTimelineRecordsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IEnumerable<TimelineRecord>>(), It.IsAny<CancellationToken>()))
                .Returns((Guid scopeIdentifier, string hubName, Guid planId, Guid timelineId, IEnumerable<TimelineRecord> records, CancellationToken token) =>
                {
                    List<TimelineRecord> snapshot = records.Select(r => r.Clone()).ToList();
                    bool fail = ShouldFailTimelineUpdate(snapshot);
                    _timelineUpdates.Enqueue(new TimelineUpdateCall() { TimelineId = timelineId, Records = snapshot, Succeeded = !fail });
                    if (fail)
                    {
                        return Task.FromException<List<TimelineRecord>>(TimelineUpdateError());
                    }

                    return Task.FromResult(snapshot);
                });

            _mock.Setup(x => x.CreateLogAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<TaskLog>(), It.IsAny<CancellationToken>()))
                .Returns((Guid scopeIdentifier, string hubName, Guid planId, TaskLog log, CancellationToken token) =>
                {
                    log.Id = Interlocked.Increment(ref _nextLogId);
                    return Task.FromResult(log);
                });

            _mock.Setup(x => x.AppendLogContentAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .Returns((Guid scopeIdentifier, string hubName, Guid planId, int logId, Stream stream, CancellationToken token) =>
                    Task.FromResult(new TaskLog($"logs\\{logId}") { Id = logId }));
        }

        public IJobServer Object => _mock.Object;

        public Mock<IJobServer> Mock => _mock;

        public Func<IReadOnlyList<TimelineRecord>, bool> ShouldFailTimelineUpdate { get; set; } = _ => false;

        // What a failed timeline update throws; by default the error seen when the connection breaks during the request.
        public Func<Exception> TimelineUpdateError { get; set; } = () =>
            new HttpRequestException("An error occurred while sending the request.", new IOException("The response ended prematurely."));

        // Fails the first `count` final timeline updates; later ones succeed.
        public void FailFinalTimelineUpdates(int count)
        {
            int failed = 0;
            ShouldFailTimelineUpdate = records => records.Any(r => r.Log != null) && Interlocked.Increment(ref failed) <= count;
        }

        public List<TimelineUpdateCall> TimelineUpdates() => _timelineUpdates.ToList();

        public List<TimelineUpdateCall> SucceededTimelineUpdates() => TimelineUpdates().Where(u => u.Succeeded).ToList();

        // Updates that carry a log link; logs are only uploaded at job end in these tests, so these are the final timeline updates.
        public List<TimelineUpdateCall> FinalTimelineUpdates() => TimelineUpdates().Where(u => u.Records.Any(r => r.Log != null)).ToList();

        public async Task WaitForSucceededTimelineUpdateAsync(Func<TimelineRecord, bool> predicate)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while (!SucceededTimelineUpdates().SelectMany(u => u.Records).Any(predicate))
            {
                Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the timeline update to be sent.");
                await Task.Delay(20);
            }
        }
    }
}
