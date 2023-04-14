// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using Agent.Sdk;
using Microsoft.VisualStudio.Services.Content.Common.Telemetry;

namespace Agent.Plugins.PipelineArtifact.Telemetry
{
    /// <summary>
    /// Generic telemetry record for use with FileShare Artifact events.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1501: Avoid excessive inheritance")]
    public class FileShareActionRecord : PipelineArtifactActionRecord
    {
        public int TotalFile { get; private set; }
        public long TotalContentSize { get; private set; }
        public long TimeLapse { get; private set; }
        public int ExitCode { get; private set; }
        public IList<ArtifactRecord> ArtifactRecords { get; private set; }

        public FileShareActionRecord(TelemetryInformationLevel level, Uri baseAddress, string eventNamePrefix, string eventNameSuffix, AgentTaskPluginExecutionContext context, uint attemptNumber = 1)
            : base(level, baseAddress, eventNamePrefix, eventNameSuffix, context, attemptNumber)
        {
        }

        protected override void SetMeasuredActionResult<T>(T value)
        {
            if (value is FileSharePublishResult)
            {
                FileSharePublishResult result = value as FileSharePublishResult;
                ExitCode = result.ExitCode;
            }

            if (value is FileShareDownloadResult)
            {
                FileShareDownloadResult result = value as FileShareDownloadResult;
                TotalFile = result.FileCount;
                TotalContentSize = result.ContentSize;
                this.ArtifactRecords = result.ArtifactRecords;
            }
        }
    }

    public sealed class FileSharePublishResult
    {
        public int ExitCode { get; private set; }

        public FileSharePublishResult(int exitCode)
        {
            this.ExitCode = exitCode;
        }
    }

    public sealed class FileShareDownloadResult
    {
        public int FileCount { get; private set; }
        public long ContentSize { get; private set; }
        public IList<ArtifactRecord> ArtifactRecords { get; private set; }

        public FileShareDownloadResult(IList<ArtifactRecord> records, int fileCount, long contentSize)
        {
            this.FileCount = fileCount;
            this.ContentSize = contentSize;
            this.ArtifactRecords = records;
        }
    }

    public sealed class ArtifactRecord
    {
        public string ArtifactName { get; private set; }
        public int FileCount { get; private set; }
        public long ContentSize { get; private set; }
        public long TimeLapse { get; private set; }

        public ArtifactRecord(string artifactName, int fileCount, long contentSize, long timeLapse)
        {
            this.ArtifactName = artifactName;
            this.FileCount = fileCount;
            this.ContentSize = contentSize;
            this.TimeLapse = timeLapse;
        }
    }
}
