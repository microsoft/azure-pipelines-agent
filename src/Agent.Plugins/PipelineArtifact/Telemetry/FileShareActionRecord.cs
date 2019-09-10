using System;
using Agent.Sdk;
using Microsoft.VisualStudio.Services.Content.Common.Telemetry;

namespace Agent.Plugins.PipelineArtifact.Telemetry
{
    /// <summary>
    /// Generic telemetry record for use with Pipeline Artifact events.
    /// </summary>
    public class FileShareActionRecord : PipelineArtifactActionRecord
    {
        public new long FileCount;
        public long ContentSize;
        public long TimeLapse;
        public string Command;
        public int ExitCode;

        public FileShareActionRecord(TelemetryInformationLevel level, Uri baseAddress, string eventNamePrefix, string eventNameSuffix, AgentTaskPluginExecutionContext context, uint attemptNumber = 1)
            : base(level, baseAddress, eventNamePrefix, eventNameSuffix, context, attemptNumber)
        {
        }

        protected override void SetMeasuredActionResult<T>(T value)
        {
            if (value is FileSharePublishResult)
            {
                FileSharePublishResult result = value as FileSharePublishResult;
                Command = result.Command;
                TimeLapse = result.TimeLapse;
                ExitCode = result.ExitCode;
            }

            if (value is FileShareDownloadResult)
            {
                FileShareDownloadResult result = value as FileShareDownloadResult;
                FileCount = result.FileCount;
                ContentSize = result.ContentSize;
                TimeLapse = result.TimeLapse;
            }
        }
    }
 
    public sealed class FileSharePublishResult
    {
        public readonly string Command;
        public readonly long TimeLapse;
        public readonly int ExitCode;

        public FileSharePublishResult(long timeLapse, string command, int exitCode)
        {
            this.TimeLapse = timeLapse;
            this.Command = command;
            this.ExitCode = exitCode;
        }
    }

    public sealed class FileShareDownloadResult
    {
        public long FileCount;
        public long ContentSize;
        public long TimeLapse;

        public FileShareDownloadResult(long timeLapse, long fileCount, long contentSize)
        {
            this.TimeLapse = timeLapse;
            this.FileCount = fileCount;
            this.ContentSize = contentSize;
        }
    }
}