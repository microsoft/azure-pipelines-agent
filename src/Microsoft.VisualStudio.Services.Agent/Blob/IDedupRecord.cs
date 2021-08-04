// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.Services.BlobStore.Common.Telemetry;

namespace Microsoft.VisualStudio.Services.Agent.Blob
{
    public interface IDedupRecord
    {
        DedupUploadStatistics UploadStatistics { get; }

        DedupDownloadStatistics DownloadStatistics { get; }
    }
}