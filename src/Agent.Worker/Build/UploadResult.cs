using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.VisualStudio.Services.WebApi;
using System.Net.Http;
using System.Net;

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
