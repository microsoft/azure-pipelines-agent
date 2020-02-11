// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace Microsoft.VisualStudio.Services.Agent.Worker.Release.Artifacts
{

    public class CommitsDownloadException : Exception
    {
        public CommitsDownloadException()
        {
        }

        public CommitsDownloadException(string message) : base(message)
        {
        }

        public CommitsDownloadException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
