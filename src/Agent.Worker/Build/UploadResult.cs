// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Microsoft.VisualStudio.Services.Agent.Worker.Build
{
    public class UploadResult
    {
        public UploadResult()
        {
            FailedFiles = new List<string>();
            TotalFileSizeUploaded = 0;
        }

        public UploadResult(List<string> failedFiles, long totalFileSizeUploaded)
        {
            FailedFiles = failedFiles;
            TotalFileSizeUploaded = totalFileSizeUploaded;
        }
        public List<string> FailedFiles { get; set; }

        public long TotalFileSizeUploaded { get; set; }

        public void AddUploadResult(UploadResult resultToAdd)
        {
            ArgUtil.NotNull(resultToAdd, nameof(resultToAdd));

            this.FailedFiles.AddRange(resultToAdd.FailedFiles);
            this.TotalFileSizeUploaded += resultToAdd.TotalFileSizeUploaded;
        }
    }
}
