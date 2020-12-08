// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.DistributedTask.WebApi;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using Microsoft.VisualStudio.Services.BlobStore.WebApi;
using Microsoft.VisualStudio.Services.Content.Common;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;

namespace Microsoft.VisualStudio.Services.Agent
{
    [ServiceLocator(Default = typeof(JobServer))]
    public interface IJobServer : IAgentService
    {
        Task ConnectAsync(VssConnection jobConnection);

        // logging and console
        Task<TaskLog> AppendLogContentAsync(Guid scopeIdentifier, string hubName, Guid planId, int logId, Stream uploadStream, CancellationToken cancellationToken);
        Task AppendTimelineRecordFeedAsync(Guid scopeIdentifier, string hubName, Guid planId, Guid timelineId, Guid timelineRecordId, Guid stepId, IList<string> lines, long startLine, CancellationToken cancellationToken);
        Task<TaskAttachment> CreateAttachmentAsync(Guid scopeIdentifier, string hubName, Guid planId, Guid timelineId, Guid timelineRecordId, String type, String name, Stream uploadStream, CancellationToken cancellationToken);
        Task<TaskLog> CreateLogAsync(Guid scopeIdentifier, string hubName, Guid planId, TaskLog log, CancellationToken cancellationToken);
        Task<Timeline> CreateTimelineAsync(Guid scopeIdentifier, string hubName, Guid planId, Guid timelineId, CancellationToken cancellationToken);
        Task<List<TimelineRecord>> UpdateTimelineRecordsAsync(Guid scopeIdentifier, string hubName, Guid planId, Guid timelineId, IEnumerable<TimelineRecord> records, CancellationToken cancellationToken);
        Task RaisePlanEventAsync<T>(Guid scopeIdentifier, string hubName, Guid planId, T eventData, CancellationToken cancellationToken) where T : JobEvent;
        Task<Timeline> GetTimelineAsync(Guid scopeIdentifier, string hubName, Guid planId, Guid timelineId, CancellationToken cancellationToken);
        Task<PublishResult> UploadLogToBlobstorageService(string sourceFilePath, CancellationToken cancellationToken);
        Task DownloadAsync(DedupIdentifier manifestId, string targetDirectory, CancellationToken cancellationToken);
    }

    public sealed class JobServer : AgentService, IJobServer
    {
        private bool _hasConnection;
        private VssConnection _connection;
        private TaskHttpClient _taskClient;
        private const int DefaultDedupStoreClientMaxParallelism = 192;

        public async Task ConnectAsync(VssConnection jobConnection)
        {
            ArgUtil.NotNull(jobConnection, nameof(jobConnection));
            _connection = jobConnection;
            int attemptCount = 5;
            while (!_connection.HasAuthenticated && attemptCount-- > 0)
            {
                try
                {
                    await _connection.ConnectAsync();
                    break;
                }
                catch (Exception ex) when (attemptCount > 0)
                {
                    Trace.Info($"Catch exception during connect. {attemptCount} attemp left.");
                    Trace.Error(ex);
                }

                await Task.Delay(100);
            }

            _taskClient = _connection.GetClient<TaskHttpClient>();
            _hasConnection = true;
        }

        private void CheckConnection()
        {
            if (!_hasConnection)
            {
                throw new InvalidOperationException("SetConnection");
            }
        }

        //-----------------------------------------------------------------
        // Feedback: WebConsole, TimelineRecords and Logs
        //-----------------------------------------------------------------

        public Task<TaskLog> AppendLogContentAsync(Guid scopeIdentifier, string hubName, Guid planId, int logId, Stream uploadStream, CancellationToken cancellationToken)
        {
            CheckConnection();
            return _taskClient.AppendLogContentAsync(scopeIdentifier, hubName, planId, logId, uploadStream, cancellationToken: cancellationToken);
        }

        public Task AppendTimelineRecordFeedAsync(Guid scopeIdentifier, string hubName, Guid planId, Guid timelineId, Guid timelineRecordId, Guid stepId, IList<string> lines, long startLine,  CancellationToken cancellationToken)
        {
            CheckConnection();
            return _taskClient.AppendTimelineRecordFeedAsync(scopeIdentifier, hubName, planId, timelineId, timelineRecordId, stepId, lines, startLine, cancellationToken: cancellationToken);
        }

        public Task<TaskAttachment> CreateAttachmentAsync(Guid scopeIdentifier, string hubName, Guid planId, Guid timelineId, Guid timelineRecordId, string type, string name, Stream uploadStream, CancellationToken cancellationToken)
        {
            CheckConnection();
            return _taskClient.CreateAttachmentAsync(scopeIdentifier, hubName, planId, timelineId, timelineRecordId, type, name, uploadStream, cancellationToken: cancellationToken);
        }

        public Task<TaskLog> CreateLogAsync(Guid scopeIdentifier, string hubName, Guid planId, TaskLog log, CancellationToken cancellationToken)
        {
            CheckConnection();
            return _taskClient.CreateLogAsync(scopeIdentifier, hubName, planId, log, cancellationToken: cancellationToken);
        }

        public Task<Timeline> CreateTimelineAsync(Guid scopeIdentifier, string hubName, Guid planId, Guid timelineId, CancellationToken cancellationToken)
        {
            CheckConnection();
            return _taskClient.CreateTimelineAsync(scopeIdentifier, hubName, planId, new Timeline(timelineId), cancellationToken: cancellationToken);
        }

        public Task<List<TimelineRecord>> UpdateTimelineRecordsAsync(Guid scopeIdentifier, string hubName, Guid planId, Guid timelineId, IEnumerable<TimelineRecord> records, CancellationToken cancellationToken)
        {
            CheckConnection();
            return _taskClient.UpdateTimelineRecordsAsync(scopeIdentifier, hubName, planId, timelineId, records, cancellationToken: cancellationToken);
        }

        public Task RaisePlanEventAsync<T>(Guid scopeIdentifier, string hubName, Guid planId, T eventData, CancellationToken cancellationToken) where T : JobEvent
        {
            CheckConnection();
            return _taskClient.RaisePlanEventAsync(scopeIdentifier, hubName, planId, eventData, cancellationToken: cancellationToken);
        }

        public Task<Timeline> GetTimelineAsync(Guid scopeIdentifier, string hubName, Guid planId, Guid timelineId, CancellationToken cancellationToken)
        {
            CheckConnection();
            return _taskClient.GetTimelineAsync(scopeIdentifier, hubName, planId, timelineId, includeRecords: true, cancellationToken: cancellationToken);
        }

        public Task<PublishResult> UploadLogToBlobstorageService(string sourceFilePath, CancellationToken cancellationToken)
        {
            // TODO - it seems like it would be better to not have to create this client every time
            CheckConnection();
            using(var dedupManifestClient = CreateDedupManifestClient(_connection, cancellationToken))
            {
                return dedupManifestClient.PublishAsync(sourceFilePath, cancellationToken);
            }
        }

        // TODO - remove this function (here and above)
        public Task DownloadAsync(DedupIdentifier manifestId, string targetDirectory, CancellationToken cancellationToken)
        {
            CheckConnection();
            using(var dedupManifestClient = CreateDedupManifestClient(_connection, cancellationToken))
            {
                return dedupManifestClient.DownloadAsync(manifestId, targetDirectory, cancellationToken);
            }
        }

        private DedupManifestArtifactClient CreateDedupManifestClient(VssConnection connection, CancellationToken cancellationToken){
            var tracer = new CallbackAppTraceSource(str => Trace.Info(str), System.Diagnostics.SourceLevels.Information);

            ArtifactHttpClientFactory factory = new ArtifactHttpClientFactory(
            connection.Credentials,
            TimeSpan.FromSeconds(50),
            tracer,
            default(CancellationToken));

            var dedupStoreHttpClient = factory.CreateVssHttpClient<IDedupStoreHttpClient, DedupStoreHttpClient>(connection.GetClient<DedupStoreHttpClient>().BaseAddress);
        
            var client = new DedupStoreClientWithDataport(dedupStoreHttpClient, DefaultDedupStoreClientMaxParallelism);
            return new DedupManifestArtifactClient(client, tracer);
        }
    }
}