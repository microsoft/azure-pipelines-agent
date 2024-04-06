// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace Microsoft.VisualStudio.Services.Agent.Worker.Handlers
{
    public class InvalidScriptArgsException : Exception
    {
        public InvalidScriptArgsException(string message) : base(message)
        {
        }
    }
}
