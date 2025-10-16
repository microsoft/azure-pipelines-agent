// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.Framework.Common;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker.Container;
using Microsoft.VisualStudio.Services.WebApi;
using Newtonsoft.Json;

namespace Microsoft.VisualStudio.Services.Agent.Worker.Handlers
{
    public interface IStepHost : IAgentService
    {
        event EventHandler<ProcessDataReceivedEventArgs> OutputDataReceived;
        event EventHandler<ProcessDataReceivedEventArgs> ErrorDataReceived;

        string ResolvePathForStepHost(string path);

        Task<int> ExecuteAsync(string workingDirectory,
                               string fileName,
                               string arguments,
                               IDictionary<string, string> environment,
                               bool requireExitCodeZero,
                               Encoding outputEncoding,
                               bool killProcessOnCancel,
                               bool inheritConsoleHandler,
                               bool continueAfterCancelProcessTreeKillAttempt,
                               TimeSpan sigintTimeout,
                               TimeSpan sigtermTimeout,
                               bool useGracefulShutdown,
                               CancellationToken cancellationToken);
    }

    [ServiceLocator(Default = typeof(ContainerStepHost))]
    public interface IContainerStepHost : IStepHost
    {
        ContainerInfo Container { get; set; }
        string PrependPath { get; set; }
    }

    [ServiceLocator(Default = typeof(DefaultStepHost))]
    public interface IDefaultStepHost : IStepHost
    {
    }

    public sealed class DefaultStepHost : AgentService, IDefaultStepHost
    {
        public event EventHandler<ProcessDataReceivedEventArgs> OutputDataReceived;
        public event EventHandler<ProcessDataReceivedEventArgs> ErrorDataReceived;

        public string ResolvePathForStepHost(string path)
        {
            return path;
        }

        public async Task<int> ExecuteAsync(string workingDirectory,
                                            string fileName,
                                            string arguments,
                                            IDictionary<string, string> environment,
                                            bool requireExitCodeZero,
                                            Encoding outputEncoding,
                                            bool killProcessOnCancel,
                                            bool inheritConsoleHandler,
                                            bool continueAfterCancelProcessTreeKillAttempt,
                                            TimeSpan sigintTimeout,
                                            TimeSpan sigtermTimeout,
                                            bool useGracefulShutdown,
                                            CancellationToken cancellationToken)
        {
            using (var processInvoker = HostContext.CreateService<IProcessInvoker>())
            {
                processInvoker.OutputDataReceived += OutputDataReceived;
                processInvoker.ErrorDataReceived += ErrorDataReceived;
                processInvoker.SigintTimeout = sigintTimeout;
                processInvoker.SigtermTimeout = sigtermTimeout;
                processInvoker.TryUseGracefulShutdown = useGracefulShutdown;

                return await processInvoker.ExecuteAsync(workingDirectory: workingDirectory,
                                                         fileName: fileName,
                                                         arguments: arguments,
                                                         environment: environment,
                                                         requireExitCodeZero: requireExitCodeZero,
                                                         outputEncoding: outputEncoding,
                                                         killProcessOnCancel: killProcessOnCancel,
                                                         redirectStandardIn: null,
                                                         inheritConsoleHandler: inheritConsoleHandler,
                                                         continueAfterCancelProcessTreeKillAttempt: continueAfterCancelProcessTreeKillAttempt,
                                                         cancellationToken: cancellationToken);
            }
        }
    }

    public sealed class ContainerStepHost : AgentService, IContainerStepHost
    {
        public ContainerInfo Container { get; set; }
        public string PrependPath { get; set; }
        public event EventHandler<ProcessDataReceivedEventArgs> OutputDataReceived;
        public event EventHandler<ProcessDataReceivedEventArgs> ErrorDataReceived;

        public string ResolvePathForStepHost(string path)
        {
            // make sure container exist.
            ArgUtil.NotNull(Container, nameof(Container));
            ArgUtil.NotNullOrEmpty(Container.ContainerId, nameof(Container.ContainerId));
            ArgUtil.NotNull(path, nameof(path));

            // remove double quotes around the path
            path = path.Trim('\"');

            // try to resolve path inside container if the request path is part of the mount volume
            StringComparison sc = (PlatformUtil.RunningOnWindows)
                                ? StringComparison.OrdinalIgnoreCase
                                : StringComparison.Ordinal;
            if (Container.MountVolumes.Exists(x =>
            {
                if (!string.IsNullOrEmpty(x.SourceVolumePath))
                {
                    return path.StartsWith(x.SourceVolumePath, sc);
                }
                if (!string.IsNullOrEmpty(x.TargetVolumePath))
                {
                    return path.StartsWith(x.TargetVolumePath, sc);
                }
                return false; // this should not happen, but just in case bad data got into MountVolumes, we do not want to throw an exception here
            }))
            {
                return Container.TranslateContainerPathForImageOS(PlatformUtil.HostOS, Container.TranslateToContainerPath(path));
            }
            else
            {
                return path;
            }
        }

        public async Task<int> ExecuteAsync(string workingDirectory,
                                            string fileName,
                                            string arguments,
                                            IDictionary<string, string> environment,
                                            bool requireExitCodeZero,
                                            Encoding outputEncoding,
                                            bool killProcessOnCancel,
                                            bool inheritConsoleHandler,
                                            bool continueAfterCancelProcessTreeKillAttempt,
                                            TimeSpan sigintTimeout,
                                            TimeSpan sigtermTimeout,
                                            bool useGracefulShutdown,
                                            CancellationToken cancellationToken)
        {
            // make sure container exist.
            ArgUtil.NotNull(Container, nameof(Container));
            ArgUtil.NotNullOrEmpty(Container.ContainerId, nameof(Container.ContainerId));

            var dockerManger = HostContext.GetService<IDockerCommandManager>();
            string containerEnginePath = dockerManger.DockerPath;

            ContainerStandardInPayload payload = new ContainerStandardInPayload()
            {
                ExecutionHandler = fileName,
                ExecutionHandlerWorkingDirectory = workingDirectory,
                ExecutionHandlerArguments = arguments,
                ExecutionHandlerEnvironment = environment,
                ExecutionHandlerPrependPath = PrependPath
            };

            // copy the intermediate script (containerHandlerInvoker.js) into Agent_TempDirectory
            // Background:
            //    We rely on environment variables to send task execution information from agent to task execution engine (node/powershell)
            //    Those task execution information will include all the variables and secrets customer has.
            //    The only way to pass environment variables to `docker exec` is through command line arguments, ex: `docker exec -e myenv=myvalue -e mysecert=mysecretvalue ...`
            //    Since command execution may get log into system event log which might cause secret leaking.
            //    We use this intermediate script to read everything from STDIN, then launch the task execution engine (node/powershell) and redirect STDOUT/STDERR

            string tempDir = Path.Combine(HostContext.GetDirectory(WellKnownDirectory.Work), Constants.Path.TempDirectory);
            string targetEntryScript = Path.Combine(tempDir, "containerHandlerInvoker.js");
            HostContext.GetTrace(nameof(ContainerStepHost)).Info($"Copying containerHandlerInvoker.js to {tempDir}");
            File.Copy(Path.Combine(HostContext.GetDirectory(WellKnownDirectory.Bin), "containerHandlerInvoker.js.template"), targetEntryScript, true);

            string entryScript = Container.TranslateContainerPathForImageOS(PlatformUtil.HostOS, Container.TranslateToContainerPath(targetEntryScript));

            string userArgs = "";
            string workingDirectoryParam = "";
            if (!PlatformUtil.RunningOnWindows)
            {
                userArgs = $"-u {Container.CurrentUserId}";
                if (Container.CurrentUserName == "root")
                {
                    workingDirectoryParam = $" -w /root";
                }
                else
                {
                    workingDirectoryParam = $" -w /home/{Container.CurrentUserName}";
                }
            }

            string containerExecutionArgs = $"exec -i {userArgs} {workingDirectoryParam} {Container.ContainerId} {Container.ResultNodePath} {entryScript}";

            using (var processInvoker = HostContext.CreateService<IProcessInvoker>())
            {
                processInvoker.OutputDataReceived += OutputDataReceived;
                processInvoker.ErrorDataReceived += ErrorDataReceived;
                outputEncoding = null; // Let .NET choose the default.

                processInvoker.SigintTimeout = sigintTimeout;
                processInvoker.SigtermTimeout = sigtermTimeout;
                processInvoker.TryUseGracefulShutdown = useGracefulShutdown;

                if (PlatformUtil.RunningOnWindows)
                {
                    // It appears that node.exe outputs UTF8 when not in TTY mode.
                    outputEncoding = Encoding.UTF8;
                }

                using var redirectStandardIn = new InputQueue<string>();
                var payloadJson = JsonUtility.ToString(payload);
                redirectStandardIn.Enqueue(payloadJson);
                HostContext.GetTrace(nameof(ContainerStepHost)).Info($"Payload: {payloadJson}");
                
                int exitCode = 0;
                try
                {
                    exitCode = await processInvoker.ExecuteAsync(workingDirectory: HostContext.GetDirectory(WellKnownDirectory.Work),
                                                             fileName: containerEnginePath,
                                                             arguments: containerExecutionArgs,
                                                             environment: null,
                                                             requireExitCodeZero: requireExitCodeZero,
                                                             outputEncoding: outputEncoding,
                                                             killProcessOnCancel: killProcessOnCancel,
                                                             redirectStandardIn: redirectStandardIn,
                                                             inheritConsoleHandler: inheritConsoleHandler,
                                                             continueAfterCancelProcessTreeKillAttempt: continueAfterCancelProcessTreeKillAttempt,
                                                             cancellationToken: cancellationToken);
                }
                catch (Exception ex)
                {
                    // Collect comprehensive diagnostics when docker exec fails
                    HostContext.GetTrace(nameof(ContainerStepHost)).Info("=== DIAGNOSTICS: Exception caught, running diagnostics ===");
                    await CollectDockerExecFailureDiagnostics(ex, containerEnginePath, containerExecutionArgs, Container.ContainerId);
                    throw; // Re-throw the original exception
                }
                return exitCode;
            }
        }

        /// <summary>
        /// Collects comprehensive diagnostics when docker exec command fails
        /// </summary>
        private async Task CollectDockerExecFailureDiagnostics(Exception originalException, string dockerPath, string dockerArgs, string containerId)
        {
            var trace = HostContext.GetTrace(nameof(ContainerStepHost));
            var dockerManager = HostContext.GetService<IDockerCommandManager>();
            
            try
            {
                using (trace.EnteringWithDuration())
                {
                    trace.Error("Docker exec failure diagnostics started");
                    trace.Error($"Exception: {originalException.GetType().Name}: {originalException.Message}");
                    trace.Error($"Failed command: {dockerPath} {dockerArgs}");
                    trace.Info($"Container ID: {containerId}");
                    
                    // Extract exit code from exception
                    int? exitCode = null;
                    if (originalException is ProcessExitCodeException processEx)
                    {
                        exitCode = processEx.ExitCode;
                        trace.Error($"Exit code: {exitCode}");
                    }
                    
                    // Collect system information
                    trace.Info("Collecting system information");
                    await CollectBasicSystemInfo(trace);
                    
                    // Run diagnostics (this collects container state internally)
                    await RunDiagnostics(exitCode, dockerManager, containerId, dockerArgs);
                    
                    trace.Error("Docker exec failure diagnostics completed");
                }
            }
            catch (Exception diagEx)
            {
                trace.Error($"Diagnostic collection failed: {diagEx.GetType().Name}: {diagEx.Message}");
            }
        }
        /// <summary>
        /// Evidence-based diagnostics - collects all evidence first, then analyzes to determine root cause
        /// </summary>
        private async Task RunDiagnostics(int? exitCode, IDockerCommandManager dockerManager, string containerId, string dockerArgs)
        {
            var trace = HostContext.GetTrace(nameof(ContainerStepHost));
            
            try
            {
                using (trace.EnteringWithDuration())
                {
                    trace.Info("Starting diagnostic evidence collection");
                    trace.Error($"Docker exec failed with exit code: {exitCode?.ToString() ?? "null"}");
                    trace.Error($"Failed command: docker {dockerArgs}");
                    
                    trace.Info("Phase 1: Collecting diagnostic evidence");
                    
                    trace.Info("Checking container state and lifecycle");
                    var containerState = await GetContainerState(dockerManager, containerId, trace);
                    
                    // Get containerOS from the collected state
                    string containerOS = containerState?.OS ?? "linux";

                    trace.Info("Checking resource constraints and OOM status");
                    var resourceState = await GetResourceState(dockerManager, containerId, trace);

                    trace.Info("Retrieving container logs from time of failure");
                    await GetContainerLogs(dockerManager, containerId, trace, resourceState);

                    trace.Info("Checking Docker daemon health");
                    await DiagnoseDockerDaemon(dockerManager, trace);

                    if (containerState != null && containerState.IsRunning)
                    {
                        trace.Info("Checking command and environment availability");
                        await DiagnoseCommandIssues(dockerManager, containerId, trace, containerOS);
                    }
                    else
                    {
                        trace.Info("Skipping command availability check because container is not running");
                    }

                    trace.Info("Phase 2: Analyzing evidence to determine root cause");
                    AnalyzeAndReportRootCause(exitCode, containerState, resourceState, containerOS, dockerArgs, trace);
                }
            }
            catch (Exception ex)
            {
                trace.Info($"Diagnostic collection failed: {ex.Message}");
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

                // Basic memory info
                var process = System.Diagnostics.Process.GetCurrentProcess();
                trace.Info($"Agent Memory Usage: {process.WorkingSet64 / 1024 / 1024} MB");
                        
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
                
                var exitCode = await processInvoker.ExecuteAsync(
                    workingDirectory: HostContext.GetDirectory(WellKnownDirectory.Work),
                    fileName: command,
                    arguments: args,
                    environment: null,
                    requireExitCodeZero: false,
                    outputEncoding: null,
                    cancellationToken: CancellationToken.None);
                
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
                var exitCode = await processInvoker.ExecuteAsync(
                    workingDirectory: HostContext.GetDirectory(WellKnownDirectory.Work),
                    fileName: dockerManager.DockerPath,
                    arguments: $"inspect {containerId} --format \"Running={{{{.State.Running}}}}|Status={{{{.State.Status}}}}|ExitCode={{{{.State.ExitCode}}}}|Error={{{{.State.Error}}}}|StartedAt={{{{.State.StartedAt}}}}|FinishedAt={{{{.State.FinishedAt}}}}|OS={{{{.Platform}}}}\"",
                    environment: null,
                    requireExitCodeZero: false,
                    outputEncoding: null,
                    cancellationToken: CancellationToken.None);
                
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
                var exitCode = await processInvoker.ExecuteAsync(
                    workingDirectory: HostContext.GetDirectory(WellKnownDirectory.Work),
                    fileName: dockerManager.DockerPath,
                    arguments: $"inspect {containerId} --format \"OOMKilled={{{{.State.OOMKilled}}}}|MemoryLimit={{{{.HostConfig.Memory}}}}|LogDriver={{{{.HostConfig.LogConfig.Type}}}}|LogPath={{{{.LogPath}}}}\"",
                    environment: null,
                    requireExitCodeZero: false,
                    outputEncoding: null,
                    cancellationToken: CancellationToken.None);
                
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
                
                var exitCode = await processInvoker.ExecuteAsync(
                    workingDirectory: HostContext.GetDirectory(WellKnownDirectory.Work),
                    fileName: dockerManager.DockerPath,
                    arguments: $"logs --tail 50 --timestamps {containerId}",
                    environment: null,
                    requireExitCodeZero: false,
                    outputEncoding: null,
                    cancellationToken: CancellationToken.None);
                
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
        
        private class ContainerStandardInPayload
        {
            [JsonProperty("handler")]
            public String ExecutionHandler { get; set; }

            [JsonProperty("args")]
            public String ExecutionHandlerArguments { get; set; }

            [JsonProperty("workDir")]
            public String ExecutionHandlerWorkingDirectory { get; set; }

            [JsonProperty("environment")]
            public IDictionary<string, string> ExecutionHandlerEnvironment { get; set; }

            [JsonProperty("prependPath")]
            public string ExecutionHandlerPrependPath { get; set; }
        }
    }
}
