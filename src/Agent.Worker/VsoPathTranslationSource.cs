// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    /// <summary>
    /// Identifies the agent-owned command and path field requesting translation.
    /// </summary>
    /// <remarks>
    /// ExecutionContext uses these identities to determine flag-gated Work validation.
    /// Names are emitted in telemetry and should remain stable.
    /// </remarks>
    public enum VsoPathTranslationSource
    {
        TaskAddAttachment,
        TaskUploadFile,
        TaskUploadSummary,
        /// <summary>Diagnostic sourcepath for task.logissue and its task.issue alias.</summary>
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
