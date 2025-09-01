// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Microsoft.VisualStudio.Services.Agent.Util
{
    public partial class ProcessInvoker : IDisposable
    {
        private async Task<bool> SendPosixSignal(PosixSignals signal, TimeSpan timeout)
        {
            if (_proc == null)
            {
                Trace.Info(StringUtil.SafeLog("Process already exited, no need to send {0}.", signal));
                return true;
            }

            Trace.Info($"Sending {signal} to process {_proc.Id}.");
            int errorCode = kill(_proc.Id, (int)signal);
            if (errorCode != 0)
            {
                Trace.Info($"{signal} signal did not fire successfully.");
                Trace.Info($"Error code: {errorCode}.");
                return false;
            }

            Trace.Info($"Successfully sent {signal} to process {_proc.Id}.");
            Trace.Info($"Waiting for process exit or {timeout.TotalSeconds} seconds after {signal} signal fired.");
            var completedTask = await Task.WhenAny(Task.Delay(timeout), _processExitedCompletionSource.Task);
            if (completedTask == _processExitedCompletionSource.Task)
            {
                Trace.Info("Process exited successfully.");
                return true;
            }
            else
            {
                Trace.Info($"Process did not honor {signal} signal within {timeout.TotalSeconds} seconds.");
                return false;
            }
        }

        private void NixKillProcessTree()
        {
            try
            {
                if (_proc?.HasExited == false)
                {
                    _proc?.Kill();
                }
            }
            catch (InvalidOperationException ex)
            {
                Trace.Info("Ignore InvalidOperationException during Process.Kill().");
                Trace.Info(ex.ToString());
            }
        }

        private void WriteProcessOomScoreAdj(int processId, int oomScoreAdj)
        {
            if (PlatformUtil.HostOS != PlatformUtil.OS.Linux)
            {
                Trace.Info("OOM score adjustment is Linux only");
                return;
            }

            try
            {
                string procFilePath = $"/proc/{processId}/oom_score_adj";
                if (File.Exists(procFilePath))
                {
                    File.WriteAllText(procFilePath, oomScoreAdj.ToString());
                    Trace.Info($"Updated oom_score_adj to {oomScoreAdj} for PID: {processId}.");
                }
            }
            catch (Exception ex)
            {
                Trace.Info($"Failed to update oom_score_adj for PID: {processId}.");
                Trace.Info(ex.ToString());
            }
        }

        private enum PosixSignals : int
        {
            SIGINT = 2,
            SIGTERM = 15
        }

        [DllImport("libc", SetLastError = true)]
        private static extern int kill(int pid, int sig);
    }
}