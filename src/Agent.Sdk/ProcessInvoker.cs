// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Agent.Sdk;
using Agent.Sdk.Knob;
using Microsoft.TeamFoundation.Framework.Common;

namespace Microsoft.VisualStudio.Services.Agent.Util
{

    // The implementation of the process invoker does not hook up DataReceivedEvent and ErrorReceivedEvent of Process,
    // instead, we read both STDOUT and STDERR stream manually on separate thread.
    // The reason is we find a huge perf issue about process STDOUT/STDERR with those events.
    public partial class ProcessInvoker : IDisposable
    {
        public static readonly bool ContinueAfterCancelProcessTreeKillAttemptDefault = false;
        private Process _proc;
        private Stopwatch _stopWatch;
        private int _asyncStreamReaderCount = 0;
        private bool _waitingOnStreams = false;
        private readonly AsyncManualResetEvent _outputProcessEvent = new AsyncManualResetEvent();
        private readonly TaskCompletionSource<bool> _processExitedCompletionSource = new TaskCompletionSource<bool>();
        private readonly ConcurrentQueue<string> _errorData = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> _outputData = new ConcurrentQueue<string>();
        private readonly TimeSpan _defaultSigintTimeout = TimeSpan.FromMilliseconds(7500);
        private readonly TimeSpan _defaultSigtermTimeout = TimeSpan.FromMilliseconds(2500);

        private ITraceWriter Trace { get; set; }

        private class AsyncManualResetEvent
        {
            private volatile TaskCompletionSource<bool> m_tcs = new TaskCompletionSource<bool>();

            public Task WaitAsync() { return m_tcs.Task; }

            public void Set()
            {
                var tcs = m_tcs;
                Task.Factory.StartNew(s => ((TaskCompletionSource<bool>)s).TrySetResult(true),
                    tcs, CancellationToken.None, TaskCreationOptions.PreferFairness, TaskScheduler.Default);
                tcs.Task.Wait();
            }

            public void Reset()
            {
                while (true)
                {
                    var tcs = m_tcs;
                    if (!tcs.Task.IsCompleted ||
                        Interlocked.CompareExchange(ref m_tcs, new TaskCompletionSource<bool>(), tcs) == tcs)
                        return;
                }
            }
        }

        public bool DisableWorkerCommands { get; set; }
        public bool TryUseGracefulShutdown { get; set; }
        public TimeSpan SigintTimeout { get; set; }
        public TimeSpan SigtermTimeout { get; set; }

        public event EventHandler<ProcessDataReceivedEventArgs> OutputDataReceived;
        public event EventHandler<ProcessDataReceivedEventArgs> ErrorDataReceived;

        public ProcessInvoker(ITraceWriter trace, bool disableWorkerCommands = false, TimeSpan? sigintTimeout = null, TimeSpan? sigtermTimeout = null)
        {
            this.Trace = trace;
            this.DisableWorkerCommands = disableWorkerCommands;
            this.SigintTimeout = sigintTimeout ?? _defaultSigintTimeout;
            this.SigtermTimeout = sigtermTimeout ?? _defaultSigtermTimeout;
        }

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
            bool inheritConsoleHandler,
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
                highPriorityProcess: false,
                continueAfterCancelProcessTreeKillAttempt: ProcessInvoker.ContinueAfterCancelProcessTreeKillAttemptDefault,
                cancellationToken: cancellationToken);
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
            ArgUtil.Null(_proc, nameof(_proc));
            ArgUtil.NotNullOrEmpty(fileName, nameof(fileName));

            Trace.Info("Starting process:");
            Trace.Info($"  File name: '{fileName}'");
            Trace.Info($"  Arguments: '{arguments}'");
            Trace.Info($"  Working directory: '{workingDirectory}'");
            Trace.Info($"  Require exit code zero: '{requireExitCodeZero}'");
            Trace.Info($"  Encoding web name: {outputEncoding?.WebName} ; code page: '{outputEncoding?.CodePage}'");
            Trace.Info($"  Force kill process on cancellation: '{killProcessOnCancel}'");
            Trace.Info($"  Redirected STDIN: '{redirectStandardIn != null}'");
            Trace.Info($"  Persist current code page: '{inheritConsoleHandler}'");
            Trace.Info($"  Keep redirected STDIN open: '{keepStandardInOpen}'");
            Trace.Info($"  High priority process: '{highPriorityProcess}'");
            Trace.Info($"  ContinueAfterCancelProcessTreeKillAttempt: '{continueAfterCancelProcessTreeKillAttempt}'");
            Trace.Info($"  Sigint timeout: '{SigintTimeout}'");
            Trace.Info($"  Sigterm timeout: '{SigtermTimeout}'");
            Trace.Info($"  Try to use graceful shutdown: {TryUseGracefulShutdown}");

            _proc = new Process();
            _proc.StartInfo.FileName = fileName;
            _proc.StartInfo.Arguments = arguments;
            _proc.StartInfo.WorkingDirectory = workingDirectory;
            _proc.StartInfo.UseShellExecute = false;
            _proc.StartInfo.CreateNoWindow = !inheritConsoleHandler;
            _proc.StartInfo.RedirectStandardInput = true;
            _proc.StartInfo.RedirectStandardError = true;
            _proc.StartInfo.RedirectStandardOutput = true;

            // Ensure we process STDERR even the process exit event happen before we start read STDERR stream.
            if (_proc.StartInfo.RedirectStandardError)
            {
                Trace.Info($"[Thread {Thread.CurrentThread.ManagedThreadId}] Before incrementing _asyncStreamReaderCount for STDERR: {_asyncStreamReaderCount}");
                Interlocked.Increment(ref _asyncStreamReaderCount);
                Trace.Info($"[Thread {Thread.CurrentThread.ManagedThreadId}] After incrementing _asyncStreamReaderCount for STDERR: {_asyncStreamReaderCount}");
            }

            // Ensure we process STDOUT even the process exit event happen before we start read STDOUT stream.
            if (_proc.StartInfo.RedirectStandardOutput)
            {
                Trace.Info($"[Thread {Thread.CurrentThread.ManagedThreadId}] Before incrementing _asyncStreamReaderCount for STDOUT: {_asyncStreamReaderCount}");
                Interlocked.Increment(ref _asyncStreamReaderCount);
                Trace.Info($"[Thread {Thread.CurrentThread.ManagedThreadId}] After incrementing _asyncStreamReaderCount for STDOUT: {_asyncStreamReaderCount}");
            }

            // If StandardErrorEncoding or StandardOutputEncoding is not specified the on the
            // ProcessStartInfo object, then .NET PInvokes to resolve the default console output
            // code page:
            //      [DllImport("api-ms-win-core-console-l1-1-0.dll", SetLastError = true)]
            //      public extern static uint GetConsoleOutputCP();
            if (PlatformUtil.RunningOnWindows)
            {
                StringUtil.EnsureRegisterEncodings();
            }

            if (outputEncoding != null)
            {
                _proc.StartInfo.StandardErrorEncoding = outputEncoding;
                _proc.StartInfo.StandardOutputEncoding = outputEncoding;
            }

            // Copy the environment variables.
            if (environment != null && environment.Count > 0)
            {
                foreach (KeyValuePair<string, string> kvp in environment)
                {
                    _proc.StartInfo.Environment[kvp.Key] = kvp.Value;
                }
            }

            // Set the TF_BUILD env variable.
            _proc.StartInfo.Environment["TF_BUILD"] = "True";

            // Hook up the events.
            _proc.EnableRaisingEvents = true;
            _proc.Exited += ProcessExitedHandler;

            // Start the process.
            _stopWatch = Stopwatch.StartNew();
            _proc.Start();

            // Decrease invoked process priority, in platform specifc way, relative to parent
            if (!highPriorityProcess)
            {
                DecreaseProcessPriority(_proc);
            }

            // Start the standard error notifications, if appropriate.
            if (_proc.StartInfo.RedirectStandardError)
            {
                StartReadStream(_proc.StandardError, _errorData);
            }

            // Start the standard output notifications, if appropriate.
            if (_proc.StartInfo.RedirectStandardOutput)
            {
                StartReadStream(_proc.StandardOutput, _outputData);
            }

            if (_proc.StartInfo.RedirectStandardInput)
            {
                if (redirectStandardIn != null)
                {
                    StartWriteStream(redirectStandardIn, _proc.StandardInput, keepStandardInOpen);
                }
                else
                {
                    // Close the input stream. This is done to prevent commands from blocking the build waiting for input from the user.
                    _proc.StandardInput.Close();
                }
            }

            AsyncManualResetEvent afterCancelKillProcessTreeAttemptSignal = new AsyncManualResetEvent();
            using (var registration = cancellationToken.Register(async () =>
            {
                Trace.Info($"##DEBUG_SB: Cancellation requested, attempting to cancel and kill process tree for process {_proc.Id}.");
                await CancelAndKillProcessTree(killProcessOnCancel);

                // signal to ensure we exit the loop after we attempt to cancel and kill the process tree (which is best effort)
                afterCancelKillProcessTreeAttemptSignal.Set();
                Trace.Info($"##DEBUG_SB: Cancellation requested, process tree killed for process {_proc.Id}.");
            }))
            {
                Trace.Info($"Process started with process id {_proc.Id}, waiting for process exit.");

                while (true)
                {
                    Task outputSignal = _outputProcessEvent.WaitAsync();
                    Task[] tasks;
                    Trace.Info($"##DEBUG_SB: whiletrueblock_processinvoker using block for process {_proc.Id}.");

                    if (continueAfterCancelProcessTreeKillAttempt)
                    {
                        Trace.Info($"##DEBUG_SB: PInvokerA for process {_proc.Id}.");
                        tasks = new Task[] { outputSignal, _processExitedCompletionSource.Task, afterCancelKillProcessTreeAttemptSignal.WaitAsync() };
                        Trace.Info($"##DEBUG_SB: PInvokerB for process {_proc.Id}.");
                    }
                    else
                    {
                        Trace.Info($"##DEBUG_SB: PInvokerC for process {_proc.Id}.");
                        tasks = new Task[] { outputSignal, _processExitedCompletionSource.Task };
                        Trace.Info($"##DEBUG_SB: PInvokerD for process {_proc.Id}.");
                    }

                    Trace.Info($"##DEBUG_SB: before signaled await for process {_proc.Id}.");
                    var signaled = await Task.WhenAny(tasks);
                    Trace.Info($"##DEBUG_SB: after signaled await for process {_proc.Id}.");

                    if (signaled == outputSignal)
                    {
                        Trace.Info($"##DEBUG_SB: ProcessOutput if signaled == outputSignal for process {_proc.Id}.");
                        ProcessOutput();
                    }
                    else
                    {
                        Trace.Info($"##DEBUG_SB: ProcessOutput if signaled != outputSignal for process {_proc.Id}.");
                        _stopWatch.Stop();
                        break;
                    }
                }

                // Just in case there was some pending output when the process shut down go ahead and check the
                // data buffers one last time before returning
                Trace.Info($"##DEBUG_SB: ProcessOutput before exit for process {_proc.Id}.");
                ProcessOutput();

                if (_proc.HasExited)
                {
                    Trace.Info($"Finished process {_proc.Id} with exit code {_proc.ExitCode}, and elapsed time {_stopWatch.Elapsed}.");
                }
                else
                {
                    Trace.Info($"Process _proc.HasExited={_proc.HasExited}, {_proc.Id},  and elapsed time {_stopWatch.Elapsed}.");
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Wait for process to finish.
            if (_proc.ExitCode != 0 && requireExitCodeZero)
            {
                throw new ProcessExitCodeException(exitCode: _proc.ExitCode, fileName: fileName, arguments: arguments);
            }

            return _proc.ExitCode;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_proc != null)
                {
                    _proc.Dispose();
                    _proc = null;
                }
            }
        }

        private void ProcessOutput()
        {
            List<string> errorData = new List<string>();
            List<string> outputData = new List<string>();

            Trace.Info($"##DEBUG_SB: before errorLine dequeue for process {_proc.Id}.");
            string errorLine;
            while (_errorData.TryDequeue(out errorLine))
            {
                errorData.Add(errorLine);
            }

            Trace.Info($"##DEBUG_SB: before outputLine dequeue for process {_proc.Id}.");
            string outputLine;
            while (_outputData.TryDequeue(out outputLine))
            {
                outputData.Add(outputLine);
            }

            Trace.Info($"##DEBUG_SB: Before reset thread info: Thread.CurrentThread.ManagedThreadId={Thread.CurrentThread.ManagedThreadId} for process {_proc.Id}.");
            _outputProcessEvent.Reset();
            Trace.Info($"##DEBUG_SB: After reset thread info: Thread.CurrentThread.ManagedThreadId={Thread.CurrentThread.ManagedThreadId} for process {_proc.Id}.");

            Trace.Info($"##DEBUG_SB: Writing the error lines for process {_proc.Id}.");
            // Write the error lines.
            if (errorData != null && this.ErrorDataReceived != null)
            {
                foreach (string line in errorData)
                {
                    if (line != null)
                    {
                        this.ErrorDataReceived(this, new ProcessDataReceivedEventArgs(line));
                    }
                }
            }

            Trace.Info($"##DEBUG_SB: Writing the output lines for process {_proc.Id}.");
            // Process the output lines.
            if (outputData != null && this.OutputDataReceived != null)
            {
                foreach (string line in outputData)
                {
                    if (line != null)
                    {
                        // The line is output from the process that was invoked.
                        this.OutputDataReceived(this, new ProcessDataReceivedEventArgs(line));
                    }
                }
            }
        }

        internal protected virtual async Task CancelAndKillProcessTree(bool killProcessOnCancel)
        {
            Trace.Info($"[Thread {Thread.CurrentThread.ManagedThreadId}] Entering CancelAndKillProcessTree for process {_proc.Id}.");

            bool gracefulShoutdown = TryUseGracefulShutdown && !killProcessOnCancel;

            ArgUtil.NotNull(_proc, nameof(_proc));

            Trace.Info($"##DEBUG_SB: Cancel and kill process tree for process {_proc.Id}.");

            if (!killProcessOnCancel)
            {
                bool sigint_succeed = await SendSIGINT(SigintTimeout);
                if (sigint_succeed)
                {
                    Trace.Info("Process cancelled successfully through Ctrl+C/SIGINT.");
                    Trace.Info($"[Thread {Thread.CurrentThread.ManagedThreadId}] Exiting CancelAndKillProcessTree for process {_proc.Id}.");
                    return;
                }

                if (gracefulShoutdown)
                {
                    Trace.Info($"[Thread {Thread.CurrentThread.ManagedThreadId}] Exiting CancelAndKillProcessTree for process {_proc.Id}.");
                    return;
                }

                bool sigterm_succeed = await SendSIGTERM(SigtermTimeout);
                if (sigterm_succeed)
                {
                    Trace.Info("Process terminate successfully through Ctrl+Break/SIGTERM.");
                    Trace.Info($"[Thread {Thread.CurrentThread.ManagedThreadId}] Exiting CancelAndKillProcessTree for process {_proc.Id}.");
                    return;
                }
            }

            Trace.Info("Kill entire process tree since both cancel and terminate signal has been ignored by the target process.");
            KillProcessTree();
            Trace.Info($"[Thread {Thread.CurrentThread.ManagedThreadId}] Exiting CancelAndKillProcessTree for process {_proc.Id}.");
        }

        private async Task<bool> SendSIGINT(TimeSpan timeout)
        {
            if (PlatformUtil.RunningOnWindows)
            {
                return await SendCtrlSignal(ConsoleCtrlEvent.CTRL_C, timeout);
            }

            return await SendPosixSignal(PosixSignals.SIGINT, timeout);
        }

        private async Task<bool> SendSIGTERM(TimeSpan timeout)
        {
            if (PlatformUtil.RunningOnWindows)
            {
                return await SendCtrlSignal(ConsoleCtrlEvent.CTRL_BREAK, timeout);
            }

            return await SendPosixSignal(PosixSignals.SIGTERM, timeout);
        }

        private void ProcessExitedHandler(object sender, EventArgs e)
        {
            Trace.Info($"Exited process {_proc.Id} with exit code {_proc.ExitCode}");

            // if ((_proc.StartInfo.RedirectStandardError || _proc.StartInfo.RedirectStandardOutput) && Interlocked.CompareExchange(ref _asyncStreamReaderCount, 0, 0) > 0)
            if ((_proc.StartInfo.RedirectStandardError || _proc.StartInfo.RedirectStandardOutput) && _asyncStreamReaderCount != 0)
            {
                _waitingOnStreams = true;

                Task.Run(async () =>
                {
                    // Wait 5 seconds and then Cancel/Kill process tree
                    await Task.Delay(TimeSpan.FromSeconds(5));
                    KillProcessTree();
                    _processExitedCompletionSource.TrySetResult(true);
                });
            }
            else
            {
                _processExitedCompletionSource.TrySetResult(true);
            }
        }

        private void StartReadStream(StreamReader reader, ConcurrentQueue<string> dataBuffer)
        {
            Task.Run(() =>
            {
                //add _proc.id info everywhere that thread id is being logged
                Trace.Info($"[Thread {Thread.CurrentThread.ManagedThreadId}] Start reading stream for process {_proc.Id}.");
                while (!reader.EndOfStream)
                {
                    string line = reader.ReadLine();
                    if (line != null)
                    {
                        if (DisableWorkerCommands)
                        {
                            Trace.Info($"##DEBUG_SB: Deactivating VSO commands for process {_proc.Id}.");
                            line = StringUtil.DeactivateVsoCommands(line);
                        }
                        dataBuffer.Enqueue(line);
                        Trace.Info($"[Thread {Thread.CurrentThread.ManagedThreadId}] Enqueued line: {line} for process {_proc.Id}.");
                        _outputProcessEvent.Set();
                    }
                }

                Trace.Info($"[Thread {Thread.CurrentThread.ManagedThreadId}] STDOUT/STDERR stream read finished for process {_proc.Id}.");
                Trace.Info("STDOUT/STDERR stream read finished.");

                if (Interlocked.Decrement(ref _asyncStreamReaderCount) == 0 && _waitingOnStreams)
                {
                    Trace.Info($"[Thread {Thread.CurrentThread.ManagedThreadId}] All stream readers finished. Setting process exited completion source for process {_proc.Id}.");
                    _processExitedCompletionSource.TrySetResult(true);
                }
            });
        }

        private void StartWriteStream(InputQueue<string> redirectStandardIn, StreamWriter standardIn, bool keepStandardInOpen)
        {
            Task.Run(async () =>
            {
                // Write the contents as UTF8 to handle all characters.
                var utf8Writer = new StreamWriter(standardIn.BaseStream, new UTF8Encoding(false));

                while (!_processExitedCompletionSource.Task.IsCompleted)
                {
                    Task<string> dequeueTask = redirectStandardIn.DequeueAsync();
                    var completedTask = await Task.WhenAny(dequeueTask, _processExitedCompletionSource.Task);
                    if (completedTask == dequeueTask)
                    {
                        string input = await dequeueTask;
                        if (input != null)
                        {
                            utf8Writer.WriteLine(input);
                            utf8Writer.Flush();

                            if (!keepStandardInOpen)
                            {
                                Trace.Info($"Close STDIN after the first redirect finished. for process {_proc.Id}.");
                                standardIn.Close();
                                break;
                            }
                        }
                    }
                }

                Trace.Info("STDIN stream write finished.");
            });
        }

        private void KillProcessTree()
        {
            if (PlatformUtil.RunningOnWindows)
            {
                WindowsKillProcessTree();
            }
            else
            {
                NixKillProcessTree();
            }
        }

        private void DecreaseProcessPriority(Process process)
        {
            if (PlatformUtil.HostOS != PlatformUtil.OS.Linux)
            {
                Trace.Info("OOM score adjustment is Linux-only.");
                return;
            }

            int oomScoreAdj = 500;
            string userOomScoreAdj;
            if (process.StartInfo.Environment.TryGetValue("PIPELINE_JOB_OOMSCOREADJ", out userOomScoreAdj))
            {
                int userOomScoreAdjParsed;
                if (int.TryParse(userOomScoreAdj, out userOomScoreAdjParsed) && userOomScoreAdjParsed >= -1000 && userOomScoreAdjParsed <= 1000)
                {
                    oomScoreAdj = userOomScoreAdjParsed;
                }
                else
                {
                    Trace.Info($"Invalid PIPELINE_JOB_OOMSCOREADJ ({userOomScoreAdj}). Valid range is -1000:1000. Using default 500.");
                }
            }
            // Values (up to 1000) make the process more likely to be killed under OOM scenario,
            // protecting the agent by extension. Default of 500 is likely to get killed, but can
            // be adjusted up or down as appropriate.
            WriteProcessOomScoreAdj(process.Id, oomScoreAdj);
        }
    }

    public sealed class ProcessExitCodeException : Exception
    {
        public int ExitCode { get; private set; }

        public ProcessExitCodeException(int exitCode, string fileName, string arguments)
            : base(StringUtil.Loc("ProcessExitCode", exitCode, fileName, arguments))
        {
            ExitCode = exitCode;
        }
    }

    public sealed class ProcessDataReceivedEventArgs : EventArgs
    {
        public ProcessDataReceivedEventArgs(string data)
        {
            Data = data;
        }

        public string Data { get; set; }
    }
}
