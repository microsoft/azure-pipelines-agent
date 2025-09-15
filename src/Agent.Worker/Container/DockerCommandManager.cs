// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using Agent.Sdk.Knob;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.Framework.Common;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Microsoft.VisualStudio.Services.Agent.Worker.Container
{
    [ServiceLocator(Default = typeof(DockerCommandManager))]
    public interface IDockerCommandManager : IAgentService
    {
        string DockerPath { get; }
        string DockerInstanceLabel { get; }
        Task<DockerVersion> DockerVersion(IExecutionContext context);
        Task<int> DockerLogin(IExecutionContext context, string server, string username, string password);
        Task<int> DockerLogout(IExecutionContext context, string server);
        Task<int> DockerPull(IExecutionContext context, string image);
        Task<string> DockerCreate(IExecutionContext context, ContainerInfo container);
        Task<int> DockerStart(IExecutionContext context, string containerId);
        Task<int> DockerLogs(IExecutionContext context, string containerId);
        Task<List<string>> GetDockerLogs(IExecutionContext context, string containerId);
        Task<List<string>> DockerPS(IExecutionContext context, string options);
        Task<int> DockerRemove(IExecutionContext context, string containerId);
        Task<int> DockerNetworkCreate(IExecutionContext context, string network);
        Task<int> DockerNetworkRemove(IExecutionContext context, string network);
        Task<int> DockerNetworkPrune(IExecutionContext context);
        Task<int> DockerExec(IExecutionContext context, string containerId, string options, string command);
        Task<int> DockerExec(IExecutionContext context, string containerId, string options, string command, List<string> outputs);
        Task<string> DockerInspect(IExecutionContext context, string dockerObject, string options);
        Task<List<PortMapping>> DockerPort(IExecutionContext context, string containerId);
        Task<bool> IsContainerRunning(IExecutionContext context, string containerId);
    }

    public class DockerCommandManager : AgentService, IDockerCommandManager
    {
        public string DockerPath { get; private set; }

        public string DockerInstanceLabel { get; private set; }
        private static UtilKnobValueContext _knobContext = UtilKnobValueContext.Instance();

        public override void Initialize(IHostContext hostContext)
        {
            ArgUtil.NotNull(hostContext, nameof(hostContext));

            base.Initialize(hostContext);
            DockerPath = WhichUtil.Which("docker", true, Trace);
            DockerInstanceLabel = IOUtil.GetPathHash(hostContext.GetDirectory(WellKnownDirectory.Root)).Substring(0, 6);
        }

        public async Task<DockerVersion> DockerVersion(IExecutionContext context)
        {
            ArgUtil.NotNull(context, nameof(context));
            var action = new Func<Task<List<string>>>(async () => await ExecuteDockerCommandAsync(context, "version", "--format '{{.Server.APIVersion}}'"));
            const string command = "Docker version";
            string serverVersionStr = (await ExecuteDockerCommandAsyncWithRetries(context, action, command)).FirstOrDefault();
            ArgUtil.NotNullOrEmpty(serverVersionStr, "Docker.Server.Version");
            context.Output($"Docker daemon API version: {serverVersionStr}");

            string clientVersionStr = (await ExecuteDockerCommandAsync(context, "version", "--format '{{.Client.APIVersion}}'")).FirstOrDefault();
            ArgUtil.NotNullOrEmpty(serverVersionStr, "Docker.Client.Version");
            context.Output($"Docker client API version: {clientVersionStr}");

            // we interested about major.minor.patch version
            Regex verRegex = new Regex("\\d+\\.\\d+(\\.\\d+)?", RegexOptions.IgnoreCase);

            Version serverVersion = null;
            var serverVersionMatchResult = verRegex.Match(serverVersionStr);
            if (serverVersionMatchResult.Success && !string.IsNullOrEmpty(serverVersionMatchResult.Value))
            {
                if (!Version.TryParse(serverVersionMatchResult.Value, out serverVersion))
                {
                    serverVersion = null;
                }
            }

            Version clientVersion = null;
            var clientVersionMatchResult = verRegex.Match(serverVersionStr);
            if (clientVersionMatchResult.Success && !string.IsNullOrEmpty(clientVersionMatchResult.Value))
            {
                if (!Version.TryParse(clientVersionMatchResult.Value, out clientVersion))
                {
                    clientVersion = null;
                }
            }

            return new DockerVersion(serverVersion, clientVersion);
        }

        public async Task<int> DockerLogin(IExecutionContext context, string server, string username, string password)
        {
            ArgUtil.NotNull(context, nameof(context));
            ArgUtil.NotNull(server, nameof(server));
            ArgUtil.NotNull(username, nameof(username));
            ArgUtil.NotNull(password, nameof(password));

            var useDockerStdinPasswordOnWindows = AgentKnobs.UseDockerStdinPasswordOnWindows.GetValue(context).AsBoolean();

            var action = new Func<Task<int>>(async () => PlatformUtil.RunningOnWindows && !useDockerStdinPasswordOnWindows
                // Wait for 17.07 to switch using stdin for docker registry password.
                ? await ExecuteDockerCommandAsync(context, "login", $"--username \"{username}\" --password \"{password.Replace("\"", "\\\"")}\" {server}", new List<string>() { password }, context.CancellationToken)
                : await ExecuteDockerCommandAsync(context, "login", $"--username \"{username}\" --password-stdin {server}", new List<string>() { password }, context.CancellationToken)
            );

            const string command = "Docker login";
            return await ExecuteDockerCommandAsyncWithRetries(context, action, command);
        }

        public async Task<int> DockerLogout(IExecutionContext context, string server)
        {
            ArgUtil.NotNull(context, nameof(context));
            ArgUtil.NotNull(server, nameof(server));

            return await ExecuteDockerCommandAsync(context, "logout", $"{server}", context.CancellationToken);
        }

        public async Task<int> DockerPull(IExecutionContext context, string image)
        {
            ArgUtil.NotNull(context, nameof(context));
            ArgUtil.NotNull(image, nameof(image));

            var action = new Func<Task<int>>(async () => await ExecuteDockerCommandAsync(context, "pull", image, context.CancellationToken));
            const string command = "Docker pull";
            return await ExecuteDockerCommandAsyncWithRetries(context, action, command);
        }

        public async Task<string> DockerCreate(IExecutionContext context, ContainerInfo container)
        {
            ArgUtil.NotNull(context, nameof(context));
            ArgUtil.NotNull(container, nameof(container));

            IList<string> dockerOptions = new List<string>();
            // OPTIONS
            dockerOptions.Add($"--name {container.ContainerDisplayName}");
            dockerOptions.Add($"--label {DockerInstanceLabel}");

            if (AgentKnobs.AddDockerInitOption.GetValue(context).AsBoolean())
            {
                dockerOptions.Add("--init");
            }

            if (!string.IsNullOrEmpty(container.ContainerNetwork))
            {
                dockerOptions.Add($"--network {container.ContainerNetwork}");
            }
            if (!string.IsNullOrEmpty(container.ContainerNetworkAlias))
            {
                dockerOptions.Add($"--network-alias {container.ContainerNetworkAlias}");
            }
            foreach (var port in container.UserPortMappings)
            {
                dockerOptions.Add($"-p {port.Value}");
            }
            dockerOptions.Add($"{container.ContainerCreateOptions}");
            foreach (var env in container.ContainerEnvironmentVariables)
            {
                if (String.IsNullOrEmpty(env.Value) && String.IsNullOrEmpty(context?.Variables.Get("_VSTS_DONT_RESOLVE_ENV_FROM_HOST")))
                {
                    // TODO: Remove fallback variable if stable
                    dockerOptions.Add($"-e \"{env.Key}\"");
                }
                else
                {
                    dockerOptions.Add($"-e \"{env.Key}={env.Value.Replace("\"", "\\\"")}\"");
                }
            }
            foreach (var volume in container?.MountVolumes)
            {
                // replace `"` with `\"` and add `"{0}"` to all path.
                String volumeArg;
                String targetVolume = container.TranslateContainerPathForImageOS(PlatformUtil.HostOS, volume.TargetVolumePath).Replace("\"", "\\\"");

                if (String.IsNullOrEmpty(volume.SourceVolumePath))
                {
                    // Anonymous docker volume
                    volumeArg = $"-v \"{targetVolume}\"";
                }
                else
                {
                    // Named Docker volume / host bind mount
                    volumeArg = $"-v \"{volume.SourceVolumePath.Replace("\"", "\\\"")}\":\"{targetVolume}\"";
                }
                if (volume.ReadOnly)
                {
                    volumeArg += ":ro";
                }
                dockerOptions.Add(volumeArg);
            }
            // IMAGE
            dockerOptions.Add($"{container.ContainerImage}");
            // COMMAND
            dockerOptions.Add($"{container.ContainerCommand}");

            var optionsString = string.Join(" ", dockerOptions);
            List<string> outputStrings = await ExecuteDockerCommandAsync(context, "create", optionsString);

            return outputStrings.FirstOrDefault();
        }

        public async Task<int> DockerStart(IExecutionContext context, string containerId)
        {
            ArgUtil.NotNull(context, nameof(context));
            ArgUtil.NotNull(containerId, nameof(containerId));

            if (!AgentKnobs.CheckBeforeRetryDockerStart.GetValue(context).AsBoolean())
            {
                var action = new Func<Task<int>>(async () => await ExecuteDockerCommandAsync(context, "start", containerId, context.CancellationToken));
                const string command = "Docker start";
                return await ExecuteDockerCommandAsyncWithRetries(context, action, command);
            }
            // Use the new helper for start with retries and running-state checks
            return await ExecuteDockerStartWithRetriesAndCheck(context, containerId);
        }

        public async Task<int> DockerRemove(IExecutionContext context, string containerId)
        {
            ArgUtil.NotNull(context, nameof(context));
            ArgUtil.NotNull(containerId, nameof(containerId));

            return await ExecuteDockerCommandAsync(context, "rm", $"--force {containerId}", context.CancellationToken);
        }

        public async Task<int> DockerLogs(IExecutionContext context, string containerId)
        {
            ArgUtil.NotNull(context, nameof(context));
            ArgUtil.NotNull(containerId, nameof(containerId));

            return await ExecuteDockerCommandAsync(context, "logs", $"--details {containerId}", context.CancellationToken);
        }

        public async Task<List<string>> GetDockerLogs(IExecutionContext context, string containerId)
        {
            ArgUtil.NotNull(context, nameof(context));
            ArgUtil.NotNull(containerId, nameof(containerId));

            return await ExecuteDockerCommandAsync(context, "logs", $"--details {containerId}");
        }

        public async Task<List<string>> DockerPS(IExecutionContext context, string options)
        {
            ArgUtil.NotNull(context, nameof(context));

            return await ExecuteDockerCommandAsync(context, "ps", options);
        }

        public async Task<int> DockerNetworkCreate(IExecutionContext context, string network)
        {
            ArgUtil.NotNull(context, nameof(context));
            ArgUtil.NotNull(network, nameof(network));
            var usingWindowsContainers = context.Containers.Where(x => x.ExecutionOS != PlatformUtil.OS.Windows).Count() == 0;
            var networkDrivers = await ExecuteDockerCommandAsync(context, "info", "-f \"{{range .Plugins.Network}}{{println .}}{{end}}\"");
            var valueMTU = AgentKnobs.MTUValueForContainerJobs.GetValue(_knobContext).AsString();
            var driver = AgentKnobs.DockerNetworkCreateDriver.GetValue(context).AsString();
            var additionalNetworCreateOptions = AgentKnobs.DockerAdditionalNetworkOptions.GetValue(context).AsString();
            string optionMTU = "";

            if (!String.IsNullOrEmpty(valueMTU))
            {
                optionMTU = $"-o \"com.docker.network.driver.mtu={valueMTU}\"";
            }

            string options = $"create --label {DockerInstanceLabel} {network} {optionMTU}";

            if (!String.IsNullOrEmpty(driver))
            {
                if (networkDrivers.Contains(driver))
                {
                    options += $" --driver {driver}";
                }
                else
                {
                    string warningMessage = $"Specified '{driver}' driver not found!";
                    Trace.Warning(warningMessage);
                    context.Warning(warningMessage);
                }
            }
            else if (usingWindowsContainers && networkDrivers.Contains("nat"))
            {
                options += $" --driver nat";
            }

            if (!String.IsNullOrEmpty(additionalNetworCreateOptions))
            {
                options += $" {additionalNetworCreateOptions}";
            }

            return await ExecuteDockerCommandAsync(context, "network", options, context.CancellationToken);
        }

        public async Task<int> DockerNetworkRemove(IExecutionContext context, string network)
        {
            ArgUtil.NotNull(context, nameof(context));
            ArgUtil.NotNull(network, nameof(network));

            return await ExecuteDockerCommandAsync(context, "network", $"rm {network}", context.CancellationToken);
        }

        public async Task<int> DockerNetworkPrune(IExecutionContext context)
        {
            ArgUtil.NotNull(context, nameof(context));

            return await ExecuteDockerCommandAsync(context, "network", $"prune --force --filter \"label={DockerInstanceLabel}\"", context.CancellationToken);
        }

        public async Task<int> DockerExec(IExecutionContext context, string containerId, string options, string command)
        {
            ArgUtil.NotNull(context, nameof(context));
            ArgUtil.NotNull(containerId, nameof(containerId));
            ArgUtil.NotNull(options, nameof(options));
            ArgUtil.NotNull(command, nameof(command));

            return await ExecuteDockerCommandAsync(context, "exec", $"{options} {containerId} {command}", context.CancellationToken);
        }

        public async Task<int> DockerExec(IExecutionContext context, string containerId, string options, string command, List<string> output)
        {
            ArgUtil.NotNull(output, nameof(output));

            string arg = $"exec {options} {containerId} {command}".Trim();
            context.Command($"{DockerPath} {arg}");

            object outputLock = new object();
            var processInvoker = HostContext.CreateService<IProcessInvoker>();
            processInvoker.OutputDataReceived += delegate (object sender, ProcessDataReceivedEventArgs message)
            {
                if (!string.IsNullOrEmpty(message.Data))
                {
                    lock (outputLock)
                    {
                        output.Add(message.Data);
                    }
                }
            };

            processInvoker.ErrorDataReceived += delegate (object sender, ProcessDataReceivedEventArgs message)
            {
                if (!string.IsNullOrEmpty(message.Data))
                {
                    lock (outputLock)
                    {
                        output.Add(message.Data);
                    }
                }
            };

            return await processInvoker.ExecuteAsync(
                            workingDirectory: HostContext.GetDirectory(WellKnownDirectory.Work),
                            fileName: DockerPath,
                            arguments: arg,
                            environment: null,
                            requireExitCodeZero: false,
                            outputEncoding: null,
                            cancellationToken: CancellationToken.None);
        }

        public async Task<string> DockerInspect(IExecutionContext context, string dockerObject, string options)
        {
            return (await ExecuteDockerCommandAsync(context, "inspect", $"{options} {dockerObject}")).FirstOrDefault();
        }

        public async Task<List<PortMapping>> DockerPort(IExecutionContext context, string containerId)
        {
            List<string> portMappingLines = await ExecuteDockerCommandAsync(context, "port", containerId);
            return DockerUtil.ParseDockerPort(portMappingLines);
        }

        /// <summary>
        /// Checks if container with specified id is running
        /// </summary>
        /// <param name="context">Current execution context</param>
        /// <param name="containerId">String representing container id</param>
        /// <returns
        /// <c>true</c>, if specified container is running, <c>false</c> otherwise. 
        /// </returns>
        public async Task<bool> IsContainerRunning(IExecutionContext context, string containerId)
        {
            List<string> filteredItems = await DockerPS(context, $"--filter id={containerId}");

            // docker ps function is returning table with containers in Running state.
            // This table is adding to the list line by line. The first string in List is always table header.
            // The second string appeared only if container by specified id was found and in Running state.
            // Therefore, we assume that the container is running if the list contains two elements.
            var isContainerRunning = (filteredItems.Count == 2);

            return isContainerRunning;
        }

        private Task<int> ExecuteDockerCommandAsync(IExecutionContext context, string command, string options, CancellationToken cancellationToken = default(CancellationToken))
        {
            return ExecuteDockerCommandAsync(context, command, options, null, cancellationToken);
        }

        private async Task<int> ExecuteDockerCommandAsync(IExecutionContext context, string command, string options, IList<string> standardIns = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            string arg = $"{command} {options}".Trim();
            context.Command($"{DockerPath} {arg}");

            var processInvoker = HostContext.CreateService<IProcessInvoker>();
            processInvoker.OutputDataReceived += delegate (object sender, ProcessDataReceivedEventArgs message)
            {
                context.Output(message.Data);
            };

            processInvoker.ErrorDataReceived += delegate (object sender, ProcessDataReceivedEventArgs message)
            {
                context.Output(message.Data);
            };

            InputQueue<string> redirectStandardIn = null;
            if (standardIns != null)
            {
                redirectStandardIn = new InputQueue<string>();
                foreach (var input in standardIns)
                {
                    redirectStandardIn.Enqueue(input);
                }
            }

            using (redirectStandardIn)
            {
                return await processInvoker.ExecuteAsync(
                    workingDirectory: HostContext.GetDirectory(WellKnownDirectory.Work),
                    fileName: DockerPath,
                    arguments: arg,
                    environment: null,
                    requireExitCodeZero: false,
                    outputEncoding: null,
                    killProcessOnCancel: false,
                    redirectStandardIn: redirectStandardIn,
                    cancellationToken: cancellationToken);
            }
        }

        private async Task<List<string>> ExecuteDockerCommandAsync(IExecutionContext context, string command, string options)
        {
            string arg = $"{command} {options}".Trim();
            context.Command($"{DockerPath} {arg}");

            List<string> output = new List<string>();
            var processInvoker = HostContext.CreateService<IProcessInvoker>();
            processInvoker.OutputDataReceived += delegate (object sender, ProcessDataReceivedEventArgs message)
            {
                if (!string.IsNullOrEmpty(message.Data))
                {
                    output.Add(message.Data);
                    context.Output(message.Data);
                }
            };

            processInvoker.ErrorDataReceived += delegate (object sender, ProcessDataReceivedEventArgs message)
            {
                if (!string.IsNullOrEmpty(message.Data))
                {
                    context.Output(message.Data);
                }
            };

            await processInvoker.ExecuteAsync(
                            workingDirectory: HostContext.GetDirectory(WellKnownDirectory.Work),
                            fileName: DockerPath,
                            arguments: arg,
                            environment: null,
                            requireExitCodeZero: true,
                            outputEncoding: null,
                            cancellationToken: CancellationToken.None);

            return output;
        }

        private static async Task<int> ExecuteDockerCommandAsyncWithRetries(IExecutionContext context, Func<Task<int>> action, string command)
        {
            bool dockerActionRetries = AgentKnobs.DockerActionRetries.GetValue(context).AsBoolean();
            context.Output($"DockerActionRetries variable value: {dockerActionRetries}");

            int retryCount = 0;
            int exitCode = 0;
            const int maxRetries = 3;
            TimeSpan delayInSeconds = TimeSpan.FromSeconds(10);

            while (retryCount < maxRetries)
            {
                exitCode = await action();

                if (exitCode == 0 || !dockerActionRetries)
                {
                    break;
                }

                context.Warning($"{command} failed with exit code {exitCode}, back off {delayInSeconds} seconds before retry.");
                await Task.Delay(delayInSeconds);
                retryCount++;
            }

            return exitCode;
        }

        private static async Task<List<string>> ExecuteDockerCommandAsyncWithRetries(IExecutionContext context, Func<Task<List<string>>> action, string command)
        {
            bool dockerActionRetries = AgentKnobs.DockerActionRetries.GetValue(context).AsBoolean();
            context.Output($"DockerActionRetries variable value: {dockerActionRetries}");

            int retryCount = 0;
            List<string> output = new List<string>();
            const int maxRetries = 3;
            TimeSpan delayInSeconds = TimeSpan.FromSeconds(10);

            while (retryCount <= maxRetries)
            {
                try
                {
                    output = await action();
                }
                catch (ProcessExitCodeException)
                {
                    if (!dockerActionRetries || retryCount == maxRetries)
                    {
                        throw;
                    }

                    context.Warning($"{command} failed, back off {delayInSeconds} seconds before retry.");
                    await Task.Delay(delayInSeconds);
                }

                retryCount++;

                if (output != null && output.Count != 0)
                {
                    break;
                }
            }

            return output;
        }

        /// <summary>
        /// Executes 'docker start' with retries, checking if the container is already running before each retry.
        /// Returns 0 if the container is running or started successfully, otherwise returns the last exit code.
        /// </summary>
        private async Task<int> ExecuteDockerStartWithRetriesAndCheck(IExecutionContext context, string containerId)
        {
            bool dockerActionRetries = AgentKnobs.DockerActionRetries.GetValue(context).AsBoolean();
            context.Output($"DockerActionRetries variable value: {dockerActionRetries}");

            int retryCount = 0;
            const int maxRetries = 3;
            TimeSpan delayInSeconds = TimeSpan.FromSeconds(10);
            int exitCode = 0;

            while (retryCount < maxRetries)
            {
                // Check if container is already running before attempting to start
                if (await IsContainerRunning(context, containerId))
                {
                    context.Output($"Container {containerId} is running before attempt {retryCount + 1}.");
                    break;
                }

                exitCode = await ExecuteDockerCommandAsync(context, "start", containerId, context.CancellationToken);
                if (exitCode == 0 || !dockerActionRetries)
                {
                    break;
                }

                context.Warning($"Docker start failed with exit code {exitCode}, back off {delayInSeconds} seconds before retry.");
                retryCount++;
                await Task.Delay(delayInSeconds);

            }

            // handle the case where container is already running after retries but exit code is not 0
            if (exitCode != 0 && await IsContainerRunning(context, containerId))
            {
                context.Output($"Container {containerId} is already running after {retryCount} retries. but exit code was {exitCode}.");
                exitCode = 0; // Indicate success
            }
            // If the container is still not running after retries, log a warning
            if (exitCode != 0)
            {
                context.Warning($"Container {containerId} is not running after {retryCount} retries. Last exit code: {exitCode}");
            }
            else
            {
                context.Output($"Container {containerId} started successfully after {retryCount} retries.");
            }
            //return the exit code
            context.Debug($"Docker start completed with exit code {exitCode}.");
            return exitCode;
        }
    }
}
