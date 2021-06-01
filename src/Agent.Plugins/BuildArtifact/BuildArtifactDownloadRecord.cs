// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using Agent.Sdk;
using Microsoft.VisualStudio.Services.Agent.Blob;
using Microsoft.VisualStudio.Services.Content.Common.Telemetry;
using Microsoft.VisualStudio.Services.BlobStore.Common.Telemetry;

namespace Agent.Plugins.BuildArtifacts.Telemetry
{
    /// <summary>
    /// Generic telemetry record for use with Build Artifact downloads.
    /// </summary>
    public class BuildArtifactDownloadRecord : PipelineTelemetryRecord
    {
        // These properties exist so the telemetry reader can find and publish them
        public long ChunksDownloaded { get { return _chunksDownloaded; } }
        public long CompressionBytesSaved { get { return _compressionBytesSaved; } }
        public long DedupDownloadBytesSaved { get { return _dedupDownloadBytesSaved; } }
        public long NodesDownloaded { get { return _nodesDownloaded; } }
        public long PhysicalContentBytesDownloaded { get { return _physicalContentBytesDownloaded; } }
        public long TotalContentBytes { get { return _totalContentBytes; } }

        // Dedup download stats
        private long _chunksDownloaded = 0;
        private long _compressionBytesSaved = 0;
        private long _dedupDownloadBytesSaved = 0;
        private long _nodesDownloaded = 0;
        private long _physicalContentBytesDownloaded = 0;
        private long _totalContentBytes = 0;

        public BuildArtifactDownloadRecord(TelemetryInformationLevel level, Uri baseAddress, string eventNamePrefix, string eventNameSuffix, AgentTaskPluginExecutionContext context, uint attemptNumber = 1)
            : base(level, baseAddress, eventNamePrefix, eventNameSuffix, context, attemptNumber)
        {
        }

        protected override void SetMeasuredActionResult<T>(T value)
        {
            base.SetMeasuredActionResult(value);
            if (value is DedupDownloadStatistics downStats)
            {
                Interlocked.Add(ref _chunksDownloaded, downStats.ChunksDownloaded);
                Interlocked.Add(ref _compressionBytesSaved, downStats.CompressionBytesSaved);
                Interlocked.Add(ref _dedupDownloadBytesSaved, downStats.DedupDownloadBytesSaved);
                Interlocked.Add(ref _nodesDownloaded, downStats.NodesDownloaded);
                Interlocked.Add(ref _physicalContentBytesDownloaded, downStats.PhysicalContentBytesDownloaded);
                Interlocked.Add(ref _totalContentBytes, downStats.TotalContentBytes);
            }
        }
    }
}