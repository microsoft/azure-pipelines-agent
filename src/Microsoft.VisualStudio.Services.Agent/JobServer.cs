// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.DistributedTask.WebApi;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.Agent.Blob;
using Microsoft.VisualStudio.Services.Agent.Util;
using BlobIdentifierWithBlocks = Microsoft.VisualStudio.Services.BlobStore.Common.BlobIdentifierWithBlocks;
using VsoHash = Microsoft.VisualStudio.Services.BlobStore.Common.VsoHash;
using Microsoft.VisualStudio.Services.BlobStore.WebApi;
using Microsoft.VisualStudio.Services.Content.Common;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;
using Agent.Sdk.Util;

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
        Task<TaskAttachment> AssosciateAttachmentAsync(Guid scopeIdentifier, string hubName, Guid planId, Guid timelineId, Guid timelineRecordId, string type, string name, DedupIdentifier dedupId, long length, CancellationToken cancellationToken);
        Task<TaskLog> CreateLogAsync(Guid scopeIdentifier, string hubName, Guid planId, TaskLog log, CancellationToken cancellationToken);
        Task<Timeline> CreateTimelineAsync(Guid scopeIdentifier, string hubName, Guid planId, Guid timelineId, CancellationToken cancellationToken);
        Task<List<TimelineRecord>> UpdateTimelineRecordsAsync(Guid scopeIdentifier, string hubName, Guid planId, Guid timelineId, IEnumerable<TimelineRecord> records, CancellationToken cancellationToken);
        Task RaisePlanEventAsync<T>(Guid scopeIdentifier, string hubName, Guid planId, T eventData, CancellationToken cancellationToken) where T : JobEvent;
        Task<Timeline> GetTimelineAsync(Guid scopeIdentifier, string hubName, Guid planId, Guid timelineId, CancellationToken cancellationToken);
        Task<TaskLog> AssociateLogAsync(Guid scopeIdentifier, string hubName, Guid planId, int logId, BlobIdentifierWithBlocks blobBlockId, int lineCount, CancellationToken cancellationToken);
        Task<BlobIdentifierWithBlocks> UploadLogToBlobStore(Stream blob, string hubName, Guid planId, int logId);
        Task<(DedupIdentifier dedupId, ulong length)> UploadAttachmentToBlobStore(bool verbose, string itemPath, Guid planId, Guid jobId, CancellationToken cancellationToken);
    }

    public sealed class JobServer : AgentService, IJobServer
    {
        private bool _hasConnection;
        private VssConnection _connection;
        private TaskHttpClient _taskClient;

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

            try
            {
                _taskClient = _connection.GetClient<TaskHttpClient>();
            }
            catch (SocketException e)
            {
                ExceptionsUtil.HandleSocketException(e, _connection.Uri.ToString(), Trace.Error);
                throw;
            }

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

        public Task AppendTimelineRecordFeedAsync(Guid scopeIdentifier, string hubName, Guid planId, Guid timelineId, Guid timelineRecordId, Guid stepId, IList<string> lines, long startLine, CancellationToken cancellationToken)
        {
            CheckConnection();
            return _taskClient.AppendTimelineRecordFeedAsync(scopeIdentifier, hubName, planId, timelineId, timelineRecordId, stepId, lines, startLine, cancellationToken: cancellationToken);
        }

        public Task<TaskAttachment> CreateAttachmentAsync(Guid scopeIdentifier, string hubName, Guid planId, Guid timelineId, Guid timelineRecordId, string type, string name, Stream uploadStream, CancellationToken cancellationToken)
        {
            CheckConnection();
            return _taskClient.CreateAttachmentAsync(scopeIdentifier, hubName, planId, timelineId, timelineRecordId, type, name, uploadStream, cancellationToken: cancellationToken);
        }

        public Task<TaskAttachment> AssosciateAttachmentAsync(Guid scopeIdentifier, string hubName, Guid planId, Guid timelineId, Guid timelineRecordId, string type, string name, DedupIdentifier dedupId, long length, CancellationToken cancellationToken)
        {
            CheckConnection();
            return _taskClient.CreateAttachmentFromArtifactAsync(scopeIdentifier, hubName, planId, timelineId, timelineRecordId, type, name, dedupId.ValueString, length, cancellationToken: cancellationToken);
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

        public Task<TaskLog> AssociateLogAsync(Guid scopeIdentifier, string hubName, Guid planId, int logId, BlobIdentifierWithBlocks blobBlockId, int lineCount, CancellationToken cancellationToken)
        {
            CheckConnection();

            return _taskClient.AssociateLogAsync(scopeIdentifier, hubName, planId, logId, blobBlockId.Serialize(), lineCount, cancellationToken: cancellationToken);
        }

        public async Task<BlobIdentifierWithBlocks> UploadLogToBlobStore(Stream blob, string hubName, Guid planId, int logId)
        {
            CheckConnection();

            BlobIdentifier blobId = VsoHash.CalculateBlobIdentifierWithBlocks(blob).BlobId;

            // Since we read this while calculating the hash, the position needs to be reset before we send this
            blob.Position = 0;

            using (var blobClient = CreateArtifactsClient(_connection, default(CancellationToken)))
            {
                return await blobClient.UploadBlocksForBlobAsync(blobId, blob, default(CancellationToken));
            }
        }

        public async Task<(DedupIdentifier dedupId, ulong length)> UploadAttachmentToBlobStore(bool verbose, string itemPath, Guid planId, Guid jobId, CancellationToken cancellationToken)
        {
            int maxParallelism = HostContext.GetService<IConfigurationStore>().GetSettings().MaxDedupParallelism;
            var (dedupClient, clientTelemetry) = await DedupManifestArtifactClientFactory.Instance
                .CreateDedupClientAsync(
                    verbose,
                    (str) => Trace.Info(str),
                    this._connection,
                    maxParallelism,
                    clientType: null,
                    cancellationToken);

            var results = await BlobStoreUtils.UploadToBlobStore(verbose, itemPath, (level, uri, type) =>
                new TimelineRecordAttachmentTelemetryRecord(level, uri, type, nameof(UploadAttachmentToBlobStore), planId, jobId, Guid.Empty), (str) => Trace.Info(str), dedupClient, clientTelemetry, cancellationToken);

            await clientTelemetry.CommitTelemetryUpload(planId, jobId);

            return results;
        }

        private IBlobStoreHttpClient CreateArtifactsClient(VssConnection connection, CancellationToken cancellationToken)
        {
            var tracer = new CallbackAppTraceSource(str => Trace.Info(str), System.Diagnostics.SourceLevels.Information);

            ArtifactHttpClientFactory factory = new ArtifactHttpClientFactory(
                connection.Credentials,
                TimeSpan.FromSeconds(50),
                tracer,
                default(CancellationToken));

            return factory.CreateVssHttpClient<IBlobStoreHttpClient, BlobStore2HttpClient>(connection.GetClient<BlobStore2HttpClient>().BaseAddress);
        }
    }
}