// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Text;
using Microsoft.TeamFoundation.Framework.Common;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Agent.Sdk
{
    public class ProcessInvokerParams
    {
        // We always must have a file name to execute.
        public string FileName { get; init; }

        // Default value of Process.StartInfo.WorkingDirectory is an empty string.
        public string WorkingDirectory { get; init; } = string.Empty;

        public string Arguments { get; init; } = string.Empty;

        public IDictionary<string, string> Environment { get; init; } = null;

        public bool RequireExitCodeZero { get; init; } = false;

        public Encoding OutputEncoding { get; init; } = null;

        public bool KillProcessOnCancel { get; init; } = false;

        public InputQueue<string> RedirectStandardIn { get; init; } = null;

        public bool InheritConsoleHandler { get; init; } = false;

        public bool KeepStandardInOpen { get; init; } = false;

        public bool HighPriorityProcess { get; init; } = false;

        public bool ContinueAfterCancelProcessTreeKillAttempt { get; init; } = ProcessInvoker.ContinueAfterCancelProcessTreeKillAttemptDefault;

        public ProcessInvokerParams() { }

        public ProcessInvokerParams(
            string workingDirectory,
            string fileName,
            string arguments)
        {
            WorkingDirectory = workingDirectory;
            FileName = fileName;
            Arguments = arguments;
        }
    }
}
