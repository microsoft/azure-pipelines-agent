// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using System;
using System.Collections.Generic;
using System.IO;
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

            // Log container execution context
            Trace.Info($"Container ID: {Container.ContainerId}");
            Trace.Info($"Container image: {Container.ContainerImage}");
            Trace.Info($"Working directory: {workingDirectory}");
            Trace.Info($"Command: {fileName} {arguments}");

            var dockerManger = HostContext.GetService<IDockerCommandManager>();
            string containerEnginePath = dockerManger.DockerPath;
            Trace.Info($"Container engine path: {containerEnginePath}");
            if (Container.MountVolumes?.Count > 0)
            {
                Trace.Info($"Container mount volumes: {Container.MountVolumes.Count}");
                foreach (var volume in Container.MountVolumes)
                {
                    Trace.Info($"  Volume: {volume.SourceVolumePath} -> {volume.TargetVolumePath} (ReadOnly: {volume.ReadOnly})");
                }
            }

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
            Trace.Info($"Docker execution arguments: {containerExecutionArgs}");

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
                    using (Trace.EnteringWithDuration("DockerTaskExecution"))
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
                    
                    return exitCode;
                }
                catch (ProcessExitCodeException pex)
                {
                    exitCode = pex.ExitCode;
                    LogContainerTaskFailure(exitCode, pex, containerExecutionArgs);
                    throw;
                }
                catch (OperationCanceledException)
                {
                    // Container task was cancelled
                    Trace.Info("Container task execution was cancelled - check for timeout or manual cancellation");
                    Trace.Info($"Container context: {Container?.ContainerId ?? "Unknown"} ({Container?.ContainerImage ?? "Unknown"})");
                    throw;
                }
                catch (Exception ex)
                {
                    // General container execution failure
                    LogContainerTaskFailure(-1, ex, containerExecutionArgs);
                    throw;
                }
            }
        }

        /// <summary>
        /// Task 2: Enhanced Task Handler Failure Logging
        /// Provides container-specific error messages and exit code analysis
        /// </summary>
        private void LogContainerTaskFailure(int exitCode, Exception exception, string containerExecutionArgs)
        {
            try
            {
                // Log 1: Container context information
                Trace.Error($"Container execution failed - Container: {Container?.ContainerId ?? "Unknown"} (Image: {Container?.ContainerImage ?? "Unknown"})");
                
                // Log 2: Failure details
                Trace.Error($"Exit code: {exitCode}");
                Trace.Error($"Exception: {exception.GetType().Name} - {exception.Message}");
                
                // Provide exit code analysis
                var exitCodeGuidance = GetContainerExitCodeGuidance(exitCode);
                if (!string.IsNullOrEmpty(exitCodeGuidance))
                {
                    Trace.Error($"Exit code guidance: {exitCodeGuidance}");
                }
                
                Trace.Error($"Failed docker command: {containerExecutionArgs}");
            }
            catch (Exception ex)
            {
                Trace.Warning($"Failed to log container failure details: {ex.Message}");
            }
        }

        /// <summary>
        /// Provides human-readable guidance for container exit codes
        /// </summary>
        private string GetContainerExitCodeGuidance(int exitCode)
        {
            return exitCode switch
            {
                0 => "Success",
                125 => "Docker daemon error - Container failed to start, verify image and docker configuration",
                126 => "Container command not executable - Check file permissions or command path",
                127 => "Container command not found - Verify command exists in container PATH",
                130 => "Process interrupted (SIGINT) - Task was cancelled",
                137 => "Process killed (SIGKILL) - Possible out of memory or timeout",
                139 => "Segmentation fault (SIGSEGV) - Application crashed",
                143 => "Process terminated (SIGTERM) - Graceful shutdown",
                _ when exitCode > 128 && exitCode < 256 => $"Process terminated by signal {exitCode - 128}",
                _ => $"Unknown infrastructure failure - exit code {exitCode}."
            };
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
