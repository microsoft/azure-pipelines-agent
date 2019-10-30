// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

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
            this.FailedFiles.AddRange(resultToAdd.FailedFiles);
            this.TotalFileSizeUploaded += resultToAdd.TotalFileSizeUploaded;
        }
    }
}
