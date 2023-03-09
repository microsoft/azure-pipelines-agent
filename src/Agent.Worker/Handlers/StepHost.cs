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
                                            CancellationToken cancellationToken)
        {
            using (var processInvoker = HostContext.CreateService<IProcessInvoker>())
            {
                processInvoker.OutputDataReceived += OutputDataReceived;
                processInvoker.ErrorDataReceived += ErrorDataReceived;

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

            string node;
            if (!string.IsNullOrEmpty(Container.CustomNodePath))
            {
                node = Container.CustomNodePath;
            }
            else
            {
                node = Container.TranslateToContainerPath(Path.Combine(HostContext.GetDirectory(WellKnownDirectory.Externals), "node", "bin", $"node{IOUtil.ExeExtension}"));
            }

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

            string containerExecutionArgs = $"exec -i {userArgs} {workingDirectoryParam} {Container.ContainerId} {node} {entryScript}";

            using (var processInvoker = HostContext.CreateService<IProcessInvoker>())
            {
                processInvoker.OutputDataReceived += OutputDataReceived;
                processInvoker.ErrorDataReceived += ErrorDataReceived;
                outputEncoding = null; // Let .NET choose the default.

                if (PlatformUtil.RunningOnWindows)
                {
                    // It appears that node.exe outputs UTF8 when not in TTY mode.
                    outputEncoding = Encoding.UTF8;
                }

                using var redirectStandardIn = new InputQueue<string>();
                var payloadJson = JsonUtility.ToString(payload);
                redirectStandardIn.Enqueue(payloadJson);
                HostContext.GetTrace(nameof(ContainerStepHost)).Info($"Payload: {payloadJson}");
                return await processInvoker.ExecuteAsync(workingDirectory: HostContext.GetDirectory(WellKnownDirectory.Work),
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