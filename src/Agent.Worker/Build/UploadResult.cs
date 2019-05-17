using System;
using System.Collections.Generic;

namespace Microsoft.VisualStudio.Services.Agent.Worker.Build
{
    public class UploadResult
    {
        public List<string> FailedFiles { get; set; }

        public long TotalFileSizeUploaded { get; set; }

        public void AddUploadResult(UploadResult resultToAdd)
        {
            this.FailedFiles.AddRange(resultToAdd.FailedFiles);
            this.TotalFileSizeUploaded += resultToAdd.TotalFileSizeUploaded;
        }
    }
}
