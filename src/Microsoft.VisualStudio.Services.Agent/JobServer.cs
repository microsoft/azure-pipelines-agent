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
        Task<TaskLog> AssociateLogAsync(Guid scopeIdentifier, string hubName, Guid planId, int logId, string blobFileId, int lineCount, CancellationToken cancellationToken);
        Task<BlobIdentifier> UploadLogToBlobstorageService(Stream blob, string hubName, Guid planId, int logId);
        Task DownloadAsync(BlobIdentifier manifestId, string targetDirectory, CancellationToken cancellationToken);
    }

    public sealed class JobServer : AgentService, IJobServer
    {
        private bool _hasConnection;
        private VssConnection _connection;
        private TaskHttpClient _taskClient;
        private const string BuildLogScope = "buildlogs";

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

        public Task<TaskLog> AssociateLogAsync(Guid scopeIdentifier, string hubName, Guid planId, int logId, string blobFileId, int lineCount, CancellationToken cancellationToken)
        {
            CheckConnection();
            // TODO - add call to new _taskClient method here and return it instead of null
            return null;
        }

        public async Task<BlobIdentifier> UploadLogToBlobstorageService(Stream blob, string hubName, Guid planId, int logId)
        {
            CheckConnection();

            BlobIdentifier blobId = await CalculateBlobIdentifier(blob);
            var referenceId = $"{planId.ToString()}/{logId}/{Guid.NewGuid().ToString()}";
            var reference = new BlobReference(referenceId, BuildLogScope);

            using(var blobClient = CreateArtifactsClient(_connection, default(CancellationToken)))
            {   
                using (var semaphore = new SemaphoreSlim(4, 4))
                {
                    var domainId = WellKnownDomainIds.OriginalDomainId;
                    await VsoHash.WalkBlocksAsync(
                            blob,
                            blockActionSemaphore: semaphore,
                            multiBlocksInParallel: true,
                            singleBlockCallback: (blockBuffer, blockLength, blobIdWithBlocks) => 
                                {
                                    return blobClient.PutSingleBlockBlobAndReferenceAsync(blobId, blockBuffer, blockLength, reference, default(CancellationToken));
                                },
                            multiBlockCallback: (blockBuffer, blockLength, blockHash, isFinalBlock) =>
                                {
                                    return blobClient.PutBlobBlockAsync(blobId, blockBuffer, blockLength, default(CancellationToken));
                                },
                            multiBlockSealCallback: (blobIdWithBlocks) =>
                                {
                                    return blobClient.TryReferenceWithBlocksAsync(blobIdAndBlocks: blobIdWithBlocks, reference: reference, cancellationToken: default(CancellationToken));
                                });
                }
            }

            return blobId;
        }

        private async Task<BlobIdentifier> CalculateBlobIdentifier(Stream blob)
        {
            BlobIdentifierWithBlocks result = null;

            await VsoHash.WalkBlocksAsync(
                blob,
                blockActionSemaphore: null,
                multiBlocksInParallel: false,
                singleBlockCallback: (block, blockLength, blobIdWithBlocks) =>
                {
                    result = blobIdWithBlocks;
                    return Task.FromResult(0);
                },
                multiBlockCallback: (block, blockLength, blockHash, isFinalBlock) => Task.FromResult(0),
                multiBlockSealCallback: (blobIdWithBlocks) =>
                {
                    result = blobIdWithBlocks;
                    return Task.FromResult(0);
                }).ConfigureAwait(false);

            if (result == null)
            {
                throw new InvalidOperationException("Program error: CalculateBlobIdentifier did not calculate a value.");
            }

            return result.BlobId;
        }

        // TODO - remove this function (here and above)
        public async Task DownloadAsync(BlobIdentifier manifestId, string targetDirectory, CancellationToken cancellationToken)
        {
            CheckConnection();
            using(var client =  CreateArtifactsClient(_connection, cancellationToken))
            {
                var stream = await client.GetBlobAsync(manifestId, cancellationToken);
                using(FileStream outputFileStream = new FileStream(targetDirectory, FileMode.Create)) {  
                    stream.CopyTo(outputFileStream);  
                }
            }
        }

        private IBlobStoreHttpClient CreateArtifactsClient(VssConnection connection, CancellationToken cancellationToken){
            var tracer = new CallbackAppTraceSource(str => Trace.Info(str), System.Diagnostics.SourceLevels.Information);

            ArtifactHttpClientFactory factory = new ArtifactHttpClientFactory(
            connection.Credentials,
            TimeSpan.FromSeconds(50),
            tracer,
            default(CancellationToken));

            return factory.CreateVssHttpClient<IBlobStoreHttpClient, BlobStore2HttpClient>(connection.GetClient<DedupStoreHttpClient>().BaseAddress);
        }
    }
}