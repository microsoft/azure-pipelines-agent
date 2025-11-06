// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.VisualStudio.Services.Agent.Worker.Container
{
    [ServiceLocator(Default = typeof(ContainerDiagnosticsManager))]
    public interface IContainerDiagnosticsManager : IAgentService
    {
        Task CollectDockerExecFailureDiagnosticsAsync(
            Exception originalException,
            string dockerPath,
            string dockerArgs,
            string containerId);
    }

    public class ContainerDiagnosticsManager : AgentService, IContainerDiagnosticsManager
    {
        /// <summary>
        /// Collects comprehensive diagnostics when docker exec command fails
        /// </summary>
        public async Task CollectDockerExecFailureDiagnosticsAsync(
            Exception originalException,
            string dockerPath,
            string dockerArgs,
            string containerId)
        {
            var dockerManager = HostContext.GetService<IDockerCommandManager>();

            try
            {
                using (Trace.EnteringWithDuration())
                {
                    Trace.Error("Docker exec failure diagnostics started");
                    Trace.Error($"Exception: {originalException.GetType().Name}: {originalException.Message}");
                    Trace.Error($"Failed command: {dockerPath} {dockerArgs}");

                    // Extract exit code from exception
                    int? exitCode = null;
                    if (originalException is ProcessExitCodeException processEx)
                    {
                        exitCode = processEx.ExitCode;
                        Trace.Error($"Exit code: {exitCode}");
                    }

                    Trace.Info($"Container ID: {containerId}");
                    Trace.Info("Collecting system information");
                    await CollectBasicSystemInfo(Trace);

                    // Run diagnostics (this collects container state internally)
                    await RunDiagnostics(exitCode, dockerManager, containerId, dockerArgs);

                    Trace.Info("Docker exec failure diagnostics completed");
                }
            }
            catch (Exception diagEx)
            {
                Trace.Error($"Diagnostic collection failed: {diagEx.ToString()}");
            }
        }

        /// <summary>
        /// Evidence-based diagnostics - collects all evidence first, then analyzes to determine root cause
        /// </summary>
        private async Task RunDiagnostics(int? exitCode, IDockerCommandManager dockerManager, string containerId, string dockerArgs)
        {
            try
            {
                using (Trace.EnteringWithDuration())
                {
                    Trace.Info("Starting diagnostic evidence collection");
                    Trace.Error($"Docker exec failed with exit code: {exitCode?.ToString() ?? "null"}");
                    Trace.Error($"Failed command: docker {dockerArgs}");

                    Trace.Info("Phase 1: Collecting diagnostic evidence");

                    Trace.Info("Checking container state and lifecycle");
                    var containerState = await GetContainerState(dockerManager, containerId, Trace);

                    // Get containerOS from the collected state
                    string containerOS = containerState?.OS ?? "linux";

                    Trace.Info("Checking resource constraints and OOM status");
                    var resourceState = await GetResourceState(dockerManager, containerId, Trace);

                    Trace.Info("Retrieving container logs from time of failure");
                    await GetContainerLogs(dockerManager, containerId, Trace, resourceState);

                    Trace.Info("Checking Docker daemon health");
                    await DiagnoseDockerDaemon(dockerManager, Trace);

                    if (containerState != null && containerState.IsRunning)
                    {
                        Trace.Info("Checking command and environment availability");
                        await DiagnoseCommandIssues(dockerManager, containerId, Trace, containerOS);
                    }
                    else
                    {
                        Trace.Info("Skipping command availability check because container is not running");
                    }

                    Trace.Info("Phase 2: Analyzing evidence to determine root cause");
                    AnalyzeAndReportRootCause(exitCode, containerState, resourceState, containerOS, dockerArgs, Trace);
                }
            }
            catch (Exception ex)
            {
                Trace.Error($"Diagnostic collection failed: {ex.ToString()}");
            }
        }

        /// <summary>
        /// Collects basic system information
        /// </summary>
        private async Task CollectBasicSystemInfo(ITraceWriter trace)
        {
            try
            {
                trace.Info($"Platform: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
                trace.Info($"Architecture: {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}");
                trace.Info($"Process Architecture: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");

                if (PlatformUtil.RunningOnWindows)
                {
                    await ExecuteDiagnosticCommand("systeminfo", "", trace, "System Information", maxLines: 5);
                }
                else
                {
                    await ExecuteDiagnosticCommand("uname", "-a", trace, "System Information");
                }
            }
            catch (Exception ex)
            {
                trace.Info($"Basic system info collection failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Diagnoses command-related issues (Exit Code 127: Command Not Found)
        /// </summary>
        private async Task DiagnoseCommandIssues(IDockerCommandManager dockerManager, string containerId, ITraceWriter trace, string containerOS)
        {
            trace.Info("Checking PATH and available commands...");
            if (containerOS == "windows")
            {
                // Check PATH and common commands in Windows container
                await ExecuteDiagnosticCommand(dockerManager.DockerPath,
                    $"exec {containerId} cmd /c \"echo PATH=%PATH% & where node 2^>nul ^|^| echo node not found & where npm 2^>nul ^|^| echo npm not found & where powershell 2^>nul ^|^| echo powershell not found\"",
                    trace, "Windows PATH and Command Availability");
            }
            else
            {
                // Check PATH and common commands in Linux container
                await ExecuteDiagnosticCommand(dockerManager.DockerPath,
                    $"exec {containerId} sh -c \"echo PATH=$PATH; which node || echo 'node: not found'; which npm || echo 'npm: not found'; which bash || echo 'bash: not found'; which sh || echo 'sh: found'\"",
                    trace, "Linux PATH and Command Availability", maxLines: 10);
            }
        }

        /// <summary>
        /// Diagnoses Docker daemon issues
        /// </summary>
        private async Task DiagnoseDockerDaemon(IDockerCommandManager dockerManager, ITraceWriter trace)
        {
            // ExecuteDiagnosticCommand handles all exceptions internally, so no try-catch needed here
            trace.Info("Testing Docker daemon connectivity...");
            await ExecuteDiagnosticCommand(dockerManager.DockerPath, "version", trace, "Docker Version (Client & Server)", maxLines: 15);

            // Check if daemon is responsive
            await ExecuteDiagnosticCommand(dockerManager.DockerPath, "info --format \"ServerVersion={{.ServerVersion}} ContainersRunning={{.ContainersRunning}} MemTotal={{.MemTotal}}\"", trace, "Docker Daemon Status", maxLines: 15);

            // Check docker system resources
            await ExecuteDiagnosticCommand(dockerManager.DockerPath, "system df", trace, "Docker System Disk Usage", maxLines: 15);
        }

        /// <summary>
        /// Executes a diagnostic command and logs the result
        /// </summary>
        private async Task ExecuteDiagnosticCommand(string command, string args, ITraceWriter trace, string description, int maxLines = 15)
        {
            try
            {
                using var processInvoker = HostContext.CreateService<IProcessInvoker>();
                var output = new List<string>();

                processInvoker.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        output.Add(e.Data);
                };

                processInvoker.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        output.Add($"ERROR: {e.Data}");
                };

                using var timeoutTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                var exitCode = await processInvoker.ExecuteAsync(
                    workingDirectory: HostContext.GetDirectory(WellKnownDirectory.Work),
                    fileName: command,
                    arguments: args,
                    environment: null,
                    requireExitCodeZero: false,
                    outputEncoding: null,
                    cancellationToken: timeoutTokenSource.Token);

                trace.Info($"{description}: Exit Code {exitCode}");
                foreach (var line in output.Take(maxLines))
                {
                    trace.Info($"  {line}");
                }

                if (output.Count > maxLines)
                {
                    trace.Info($"  ... ({output.Count - maxLines} more lines truncated)");
                }
            }
            catch (Exception ex)
            {
                trace.Info($"Diagnostic command '{command} {args}' failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Collects comprehensive container state from docker inspect
        /// </summary>
        private async Task<ContainerState> GetContainerState(IDockerCommandManager dockerManager, string containerId, ITraceWriter trace)
        {
            var state = new ContainerState();

            try
            {
                using var processInvoker = HostContext.CreateService<IProcessInvoker>();
                var output = new List<string>();

                processInvoker.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        output.Add(e.Data);
                };

                processInvoker.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        trace.Info($"Docker inspect stderr: {e.Data}");
                };

                // Get comprehensive container state in one call
                using var timeoutTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                var exitCode = await processInvoker.ExecuteAsync(
                    workingDirectory: HostContext.GetDirectory(WellKnownDirectory.Work),
                    fileName: dockerManager.DockerPath,
                    arguments: $"inspect {containerId} --format \"Running={{{{.State.Running}}}}|Status={{{{.State.Status}}}}|ExitCode={{{{.State.ExitCode}}}}|Error={{{{.State.Error}}}}|StartedAt={{{{.State.StartedAt}}}}|FinishedAt={{{{.State.FinishedAt}}}}|OS={{{{.Platform}}}}\"",
                    environment: null,
                    requireExitCodeZero: false,
                    outputEncoding: null,
                    cancellationToken: timeoutTokenSource.Token);

                if (exitCode == 0 && output.Count > 0)
                {
                    var parts = output[0].Split('|');
                    foreach (var part in parts)
                    {
                        var kv = part.Split(new[] { '=' }, 2);
                        if (kv.Length == 2)
                        {
                            switch (kv[0])
                            {
                                case "Running":
                                    state.IsRunning = kv[1].Equals("true", StringComparison.OrdinalIgnoreCase);
                                    break;
                                case "Status":
                                    state.Status = kv[1];
                                    break;
                                case "ExitCode":
                                    if (int.TryParse(kv[1], out var code))
                                        state.ExitCode = code;
                                    break;
                                case "Error":
                                    state.Error = kv[1];
                                    break;
                                case "OS":
                                    state.OS = kv[1].Contains("windows", StringComparison.OrdinalIgnoreCase) ? "windows" : "linux";
                                    break;
                                default:
                                    // Ignore unexpected keys from docker inspect
                                    break;
                            }
                        }
                    }

                    trace.Info($"Container state collected: Running={state.IsRunning}, Status={state.Status}, ExitCode={state.ExitCode}, OS={state.OS}");
                    if (!string.IsNullOrEmpty(state.Error))
                    {
                        trace.Info($"Container error message: {state.Error}");
                    }
                }
            }
            catch (Exception ex)
            {
                trace.Info($"Failed to get container state: {ex.Message}");
            }

            return state;
        }

        /// <summary>
        /// Collects resource state including OOM status and memory limits
        /// </summary>
        private async Task<ResourceState> GetResourceState(IDockerCommandManager dockerManager, string containerId, ITraceWriter trace)
        {
            var state = new ResourceState();

            try
            {
                using var processInvoker = HostContext.CreateService<IProcessInvoker>();
                var output = new List<string>();

                processInvoker.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        output.Add(e.Data);
                };

                processInvoker.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        trace.Info($"Docker inspect stderr: {e.Data}");
                    }
                };

                // Check OOM, memory limits, and logging configuration
                using var timeoutTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                var exitCode = await processInvoker.ExecuteAsync(
                    workingDirectory: HostContext.GetDirectory(WellKnownDirectory.Work),
                    fileName: dockerManager.DockerPath,
                    arguments: $"inspect {containerId} --format \"OOMKilled={{{{.State.OOMKilled}}}}|MemoryLimit={{{{.HostConfig.Memory}}}}|LogDriver={{{{.HostConfig.LogConfig.Type}}}}|LogPath={{{{.LogPath}}}}\"",
                    environment: null,
                    requireExitCodeZero: false,
                    outputEncoding: null,
                    cancellationToken: timeoutTokenSource.Token);

                if (exitCode == 0 && output.Count > 0)
                {
                    var parts = output[0].Split('|');
                    foreach (var part in parts)
                    {
                        var kv = part.Split(new[] { '=' }, 2);
                        if (kv.Length == 2)
                        {
                            switch (kv[0])
                            {
                                case "OOMKilled":
                                    state.OOMKilled = kv[1].Equals("true", StringComparison.OrdinalIgnoreCase);
                                    break;
                                case "MemoryLimit":
                                    if (long.TryParse(kv[1], out var limit))
                                        state.MemoryLimit = limit;
                                    break;
                                case "LogDriver":
                                    state.LogDriver = kv[1];
                                    break;
                                case "LogPath":
                                    state.LogPath = kv[1];
                                    break;
                                default:
                                    // Ignore unexpected keys from docker inspect
                                    break;
                            }
                        }
                    }

                    var memoryMB = state.MemoryLimit > 0 ? $"{state.MemoryLimit / 1024 / 1024} MB" : "unlimited";
                    trace.Info($"Resource state collected: OOMKilled={state.OOMKilled}, MemoryLimit={memoryMB}, LogDriver={state.LogDriver}");
                }
            }
            catch (Exception ex)
            {
                trace.Info($"Failed to get resource state: {ex.Message}");
            }

            return state;
        }

        /// <summary>
        /// Retrieves container logs from time of failure
        /// </summary>
        private async Task GetContainerLogs(IDockerCommandManager dockerManager, string containerId, ITraceWriter trace, ResourceState resourceState)
        {
            try
            {
                trace.Info($"Log Configuration: Driver={resourceState?.LogDriver ?? "unknown"}, Path={resourceState?.LogPath ?? "unknown"}");

                // Get last 50 lines of logs with timestamps
                using var processInvoker = HostContext.CreateService<IProcessInvoker>();
                var output = new List<string>();
                var hasLogs = false;

                processInvoker.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        output.Add(e.Data);
                        hasLogs = true;
                    }
                };

                processInvoker.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        trace.Info($"Docker logs stderr: {e.Data}");
                };

                using var timeoutTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                var exitCode = await processInvoker.ExecuteAsync(
                    workingDirectory: HostContext.GetDirectory(WellKnownDirectory.Work),
                    fileName: dockerManager.DockerPath,
                    arguments: $"logs --tail 50 --timestamps {containerId}",
                    environment: null,
                    requireExitCodeZero: false,
                    outputEncoding: null,
                    cancellationToken: timeoutTokenSource.Token);

                if (hasLogs)
                {
                    trace.Info("Container logs retrieved (last 50 lines):");
                    foreach (var line in output.Take(50))
                    {
                        trace.Info($"  {line}");
                    }
                }
                else
                {
                    trace.Info("Container logs are empty. No output was written to stdout or stderr.");
                    trace.Info("Possible reasons: Application did not write to stdout/stderr, immediate crash, or output buffering.");
                }
            }
            catch (Exception ex)
            {
                trace.Info($"Failed to retrieve container logs: {ex.Message}");
            }
        }

        /// <summary>
        /// Analyzes collected evidence and reports root cause
        /// Uses evidence-based analysis rather than exit code matching
        /// </summary>
        private void AnalyzeAndReportRootCause(int? exitCode, ContainerState containerState, ResourceState resourceState, string containerOS, string dockerArgs, ITraceWriter trace)
        {
            //  OOM killed - Most definitive evidence
            if (resourceState != null && resourceState.OOMKilled)
            {
                trace.Info("ROOT CAUSE: OUT OF MEMORY");
                trace.Info($"  OOMKilled flag: TRUE ");
                trace.Info($"  Memory limit: {resourceState.MemoryLimit / 1024 / 1024} MB");
                trace.Info($"  Docker exec exit code: {exitCode}");
                trace.Info($"  Container OS: {containerOS}");
                trace.Info("  The container exceeded its memory limit and was terminated by the system OOM (Out-Of-Memory) killer. Exit codes vary by OS:");
                return;
            }

            // Container not running
            if (containerState != null && !containerState.IsRunning)
            {
                trace.Info("ROOT CAUSE: CONTAINER NOT RUNNING / EXITED");
                trace.Info($"  Container running: FALSE");
                trace.Info($"  Container status: {containerState.Status}");
                trace.Info($"  Container exit code: {containerState.ExitCode}");
                trace.Info($"  Docker exec exit code: {exitCode}");

                if (!string.IsNullOrEmpty(containerState.Error))
                {
                    trace.Info($"  Container error: {containerState.Error}");
                }
                return;
            }

            if (!exitCode.HasValue)
            {
                trace.Info("LIKELY CAUSE: PROCESS CANCELLATION OR TIMEOUT");
                trace.Info($"  Exit code: NULL (no exit code returned)");
                trace.Info($"  Container running: {containerState?.IsRunning.ToString() ?? "unknown"}");
                trace.Info($"  Container status: {containerState?.Status ?? "unknown"}");
                return;
            }

            // Container is running but exec failed
            if (containerState != null && containerState.IsRunning)
            {
                // Linux: Use exit codes for diagnosis
                if (containerOS == "linux")
                {
                    trace.Info($"  Container running: TRUE");
                    trace.Info($"  Container status: {containerState.Status}");

                    if (exitCode == 127)
                    {
                        trace.Info("Likely Cause: COMMAND NOT FOUND");
                        trace.Info(" Exit code 127 typically indicates the command or executable was not found in the container.");
                    }
                    else if (exitCode == 137)
                    {
                        trace.Info("Likely Cause: PROCESS KILLED (SIGKILL)");
                        trace.Info("  Exit code 137 indicates process was killed with SIGKILL. Common causes: OOM killer, manual kill, or timeout");
                    }
                    else if (exitCode == 126)
                    {
                        trace.Info("Likely Cause: PERMISSION DENIED");
                        trace.Info("  Exit code 126 indicates permission denied.");
                    }
                    else
                    {
                        trace.Info("Likely Cause: EXECUTION FAILURE");
                        trace.Info($"  Exit code {exitCode} indicates the command failed.");
                    }
                }
                else // Windows
                {
                    // Windows containers lack reliable diagnostic signals for automatic root cause analysis:
                    // 1. Exit codes are non-standard: The same failure (e.g., OOM) produces different codes
                    //    across Windows versions (-532462766, -2146232797, -1073741819, etc.)
                    // 2. OOMKilled flag unreliable: Docker on Windows doesn't reliably detect or report OOM events
                    //    because Windows Job Objects don't expose the same memory signals as Linux cgroups
                    // 3. Process-specific codes: .NET (COMException codes), Node.js (V8 codes), and native Win32
                    //    processes all use different exit code schemes
                    // 4. No standardized signals: Unlike Linux (SIGKILL=137, SIGTERM=143), Windows lacks
                    //    consistent process termination signals visible to Docker
                    trace.Info("Collected diagnostic summary:");
                    trace.Info($"  Docker exec exit code: {exitCode?.ToString() ?? "null"}");
                    trace.Info($"  Container running: {containerState?.IsRunning.ToString() ?? "unknown"}");
                    trace.Info($"  Container status: {containerState?.Status ?? "unknown"}");
                    trace.Info($"  Container exit code: {containerState?.ExitCode.ToString() ?? "unknown"}");
                    trace.Info($"  Container OS: {containerOS}");
                    trace.Info($"  Failed command: docker {dockerArgs}");
                }
                return;
            }

            // Fallback: Unable to determine specific cause
            trace.Info("UNABLE TO DETERMINE SPECIFIC CAUSE");
            trace.Info("Collected diagnostic summary:");
            trace.Info($"  Docker exec exit code: {exitCode?.ToString() ?? "null"}");
            trace.Info($"  Container running: {containerState?.IsRunning.ToString() ?? "unknown"}");
            trace.Info($"  Container status: {containerState?.Status ?? "unknown"}");
            trace.Info($"  Container exit code: {containerState?.ExitCode.ToString() ?? "unknown"}");
            trace.Info($"  Container OS: {containerOS}");
            trace.Info($"  OOM killed: {resourceState?.OOMKilled.ToString() ?? "unknown"}");
            trace.Info($"  Failed command: docker {dockerArgs}");
        }

        /// <summary>
        /// Container state information collected from docker inspect
        /// </summary>
        private class ContainerState
        {
            public bool IsRunning { get; set; }
            public string Status { get; set; }  // running/exited/dead/paused
            public int ExitCode { get; set; }
            public string Error { get; set; }
            public DateTime? StartedAt { get; set; }
            public DateTime? FinishedAt { get; set; }
            public string OS { get; set; }  // windows/linux
        }

        /// <summary>
        /// Resource state information for OOM and memory diagnostics
        /// </summary>
        private class ResourceState
        {
            public bool OOMKilled { get; set; }
            public long MemoryLimit { get; set; }
            public string LogDriver { get; set; }
            public string LogPath { get; set; }
        }
    }
}
