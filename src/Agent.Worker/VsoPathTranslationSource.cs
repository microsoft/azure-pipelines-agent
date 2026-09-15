// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    /// <summary>
    /// Identifies the command and path field being translated.
    /// </summary>
    /// <remarks>Names are emitted in telemetry; keep them stable.</remarks>
    public enum VsoPathTranslationSource
    {
        TaskAddAttachment,
        TaskUploadFile,
        TaskUploadSummary,
        /// <summary>Source path for task.logissue and its task.issue alias.</summary>
        TaskLogIssueSourcePath,
        ArtifactUpload,
        BuildUploadLog,
        BuildUploadSummary,
        ResultsPublishData,
        ResultsPublishResultFiles,
        CodeCoveragePublishSummaryFile,
        CodeCoveragePublishReportDirectory,
        CodeCoveragePublishAdditionalFiles
    }
}
