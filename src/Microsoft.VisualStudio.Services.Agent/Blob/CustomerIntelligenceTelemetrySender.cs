// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Content.Common.Telemetry;
using Microsoft.VisualStudio.Services.CustomerIntelligence.WebApi;
using Microsoft.VisualStudio.Services.WebPlatform;

namespace Microsoft.VisualStudio.Services.Agent.Blob
{
    public class CustomerIntelligenceTelemetrySender : ITelemetrySender
    {
        private CustomerIntelligenceHttpClient _ciClient;

        // Upload
        private long _chunksUploaded = 0;
        private long _compressionBytesSaved = 0;
        private long _dedupUploadBytesSaved = 0;
        private long _logicalContentBytesUploaded = 0;
        private long _physicalContentBytesUploaded = 0;
        private long _totalNumberOfChunks = 0;

        // Download
        private long _chunksDownloaded = 0;
        private long _compressionBytesSavedDown = 0;
        private long _dedupDownloadBytesSaved = 0;
        private long _physicalContentBytesDownloaded = 0;
        private long _totalBytesDown = 0;

        public CustomerIntelligenceTelemetrySender(VssConnection connection)
        {
            ArgUtil.NotNull(connection, nameof(connection));
            _ciClient = connection.GetClient<CustomerIntelligenceHttpClient>();
        }

        // Not used by the interface. We just want to capture successful telemetry for dedup analytics
        public void StartSender()
        {
        }
        public void StopSender()
        {
        }
        public void SendErrorTelemetry(ErrorTelemetryRecord errorTelemetry)
        {
        }
        public void SendRecord(TelemetryRecord record)
        {
        }

        public void SendActionTelemetry(ActionTelemetryRecord actionTelemetry)
        {
            if (actionTelemetry is IDedupRecord dedupRecord)
            {
                var uploadStats = dedupRecord.UploadStatistics;
                if (uploadStats != null)
                {
                    Interlocked.Add(ref this._chunksUploaded, uploadStats.ChunksUploaded);
                    Interlocked.Add(ref this._compressionBytesSaved, uploadStats.CompressionBytesSaved);
                    Interlocked.Add(ref this._dedupUploadBytesSaved, uploadStats.DedupUploadBytesSaved);
                    Interlocked.Add(ref this._logicalContentBytesUploaded, uploadStats.LogicalContentBytesUploaded);
                    Interlocked.Add(ref this._physicalContentBytesUploaded, uploadStats.PhysicalContentBytesUploaded);
                    Interlocked.Add(ref this._totalNumberOfChunks, uploadStats.TotalNumberOfChunks);
                }
                var downloadStats = dedupRecord.DownloadStatistics;
                if (downloadStats != null)
                {
                    Interlocked.Add(ref this._chunksDownloaded, downloadStats.ChunksDownloaded);
                    Interlocked.Add(ref this._compressionBytesSavedDown, downloadStats.CompressionBytesSaved);
                    Interlocked.Add(ref this._dedupDownloadBytesSaved, downloadStats.DedupDownloadBytesSaved);
                    Interlocked.Add(ref this._totalBytesDown, downloadStats.TotalContentBytes);
                    Interlocked.Add(ref this._physicalContentBytesDownloaded, downloadStats.PhysicalContentBytesDownloaded);
                }
            }
        }

        public async Task CommitTelemetryUpload(Guid planId, Guid jobId)
        {
            var ciData = new Dictionary<string, object>();

            ciData.Add("PlanId", planId);
            ciData.Add("JobId", jobId);
            
            ciData.Add("ChunksUploaded", this._chunksUploaded);
            ciData.Add("CompressionBytesSaved", this._compressionBytesSaved);
            ciData.Add("DedupDownloadBytesSaved", this._dedupUploadBytesSaved);
            ciData.Add("LogicalContentBytesUploaded", this._logicalContentBytesUploaded);
            ciData.Add("PhysicalContentBytesUploaded", this._physicalContentBytesUploaded);
            ciData.Add("TotalNumberOfChunks", this._totalNumberOfChunks);

            var ciEvent = new CustomerIntelligenceEvent
            {
                Area = "AzurePipelinesAgent",
                Feature = "BuildArtifacts",
                Properties = ciData
            };
            await _ciClient.PublishEventsAsync(new [] { ciEvent });
        }

        public Dictionary<string, object> GetTelemetryDownload(Guid planId, Guid jobId)
        {
            var ciData = new Dictionary<string, object>();

            ciData.Add("PlanId", planId);
            ciData.Add("JobId", jobId);

            ciData.Add("ChunksDownloaded", this._chunksDownloaded);
            ciData.Add("CompressionBytesSavedDownload", this._compressionBytesSavedDown);
            ciData.Add("DedupDownloadBytesSaved", this._dedupUploadBytesSaved);
            ciData.Add("PhysicalContentBytesDownloaded", this._physicalContentBytesDownloaded);
            ciData.Add("TotalBytesDownloaded", this._totalBytesDown);

            return ciData;
        }

        private async Task CommitTelemetry(Guid planId, Guid jobId, Dictionary<string, object> ciData)
        {
            ciData.Add("PlanId", planId);
            ciData.Add("JobId", jobId);

            var ciEvent = new CustomerIntelligenceEvent
            {
                Area = "AzurePipelinesAgent",
                Feature = "BuildArtifacts",
                Properties = ciData
            };
            await _ciClient.PublishEventsAsync(new [] { ciEvent });
        }
    }
}