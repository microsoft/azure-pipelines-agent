// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.Framework.Common;
using Agent.Sdk;

namespace Microsoft.VisualStudio.Services.Agent
{
    [ServiceLocator(Default = typeof(ProcessInvokerWrapper))]
    public interface IProcessInvoker : IDisposable, IAgentService
    {
        event EventHandler<ProcessDataReceivedEventArgs> OutputDataReceived;
        event EventHandler<ProcessDataReceivedEventArgs> ErrorDataReceived;

        Task<int> ExecuteAsync(
            string workingDirectory,
            string fileName,
            string arguments,
            IDictionary<string, string> environment,
            CancellationToken cancellationToken);

        Task<int> ExecuteAsync(
            string workingDirectory,
            string fileName,
            string arguments,
            IDictionary<string, string> environment,
            bool requireExitCodeZero,
            CancellationToken cancellationToken);

        Task<int> ExecuteAsync(
            string workingDirectory,
            string fileName,
            string arguments,
            IDictionary<string, string> environment,
            bool requireExitCodeZero,
            Encoding outputEncoding,
            CancellationToken cancellationToken);

        Task<int> ExecuteAsync(
            string workingDirectory,
            string fileName,
            string arguments,
            IDictionary<string, string> environment,
            bool requireExitCodeZero,
            Encoding outputEncoding,
            bool killProcessOnCancel,
            CancellationToken cancellationToken);

        Task<int> ExecuteAsync(
            string workingDirectory,
            string fileName,
            string arguments,
            IDictionary<string, string> environment,
            bool requireExitCodeZero,
            Encoding outputEncoding,
            bool killProcessOnCancel,
            InputQueue<string> redirectStandardIn,
            CancellationToken cancellationToken);

        Task<int> ExecuteAsync(
            string workingDirectory,
            string fileName,
            string arguments,
            IDictionary<string, string> environment,
            bool requireExitCodeZero,
            Encoding outputEncoding,
            bool killProcessOnCancel,
            InputQueue<string> redirectStandardIn,
            bool inheritConsoleHandler,
            bool continueAfterCancelProcessTreeKillAttempt,
            CancellationToken cancellationToken);

        Task<int> ExecuteAsync(
            string workingDirectory,
            string fileName,
            string arguments,
            IDictionary<string, string> environment,
            bool requireExitCodeZero,
            Encoding outputEncoding,
            bool killProcessOnCancel,
            InputQueue<string> redirectStandardIn,
            bool inheritConsoleHandler,
            bool keepStandardInOpen,
            bool continueAfterCancelProcessTreeKillAttempt,
            CancellationToken cancellationToken);

        Task<int> ExecuteAsync(
            string workingDirectory,
            string fileName,
            string arguments,
            IDictionary<string, string> environment,
            bool requireExitCodeZero,
            Encoding outputEncoding,
            bool killProcessOnCancel,
            InputQueue<string> redirectStandardIn,
            bool inheritConsoleHandler,
            bool keepStandardInOpen,
            bool highPriorityProcess,
            bool continueAfterCancelProcessTreeKillAttempt,
            CancellationToken cancellationToken);

        Task<int> ExecuteAsync(ProcessInvokerParams inputParams, CancellationToken cancellationToken);
    }

    // The implementation of the process invoker does not hook up DataReceivedEvent and ErrorReceivedEvent of Process,
    // instead, we read both STDOUT and STDERR stream manually on seperate thread.
    // The reason is we find a huge perf issue about process STDOUT/STDERR with those events.
    //
    // Missing functionalities:
    //       1. Cancel/Kill process tree
    //       2. Make sure STDOUT and STDERR not process out of order
    public sealed class ProcessInvokerWrapper : AgentService, IProcessInvoker
    {
        private ProcessInvoker _invoker;
        public bool DisableWorkerCommands { get; set; }

        public override void Initialize(IHostContext hostContext)
        {
            base.Initialize(hostContext);
            _invoker = new ProcessInvoker(Trace, DisableWorkerCommands);
        }

        public event EventHandler<ProcessDataReceivedEventArgs> OutputDataReceived;
        public event EventHandler<ProcessDataReceivedEventArgs> ErrorDataReceived;

        public Task<int> ExecuteAsync(
            string workingDirectory,
            string fileName,
            string arguments,
            IDictionary<string, string> environment,
            CancellationToken cancellationToken)
        {
            return ExecuteAsync(
                workingDirectory: workingDirectory,
                fileName: fileName,
                arguments: arguments,
                environment: environment,
                requireExitCodeZero: false,
                cancellationToken: cancellationToken);
        }

        public Task<int> ExecuteAsync(
            string workingDirectory,
            string fileName,
            string arguments,
            IDictionary<string, string> environment,
            bool requireExitCodeZero,
            CancellationToken cancellationToken)
        {
            return ExecuteAsync(
                workingDirectory: workingDirectory,
                fileName: fileName,
                arguments: arguments,
                environment: environment,
                requireExitCodeZero: requireExitCodeZero,
                outputEncoding: null,
                cancellationToken: cancellationToken);
        }

        public Task<int> ExecuteAsync(
            string workingDirectory,
            string fileName,
            string arguments,
            IDictionary<string, string> environment,
            bool requireExitCodeZero,
            Encoding outputEncoding,
            CancellationToken cancellationToken)
        {
            return ExecuteAsync(
                workingDirectory: workingDirectory,
                fileName: fileName,
                arguments: arguments,
                environment: environment,
                requireExitCodeZero: requireExitCodeZero,
                outputEncoding: outputEncoding,
                killProcessOnCancel: false,
                cancellationToken: cancellationToken);
        }

        public Task<int> ExecuteAsync(
            string workingDirectory,
            string fileName,
            string arguments,
            IDictionary<string, string> environment,
            bool requireExitCodeZero,
            Encoding outputEncoding,
            bool killProcessOnCancel,
            CancellationToken cancellationToken)
        {
            return ExecuteAsync(
                workingDirectory: workingDirectory,
                fileName: fileName,
                arguments: arguments,
                environment: environment,
                requireExitCodeZero: requireExitCodeZero,
                outputEncoding: outputEncoding,
                killProcessOnCancel: killProcessOnCancel,
                redirectStandardIn: null,
                cancellationToken: cancellationToken);
        }

        public Task<int> ExecuteAsync(
            string workingDirectory,
            string fileName,
            string arguments,
            IDictionary<string, string> environment,
            bool requireExitCodeZero,
            Encoding outputEncoding,
            bool killProcessOnCancel,
            InputQueue<string> redirectStandardIn,
            CancellationToken cancellationToken)
        {
            return ExecuteAsync(
                workingDirectory: workingDirectory,
                fileName: fileName,
                arguments: arguments,
                environment: environment,
                requireExitCodeZero: requireExitCodeZero,
                outputEncoding: outputEncoding,
                killProcessOnCancel: killProcessOnCancel,
                redirectStandardIn: redirectStandardIn,
                inheritConsoleHandler: false,
                continueAfterCancelProcessTreeKillAttempt: ProcessInvoker.ContinueAfterCancelProcessTreeKillAttemptDefault,
                cancellationToken: cancellationToken
            );
        }

        public Task<int> ExecuteAsync(
            string workingDirectory,
            string fileName,
            string arguments,
            IDictionary<string, string> environment,
            bool requireExitCodeZero,
            Encoding outputEncoding,
            bool killProcessOnCancel,
            InputQueue<string> redirectStandardIn,
            bool inheritConsoleHandler,
            bool continueAfterCancelProcessTreeKillAttempt,
            CancellationToken cancellationToken)
        {
            return ExecuteAsync(
                workingDirectory: workingDirectory,
                fileName: fileName,
                arguments: arguments,
                environment: environment,
                requireExitCodeZero: requireExitCodeZero,
                outputEncoding: outputEncoding,
                killProcessOnCancel: killProcessOnCancel,
                redirectStandardIn: redirectStandardIn,
                inheritConsoleHandler: inheritConsoleHandler,
                keepStandardInOpen: false,
                continueAfterCancelProcessTreeKillAttempt: continueAfterCancelProcessTreeKillAttempt,
                cancellationToken: cancellationToken
            );
        }

        public Task<int> ExecuteAsync(
            string workingDirectory,
            string fileName,
            string arguments,
            IDictionary<string, string> environment,
            bool requireExitCodeZero,
            Encoding outputEncoding,
            bool killProcessOnCancel,
            InputQueue<string> redirectStandardIn,
            bool inheritConsoleHandler,
            bool keepStandardInOpen,
            bool continueAfterCancelProcessTreeKillAttempt,
            CancellationToken cancellationToken)
        {
            return ExecuteAsync(
                workingDirectory: workingDirectory,
                fileName: fileName,
                arguments: arguments,
                environment: environment,
                requireExitCodeZero: requireExitCodeZero,
                outputEncoding: outputEncoding,
                killProcessOnCancel: killProcessOnCancel,
                redirectStandardIn: redirectStandardIn,
                inheritConsoleHandler: inheritConsoleHandler,
                keepStandardInOpen: keepStandardInOpen,
                highPriorityProcess: false,
                continueAfterCancelProcessTreeKillAttempt: continueAfterCancelProcessTreeKillAttempt,
                cancellationToken: cancellationToken
            );
        }

        public async Task<int> ExecuteAsync(
            string workingDirectory,
            string fileName,
            string arguments,
            IDictionary<string, string> environment,
            bool requireExitCodeZero,
            Encoding outputEncoding,
            bool killProcessOnCancel,
            InputQueue<string> redirectStandardIn,
            bool inheritConsoleHandler,
            bool keepStandardInOpen,
            bool highPriorityProcess,
            bool continueAfterCancelProcessTreeKillAttempt,
            CancellationToken cancellationToken)
        {
            return await ExecuteAsync(
                inputParams: new()
                {
                    WorkingDirectory = workingDirectory,
                    FileName = fileName,
                    Arguments = arguments,
                    Environment = environment,
                    RequireExitCodeZero = requireExitCodeZero,
                    OutputEncoding = outputEncoding,
                    KillProcessOnCancel = killProcessOnCancel,
                    RedirectStandardIn = redirectStandardIn,
                    InheritConsoleHandler = inheritConsoleHandler,
                    KeepStandardInOpen = keepStandardInOpen,
                    HighPriorityProcess = highPriorityProcess,
                    ContinueAfterCancelProcessTreeKillAttempt = continueAfterCancelProcessTreeKillAttempt,
                },
                cancellationToken: cancellationToken);
        }

        public async Task<int> ExecuteAsync(ProcessInvokerParams inputParams, CancellationToken cancellationToken)
        {
            _invoker.ErrorDataReceived += ErrorDataReceived;
            _invoker.OutputDataReceived += OutputDataReceived;

            return await _invoker.ExecuteAsync(inputParams, cancellationToken);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_invoker != null)
                {
                    _invoker.Dispose();
                    _invoker = null;
                }
            }
        }
    }
}
