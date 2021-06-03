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

        // Telemetry is recorded in parallel. This lock is used to synchronize adds
        private readonly object _lock = new object();

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
                lock (_lock)
                {
                    var uploadStats = dedupRecord.UploadStatistics;
                    if (uploadStats != null)
                    {
                        this._chunksUploaded += uploadStats.ChunksUploaded;
                        this._compressionBytesSaved += uploadStats.CompressionBytesSaved;
                        this._dedupUploadBytesSaved += uploadStats.DedupUploadBytesSaved;
                        this._logicalContentBytesUploaded += uploadStats.LogicalContentBytesUploaded;
                        this._physicalContentBytesUploaded += uploadStats.PhysicalContentBytesUploaded;
                        this._totalNumberOfChunks += uploadStats.TotalNumberOfChunks;
                    }
                    var downloadStats = dedupRecord.DownloadStatistics;
                    if (downloadStats != null)
                    {
                        this._chunksDownloaded += downloadStats.ChunksDownloaded;
                        this._compressionBytesSavedDown += downloadStats.CompressionBytesSaved;
                        this._dedupDownloadBytesSaved += downloadStats.DedupDownloadBytesSaved;
                        this._totalBytesDown += downloadStats.TotalContentBytes;
                        this._physicalContentBytesDownloaded += downloadStats.PhysicalContentBytesDownloaded;
                    }
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
            ciData.Add("DedupUploadBytesSaved", this._dedupUploadBytesSaved);
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

        public Dictionary<string, object> GetArtifactDownloadTelemetry(Guid planId, Guid jobId)
        {
            var ciData = new Dictionary<string, object>();

            ciData.Add("PlanId", planId);
            ciData.Add("JobId", jobId);

            ciData.Add("ChunksDownloaded", this._chunksDownloaded);
            ciData.Add("CompressionBytesSavedDownload", this._compressionBytesSavedDown);
            ciData.Add("DedupDownloadBytesSaved", this._dedupDownloadBytesSaved);
            ciData.Add("PhysicalContentBytesDownloaded", this._physicalContentBytesDownloaded);
            ciData.Add("TotalBytesDownloaded", this._totalBytesDown);

            return ciData;
        }
    }
}