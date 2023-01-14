// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using Agent.Sdk;
using Agent.Sdk.Knob;
using Azure.Core;
using Azure.Identity;
using Microsoft.TeamFoundation.Work.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker.Container;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    [ServiceLocator(Default = typeof(ContainerOperationProvider))]
    public interface IContainerOperationProvider : IAgentService
    {
        Task StartContainersAsync(IExecutionContext executionContext, object data);
        Task StopContainersAsync(IExecutionContext executionContext, object data);
    }

    public class ContainerOperationProvider : AgentService, IContainerOperationProvider
    {
        private const string _nodeJsPathLabel = "com.azure.dev.pipelines.agent.handler.node.path";
        private IDockerCommandManager _dockerManger;
        private string _containerNetwork;

        public override void Initialize(IHostContext hostContext)
        {
            base.Initialize(hostContext);
            _dockerManger = HostContext.GetService<IDockerCommandManager>();
            _containerNetwork = $"vsts_network_{Guid.NewGuid():N}";
        }

        private string GetContainerNetwork(IExecutionContext executionContext)
        {
            var useHostNetwork = AgentKnobs.DockerNetworkCreateDriver.GetValue(executionContext).AsString() == "host";
            return useHostNetwork ? "host" : _containerNetwork;
        }

        public async Task StartContainersAsync(IExecutionContext executionContext, object data)
        {
            Trace.Entering();
            ArgUtil.NotNull(executionContext, nameof(executionContext));
            List<ContainerInfo> containers = data as List<ContainerInfo>;
            ArgUtil.NotNull(containers, nameof(containers));
            containers = containers.FindAll(c => c != null); // attempt to mitigate issue #11902 filed in azure-pipelines-task repo

            // Check whether we are inside a container.
            // Our container feature requires to map working directory from host to the container.
            // If we are already inside a container, we will not able to find out the real working direcotry path on the host.
            if (PlatformUtil.RunningOnRHEL6)
            {
                // Red Hat and CentOS 6 do not support the container feature
                throw new NotSupportedException(StringUtil.Loc("AgentDoesNotSupportContainerFeatureRhel6"));
            }

            ThrowIfAlreadyInContainer();
            ThrowIfWrongWindowsVersion(executionContext);

            // Check docker client/server version
            DockerVersion dockerVersion = await _dockerManger.DockerVersion(executionContext);
            ArgUtil.NotNull(dockerVersion.ServerVersion, nameof(dockerVersion.ServerVersion));
            ArgUtil.NotNull(dockerVersion.ClientVersion, nameof(dockerVersion.ClientVersion));

            Version requiredDockerEngineAPIVersion = PlatformUtil.RunningOnWindows
                ? new Version(1, 30)  // Docker-EE version 17.6
                : new Version(1, 35); // Docker-CE version 17.12

            if (dockerVersion.ServerVersion < requiredDockerEngineAPIVersion)
            {
                throw new NotSupportedException(StringUtil.Loc("MinRequiredDockerServerVersion", requiredDockerEngineAPIVersion, _dockerManger.DockerPath, dockerVersion.ServerVersion));
            }
            if (dockerVersion.ClientVersion < requiredDockerEngineAPIVersion)
            {
                throw new NotSupportedException(StringUtil.Loc("MinRequiredDockerClientVersion", requiredDockerEngineAPIVersion, _dockerManger.DockerPath, dockerVersion.ClientVersion));
            }

            // Clean up containers left by previous runs
            executionContext.Debug($"Delete stale containers from previous jobs");
            var staleContainers = await _dockerManger.DockerPS(executionContext, $"--all --quiet --no-trunc --filter \"label={_dockerManger.DockerInstanceLabel}\"");
            foreach (var staleContainer in staleContainers)
            {
                int containerRemoveExitCode = await _dockerManger.DockerRemove(executionContext, staleContainer);
                if (containerRemoveExitCode != 0)
                {
                    executionContext.Warning($"Delete stale containers failed, docker rm fail with exit code {containerRemoveExitCode} for container {staleContainer}");
                }
            }

            executionContext.Debug($"Delete stale container networks from previous jobs");
            int networkPruneExitCode = await _dockerManger.DockerNetworkPrune(executionContext);
            if (networkPruneExitCode != 0)
            {
                executionContext.Warning($"Delete stale container networks failed, docker network prune fail with exit code {networkPruneExitCode}");
            }

            // We need to pull the containers first before setting up the network
            foreach (var container in containers)
            {
                await PullContainerAsync(executionContext, container);
            }

            // Create local docker network for this job to avoid port conflict when multiple agents run on same machine.
            // All containers within a job join the same network
            var containerNetwork = GetContainerNetwork(executionContext);
            await CreateContainerNetworkAsync(executionContext, containerNetwork);
            containers.ForEach(container => container.ContainerNetwork = containerNetwork);

            foreach (var container in containers)
            {
                await StartContainerAsync(executionContext, container);
            }

            // Build JSON to expose docker container name mapping to env
            var containerMapping = new JObject();
            foreach (var container in containers)
            {
                var containerInfo = new JObject();
                containerInfo["id"] = container.ContainerId;
                containerMapping[container.ContainerName] = containerInfo;
            }
            executionContext.Variables.Set(Constants.Variables.Agent.ContainerMapping, containerMapping.ToString());

            foreach (var container in containers.Where(c => !c.IsJobContainer))
            {
                await ContainerHealthcheck(executionContext, container);
            }
        }

        public async Task StopContainersAsync(IExecutionContext executionContext, object data)
        {
            Trace.Entering();
            ArgUtil.NotNull(executionContext, nameof(executionContext));

            List<ContainerInfo> containers = data as List<ContainerInfo>;
            ArgUtil.NotNull(containers, nameof(containers));

            foreach (var container in containers)
            {
                await StopContainerAsync(executionContext, container);
            }
            // Remove the container network
            var containerNetwork = GetContainerNetwork(executionContext);
            await RemoveContainerNetworkAsync(executionContext, containerNetwork);
        }

        private async Task<string> GetMSIAccessToken(IExecutionContext executionContext)
        {
            CancellationToken cancellationToken = executionContext.CancellationToken;
            Trace.Entering();
            // Check environment variable for debugging
            var envVar = System.Environment.GetEnvironmentVariable("DEBUG_MSI_LOGIN_INFO");
            // Future: Set this client id. This is the MSI client ID.
            ChainedTokenCredential credential = envVar == "1"
                ? new ChainedTokenCredential(new ManagedIdentityCredential(clientId: null), new VisualStudioCredential(), new AzureCliCredential())
                : new ChainedTokenCredential(new ManagedIdentityCredential(clientId: null));
            executionContext.Debug("Retrieving AAD token using MSI authentication...");
            AccessToken accessToken = await credential.GetTokenAsync(new TokenRequestContext(new[] {
                "https://management.core.windows.net/"
            }), cancellationToken);

            return accessToken.Token.ToString();
        }

        private async Task<string> GetAcrPasswordFromAADToken(IExecutionContext executionContext, string AADToken, string tenantId, string registryServer, string loginServer)
        {
            Trace.Entering();
            CancellationToken cancellationToken = executionContext.CancellationToken;
            Uri url = new Uri(registryServer + "/oauth2/exchange");
            const int retryLimit = 5;
            using HttpClientHandler httpClientHandler = HostContext.CreateHttpClientHandler();
            using HttpClient httpClient = new HttpClient(httpClientHandler);
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Content-Type", "application/x-www-form-urlencoded");

            List<KeyValuePair<string, string>> keyValuePairs = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("grant_type", "access_token"),
                new KeyValuePair<string, string>("service", loginServer),
                new KeyValuePair<string, string>("tenant", tenantId),
                new KeyValuePair<string, string>("access_token", AADToken)
            };
            using FormUrlEncodedContent formUrlEncodedContent = new FormUrlEncodedContent(keyValuePairs);
            string AcrPassword = string.Empty;
            int retryCount = 0;
            int timeElapsed = 0;
            int timeToWait = 0;
            do
            {
                executionContext.Debug("Attempting to convert AAD token to an ACR token");

                var response = await httpClient.PostAsync(url, formUrlEncodedContent, cancellationToken).ConfigureAwait(false);
                executionContext.Debug($"Status Code: {response.StatusCode}");

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    executionContext.Debug("Successfully converted AAD token to an ACR token");
                    string result = await response.Content.ReadAsStringAsync();
                    Dictionary<string, string> list = JsonConvert.DeserializeObject<Dictionary<string, string>>(result);
                    AcrPassword = list["refresh_token"];
                }
                else if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    executionContext.Debug("Too many requests were made to get an ACR token. Retrying...");

                    timeElapsed = 2000 + timeToWait * 2;
                    retryCount++;
                    await Task.Delay(timeToWait);
                    timeToWait = timeElapsed;
                }
                else
                {
                    throw new NotSupportedException("Could not fetch access token for ACR. Please configure Managed Service Identity (MSI) for Azure Container Registry with the appropriate permissions - https://docs.microsoft.com/en-us/azure/app-service/tutorial-custom-container?pivots=container-linux#configure-app-service-to-deploy-the-image-from-the-registry.");
                }

            } while (retryCount < retryLimit && string.IsNullOrEmpty(AcrPassword));

            if (string.IsNullOrEmpty(AcrPassword))
            {
                throw new NotSupportedException("Could not acquire ACR token from given AAD token. Please check that the necessary access is provided and try again.");
            }
            return AcrPassword;
        }

        private async Task PullContainerAsync(IExecutionContext executionContext, ContainerInfo container)
        {
            Trace.Entering();

            ArgUtil.NotNull(executionContext, nameof(executionContext));
            ArgUtil.NotNull(container, nameof(container));
            ArgUtil.NotNullOrEmpty(container.ContainerImage, nameof(container.ContainerImage));

            Trace.Info($"Container name: {container.ContainerName}");
            Trace.Info($"Container image: {container.ContainerImage}");
            Trace.Info($"Container registry: {container.ContainerRegistryEndpoint.ToString()}");
            Trace.Info($"Container options: {container.ContainerCreateOptions}");
            Trace.Info($"Skip container image pull: {container.SkipContainerImagePull}");

            // Login to private docker registry
            string registryServer = string.Empty;
            if (container.ContainerRegistryEndpoint != Guid.Empty)
            {
                var registryEndpoint = executionContext.Endpoints.FirstOrDefault(x => x.Type == "dockerregistry" && x.Id == container.ContainerRegistryEndpoint);
                ArgUtil.NotNull(registryEndpoint, nameof(registryEndpoint));

                string username = string.Empty;
                string password = string.Empty;
                string registryType = string.Empty;
                string authType = string.Empty;

                registryEndpoint.Data?.TryGetValue("registrytype", out registryType);
                if (string.Equals(registryType, "ACR", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        executionContext.Debug("Attempting to get endpoint authorization scheme...");
                        authType = registryEndpoint.Authorization?.Scheme;

                        if (string.IsNullOrEmpty(authType))
                        {
                            executionContext.Debug("Attempting to get endpoint authorization scheme as an authorization parameter...");
                            registryEndpoint.Authorization?.Parameters?.TryGetValue("scheme", out authType);
                        }
                    }
                    catch
                    {
                        executionContext.Debug("Failed to get endpoint authorization scheme as an authorization parameter. Will default authorization scheme to ServicePrincipal");
                        authType = "ServicePrincipal";
                    }

                    string loginServer = string.Empty;
                    registryEndpoint.Authorization?.Parameters?.TryGetValue("loginServer", out loginServer);
                    if (loginServer != null)
                    {
                        loginServer = loginServer.ToLower();
                    }

                    registryServer = $"https://{loginServer}";
                    if (string.Equals(authType, "ManagedServiceIdentity", StringComparison.OrdinalIgnoreCase))
                    {
                        string tenantId = string.Empty;
                        registryEndpoint.Authorization?.Parameters?.TryGetValue("tenantid", out tenantId);
                        // Documentation says to pass username through this way
                        username = Guid.Empty.ToString("D");
                        string AADToken = await GetMSIAccessToken(executionContext);
                        executionContext.Debug("Successfully retrieved AAD token using the MSI authentication scheme.");
                        // change to getting password from string
                        password = await GetAcrPasswordFromAADToken(executionContext, AADToken, tenantId, registryServer, loginServer);
                    }
                    else
                    {
                        registryEndpoint.Authorization?.Parameters?.TryGetValue("serviceprincipalid", out username);
                        registryEndpoint.Authorization?.Parameters?.TryGetValue("serviceprincipalkey", out password);
                    }
                }
                else
                {
                    registryEndpoint.Authorization?.Parameters?.TryGetValue("registry", out registryServer);
                    registryEndpoint.Authorization?.Parameters?.TryGetValue("username", out username);
                    registryEndpoint.Authorization?.Parameters?.TryGetValue("password", out password);
                }

                ArgUtil.NotNullOrEmpty(registryServer, nameof(registryServer));
                ArgUtil.NotNullOrEmpty(username, nameof(username));
                ArgUtil.NotNullOrEmpty(password, nameof(password));

                bool dockerLoginRetry = AgentKnobs.DockerLoginRetry.GetValue(executionContext).AsBoolean();

                int loginExitCode = await _dockerManger.DockerLogin(
                    executionContext,
                    registryServer,
                    username,
                    password,
                    dockerLoginRetry);

                if (loginExitCode != 0)
                {
                    throw new InvalidOperationException($"Docker login fail with exit code {loginExitCode}");
                }
            }

            try
            {
                if (!container.SkipContainerImagePull)
                {
                    if (!string.IsNullOrEmpty(registryServer) &&
                        registryServer.IndexOf("index.docker.io", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        var registryServerUri = new Uri(registryServer);
                        if (!container.ContainerImage.StartsWith(registryServerUri.Authority, StringComparison.OrdinalIgnoreCase))
                        {
                            container.ContainerImage = $"{registryServerUri.Authority}/{container.ContainerImage}";
                        }
                    }

                    // Pull down docker image with retry up to 3 times
                    int retryCount = 0;
                    int pullExitCode = 0;
                    int maxRetries = 3;

                    while (retryCount < maxRetries)
                    {
                        pullExitCode = await _dockerManger.DockerPull(executionContext, container.ContainerImage);

                        if (pullExitCode == 0)
                        {
                            break;
                        }

                        var backOff = BackoffTimerHelper.GetRandomBackoff(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10));
                        executionContext.Warning($"Docker pull failed with exit code {pullExitCode}, back off {backOff.TotalSeconds} seconds before retry.");
                        await Task.Delay(backOff);
                        retryCount++;
                    }

                    if (retryCount == maxRetries && pullExitCode != 0)
                    {
                        throw new InvalidOperationException($"Docker pull failed with exit code {pullExitCode}");
                    }
                }

                if (PlatformUtil.RunningOnMacOS)
                {
                    container.ImageOS = PlatformUtil.OS.Linux;
                }
                // if running on Windows, and attempting to run linux container, require container to have node
                else if (PlatformUtil.RunningOnWindows)
                {
                    string containerOS = await _dockerManger.DockerInspect(context: executionContext,
                                                                dockerObject: container.ContainerImage,
                                                                options: $"--format=\"{{{{.Os}}}}\"");
                    if (string.Equals("linux", containerOS, StringComparison.OrdinalIgnoreCase))
                    {
                        container.ImageOS = PlatformUtil.OS.Linux;
                    }
                }
            }
            finally
            {
                // Logout for private registry
                if (!string.IsNullOrEmpty(registryServer))
                {
                    int logoutExitCode = await _dockerManger.DockerLogout(executionContext, registryServer);
                    if (logoutExitCode != 0)
                    {
                        executionContext.Error($"Docker logout fail with exit code {logoutExitCode}");
                    }
                }
            }
        }

        private async Task StartContainerAsync(IExecutionContext executionContext, ContainerInfo container)
        {
            Trace.Entering();
            ArgUtil.NotNull(executionContext, nameof(executionContext));
            ArgUtil.NotNull(container, nameof(container));
            ArgUtil.NotNullOrEmpty(container.ContainerImage, nameof(container.ContainerImage));

            Trace.Info($"Container name: {container.ContainerName}");
            Trace.Info($"Container image: {container.ContainerImage}");
            Trace.Info($"Container registry: {container.ContainerRegistryEndpoint.ToString()}");
            Trace.Info($"Container options: {container.ContainerCreateOptions}");
            Trace.Info($"Skip container image pull: {container.SkipContainerImagePull}");
            foreach (var port in container.UserPortMappings)
            {
                Trace.Info($"User provided port: {port.Value}");
            }
            foreach (var volume in container.UserMountVolumes)
            {
                Trace.Info($"User provided volume: {volume.Value}");
            }

            if (container.ImageOS != PlatformUtil.OS.Windows)
            {
                string defaultWorkingDirectory = executionContext.Variables.Get(Constants.Variables.System.DefaultWorkingDirectory);
                defaultWorkingDirectory = container.TranslateContainerPathForImageOS(PlatformUtil.HostOS, container.TranslateToContainerPath(defaultWorkingDirectory));
                if (string.IsNullOrEmpty(defaultWorkingDirectory))
                {
                    throw new NotSupportedException(StringUtil.Loc("ContainerJobRequireSystemDefaultWorkDir"));
                }

                string workingDirectory = IOUtil.GetDirectoryName(defaultWorkingDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), container.ImageOS);
                string mountWorkingDirectory = container.TranslateToHostPath(workingDirectory);
                executionContext.Debug($"Default Working Directory {defaultWorkingDirectory}");
                executionContext.Debug($"Working Directory {workingDirectory}");
                executionContext.Debug($"Mount Working Directory {mountWorkingDirectory}");
                if (!string.IsNullOrEmpty(workingDirectory))
                {
                    container.MountVolumes.Add(new MountVolume(mountWorkingDirectory, workingDirectory, readOnly: container.isReadOnlyVolume(Constants.DefaultContainerMounts.Work)));
                }

                container.MountVolumes.Add(new MountVolume(HostContext.GetDirectory(WellKnownDirectory.Temp), container.TranslateToContainerPath(HostContext.GetDirectory(WellKnownDirectory.Temp))));
                container.MountVolumes.Add(new MountVolume(HostContext.GetDirectory(WellKnownDirectory.Tasks), container.TranslateToContainerPath(HostContext.GetDirectory(WellKnownDirectory.Tasks)),
                    readOnly: container.isReadOnlyVolume(Constants.DefaultContainerMounts.Tasks)));
            }
            else
            {
                container.MountVolumes.Add(new MountVolume(HostContext.GetDirectory(WellKnownDirectory.Work), container.TranslateToContainerPath(HostContext.GetDirectory(WellKnownDirectory.Work)),
                    readOnly: container.isReadOnlyVolume(Constants.DefaultContainerMounts.Work)));

                if (AgentKnobs.AllowMountTasksReadonlyOnWindows.GetValue(executionContext).AsBoolean())
                {
                    container.MountVolumes.Add(new MountVolume(HostContext.GetDirectory(WellKnownDirectory.Tasks), container.TranslateToContainerPath(HostContext.GetDirectory(WellKnownDirectory.Tasks)),
                        readOnly: container.isReadOnlyVolume(Constants.DefaultContainerMounts.Tasks)));
                }
            }

            container.MountVolumes.Add(new MountVolume(HostContext.GetDirectory(WellKnownDirectory.Tools), container.TranslateToContainerPath(HostContext.GetDirectory(WellKnownDirectory.Tools)),
                readOnly: container.isReadOnlyVolume(Constants.DefaultContainerMounts.Tools)));

            bool externalReadOnly = container.ImageOS != PlatformUtil.OS.Windows || container.isReadOnlyVolume(Constants.DefaultContainerMounts.Externals); // This code was refactored to use PlatformUtils. The previous implementation did not have the externals directory mounted read-only for Windows.
                                                                                                                                                            // That seems wrong, but to prevent any potential backwards compatibility issues, we are keeping the same logic
            container.MountVolumes.Add(new MountVolume(HostContext.GetDirectory(WellKnownDirectory.Externals), container.TranslateToContainerPath(HostContext.GetDirectory(WellKnownDirectory.Externals)), externalReadOnly));

            if (container.ImageOS != PlatformUtil.OS.Windows)
            {
                // Ensure .taskkey file exist so we can mount it.
                string taskKeyFile = Path.Combine(HostContext.GetDirectory(WellKnownDirectory.Work), ".taskkey");

                if (!File.Exists(taskKeyFile))
                {
                    File.WriteAllText(taskKeyFile, string.Empty);
                }
                container.MountVolumes.Add(new MountVolume(taskKeyFile, container.TranslateToContainerPath(taskKeyFile)));
            }

            if (container.IsJobContainer)
            {
                // See if this container brings its own Node.js
                container.CustomNodePath = await _dockerManger.DockerInspect(context: executionContext,
                                                                    dockerObject: container.ContainerImage,
                                                                    options: $"--format=\"{{{{index .Config.Labels \\\"{_nodeJsPathLabel}\\\"}}}}\"");

                string node;
                if (!string.IsNullOrEmpty(container.CustomNodePath))
                {
                    node = container.CustomNodePath;
                }
                else
                {
                    node = container.TranslateToContainerPath(Path.Combine(HostContext.GetDirectory(WellKnownDirectory.Externals), "node", "bin", $"node{IOUtil.ExeExtension}"));

                    // if on Mac OS X, require container to have node
                    if (PlatformUtil.RunningOnMacOS)
                    {
                        container.CustomNodePath = "node";
                        node = container.CustomNodePath;
                    }
                    // if running on Windows, and attempting to run linux container, require container to have node
                    else if (PlatformUtil.RunningOnWindows && container.ImageOS == PlatformUtil.OS.Linux)
                    {
                        container.CustomNodePath = "node";
                        node = container.CustomNodePath;
                    }
                }
                string sleepCommand = $"\"{node}\" -e \"setInterval(function(){{}}, 24 * 60 * 60 * 1000);\"";
                container.ContainerCommand = sleepCommand;
            }

            container.ContainerId = await _dockerManger.DockerCreate(executionContext, container);
            ArgUtil.NotNullOrEmpty(container.ContainerId, nameof(container.ContainerId));
            if (container.IsJobContainer)
            {
                executionContext.Variables.Set(Constants.Variables.Agent.ContainerId, container.ContainerId);
            }

            // reuse DockerLoginRetry for also retry docker start
            bool dockerInitRetry = AgentKnobs.DockerLoginRetry.GetValue(executionContext).AsBoolean();

            // Start container
            int startExitCode = await _dockerManger.DockerStart(executionContext, container.ContainerId, dockerInitRetry);
            if (startExitCode != 0)
            {
                throw new InvalidOperationException($"Docker start fail with exit code {startExitCode}");
            }

            try
            {
                // Make sure container is up and running
                var psOutputs = await _dockerManger.DockerPS(executionContext, $"--all --filter id={container.ContainerId} --filter status=running --no-trunc --format \"{{{{.ID}}}} {{{{.Status}}}}\"");
                if (psOutputs.FirstOrDefault(x => !string.IsNullOrEmpty(x))?.StartsWith(container.ContainerId) != true)
                {
                    // container is not up and running, pull docker log for this container.
                    await _dockerManger.DockerPS(executionContext, $"--all --filter id={container.ContainerId} --no-trunc --format \"{{{{.ID}}}} {{{{.Status}}}}\"");
                    int logsExitCode = await _dockerManger.DockerLogs(executionContext, container.ContainerId);
                    if (logsExitCode != 0)
                    {
                        executionContext.Warning($"Docker logs fail with exit code {logsExitCode}");
                    }

                    executionContext.Warning($"Docker container {container.ContainerId} is not in running state.");
                }
            }
            catch (Exception ex)
            {
                // pull container log is best effort.
                Trace.Error("Catch exception when check container log and container status.");
                Trace.Error(ex);
            }

            // Get port mappings of running container
            if (!container.IsJobContainer)
            {
                container.AddPortMappings(await _dockerManger.DockerPort(executionContext, container.ContainerId));
                foreach (var port in container.PortMappings)
                {
                    executionContext.Variables.Set(
                        $"{Constants.Variables.Agent.ServicePortPrefix}.{container.ContainerNetworkAlias}.ports.{port.ContainerPort}",
                        $"{port.HostPort}");
                }
            }

            if (!PlatformUtil.RunningOnWindows)
            {
                if (container.IsJobContainer)
                {
                    // Ensure bash exist in the image
                    await DockerExec(executionContext, container.ContainerId, $"sh -c \"command -v bash\"");

                    // Get current username
                    container.CurrentUserName = (await ExecuteCommandAsync(executionContext, "whoami", string.Empty)).FirstOrDefault();
                    ArgUtil.NotNullOrEmpty(container.CurrentUserName, nameof(container.CurrentUserName));

                    // Get current userId
                    container.CurrentUserId = (await ExecuteCommandAsync(executionContext, "id", $"-u {container.CurrentUserName}")).FirstOrDefault();
                    ArgUtil.NotNullOrEmpty(container.CurrentUserId, nameof(container.CurrentUserId));
                    // Get current groupId
                    container.CurrentGroupId = (await ExecuteCommandAsync(executionContext, "id", $"-g {container.CurrentUserName}")).FirstOrDefault();
                    ArgUtil.NotNullOrEmpty(container.CurrentGroupId, nameof(container.CurrentGroupId));
                    // Get current group name
                    container.CurrentGroupName = (await ExecuteCommandAsync(executionContext, "id", $"-gn {container.CurrentUserName}")).FirstOrDefault();
                    ArgUtil.NotNullOrEmpty(container.CurrentGroupName, nameof(container.CurrentGroupName));

                    executionContext.Output(StringUtil.Loc("CreateUserWithSameUIDInsideContainer", container.CurrentUserId));

                    // Create an user with same uid as the agent run as user inside the container.
                    // All command execute in docker will run as Root by default,
                    // this will cause the agent on the host machine doesn't have permission to any new file/folder created inside the container.
                    // So, we create a user account with same UID inside the container and let all docker exec command run as that user.
                    string containerUserName = string.Empty;

                    // We need to find out whether there is a user with same UID inside the container
                    List<string> userNames = await DockerExec(executionContext, container.ContainerId, $"bash -c \"getent passwd {container.CurrentUserId} | cut -d: -f1 \"");

                    if (userNames.Count > 0)
                    {
                        // check all potential usernames that might match the UID
                        foreach (string username in userNames)
                        {
                            try
                            {
                                await DockerExec(executionContext, container.ContainerId, $"id -u {username}");
                                containerUserName = username;
                                break;
                            }
                            catch (Exception ex) when (ex is InvalidOperationException)
                            {
                                // check next username
                            }
                        }
                    }

                    // Determinate if we need to use another primary group for container user.
                    // The user created inside the container must have the same group ID (GID)
                    // as the user on the host on which the agent is running.
                    bool useHostGroupId = false;
                    int hostGroupId;
                    int hostUserId;
                    if (AgentKnobs.UseHostGroupId.GetValue(executionContext).AsBoolean() &&
                        int.TryParse(container.CurrentGroupId, out hostGroupId) &&
                        int.TryParse(container.CurrentUserId, out hostUserId) &&
                        hostGroupId != hostUserId)
                    {
                        Trace.Info($"Host group id ({hostGroupId}) is not matching host user id ({hostUserId}), using {hostGroupId} as a primary GID inside container");
                        useHostGroupId = true;
                    }

                    if (string.IsNullOrEmpty(containerUserName)) // Create a new user with same UID as on the host
                    {
                        string userNameSuffix = "_azpcontainer";
                        // Linux allows for a 32-character username
                        containerUserName = KeepAllowedLength(container.CurrentUserName, 32, userNameSuffix);
                        string fallback = $"useradd -m -u {container.CurrentUserId} {containerUserName}";
                        if (useHostGroupId) // Create a new user with the same UID and the same GID as on the host
                        {
                            try
                            {
                                // Linux allows for a 32-character groupname
                                string containerGroupName = KeepAllowedLength(container.CurrentGroupName, 32, userNameSuffix);
                                await DockerExec(executionContext, container.ContainerId, $"groupadd -g {container.CurrentGroupId} {containerGroupName}");
                                await DockerExec(executionContext, container.ContainerId, $"useradd -m -g {container.CurrentGroupId} -u {container.CurrentUserId} {containerUserName}");
                            }
                            catch (Exception ex) when (ex is InvalidOperationException)
                            {
                                Trace.Info($"Falling back to the '{fallback}' command.");
                                await DockerExec(executionContext, container.ContainerId, fallback);
                            }
                        }
                        else
                        {
                            await DockerExec(executionContext, container.ContainerId, fallback);
                        }
                    }

                    executionContext.Output(StringUtil.Loc("GrantContainerUserSUDOPrivilege", containerUserName));

                    // Create a new group for giving sudo permission
                    string sudoGroupName = "azure_pipelines_sudo";
                    await DockerExec(executionContext, container.ContainerId, $"groupadd {sudoGroupName}");

                    // Add the new created user to the new created sudo group.
                    await DockerExec(executionContext, container.ContainerId, $"usermod -a -G {sudoGroupName} {containerUserName}");

                    // Allow the new sudo group run any sudo command without providing password.
                    await DockerExec(executionContext, container.ContainerId, $"su -c \"echo '%{sudoGroupName} ALL=(ALL:ALL) NOPASSWD:ALL' >> /etc/sudoers\"");

                    if (AgentKnobs.SetupDockerGroup.GetValue(executionContext).AsBoolean())
                    {
                        executionContext.Output(StringUtil.Loc("AllowContainerUserRunDocker", containerUserName));
                        // Get docker.sock group id on Host
                        string statFormatOption = "-c %g";
                        if (PlatformUtil.RunningOnMacOS)
                        {
                            statFormatOption = "-f %g";
                        }
                        string dockerSockGroupId = (await ExecuteCommandAsync(executionContext, "stat", $"{statFormatOption} /var/run/docker.sock")).FirstOrDefault();

                        // We need to find out whether there is a group with same GID inside the container
                        string existingGroupName = null;
                        List<string> groupsOutput = await DockerExec(executionContext, container.ContainerId, $"bash -c \"cat /etc/group\"");

                        if (groupsOutput.Count > 0)
                        {
                            // check all potential groups that might match the GID.
                            foreach (string groupOutput in groupsOutput)
                            {
                                if (!string.IsNullOrEmpty(groupOutput))
                                {
                                    var groupSegments = groupOutput.Split(':');
                                    if (groupSegments.Length != 4)
                                    {
                                        Trace.Warning($"Unexpected output from /etc/group: '{groupOutput}'");
                                    }
                                    else
                                    {
                                        // the output of /etc/group should looks like `group:x:gid:`
                                        var groupName = groupSegments[0];
                                        var groupId = groupSegments[2];

                                        if (string.Equals(dockerSockGroupId, groupId))
                                        {
                                            existingGroupName = groupName;
                                            break;
                                        }
                                    }
                                }
                            }
                        }

                        if (string.IsNullOrEmpty(existingGroupName))
                        {
                            // create a new group with same gid
                            existingGroupName = "azure_pipelines_docker";
                            await DockerExec(executionContext, container.ContainerId, $"groupadd -g {dockerSockGroupId} {existingGroupName}");
                        }
                        // Add the new created user to the docker socket group.
                        await DockerExec(executionContext, container.ContainerId, $"usermod -a -G {existingGroupName} {containerUserName}");

                        // if path to node is just 'node', with no path, let's make sure it is actually there
                        if (string.Equals(container.CustomNodePath, "node", StringComparison.OrdinalIgnoreCase))
                        {
                            List<string> nodeVersionOutput = await DockerExec(executionContext, container.ContainerId, $"bash -c \"node -v\"");
                            if (nodeVersionOutput.Count > 0)
                            {
                                executionContext.Output($"Detected Node Version: {nodeVersionOutput[0]}");
                                Trace.Info($"Using node version {nodeVersionOutput[0]} in container {container.ContainerId}");
                            }
                            else
                            {
                                throw new InvalidOperationException($"Unable to get node version on container {container.ContainerId}. No output from node -v");
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(containerUserName))
                    {
                        container.CurrentUserName = containerUserName;
                    }
                }
            }
        }

        private async Task StopContainerAsync(IExecutionContext executionContext, ContainerInfo container)
        {
            Trace.Entering();
            ArgUtil.NotNull(executionContext, nameof(executionContext));
            ArgUtil.NotNull(container, nameof(container));

            if (!string.IsNullOrEmpty(container.ContainerId))
            {
                executionContext.Output($"Stop and remove container: {container.ContainerDisplayName}");

                int rmExitCode = await _dockerManger.DockerRemove(executionContext, container.ContainerId);
                if (rmExitCode != 0)
                {
                    executionContext.Warning($"Docker rm fail with exit code {rmExitCode}");
                }
            }
        }

        private async Task<List<string>> ExecuteCommandAsync(IExecutionContext context, string command, string arg)
        {
            context.Command($"{command} {arg}");

            List<string> outputs = new List<string>();
            object outputLock = new object();
            var processInvoker = HostContext.CreateService<IProcessInvoker>();
            processInvoker.OutputDataReceived += delegate (object sender, ProcessDataReceivedEventArgs message)
            {
                if (!string.IsNullOrEmpty(message.Data))
                {
                    lock (outputLock)
                    {
                        outputs.Add(message.Data);
                    }
                }
            };

            processInvoker.ErrorDataReceived += delegate (object sender, ProcessDataReceivedEventArgs message)
            {
                if (!string.IsNullOrEmpty(message.Data))
                {
                    lock (outputLock)
                    {
                        outputs.Add(message.Data);
                    }
                }
            };

            await processInvoker.ExecuteAsync(
                            workingDirectory: HostContext.GetDirectory(WellKnownDirectory.Work),
                            fileName: command,
                            arguments: arg,
                            environment: null,
                            requireExitCodeZero: true,
                            outputEncoding: null,
                            cancellationToken: CancellationToken.None);

            foreach (var outputLine in outputs)
            {
                context.Output(outputLine);
            }

            return outputs;
        }

        private async Task CreateContainerNetworkAsync(IExecutionContext executionContext, string network)
        {
            Trace.Entering();
            ArgUtil.NotNull(executionContext, nameof(executionContext));

            if (network != "host")
            {
                int networkExitCode = await _dockerManger.DockerNetworkCreate(executionContext, network);
                if (networkExitCode != 0)
                {
                    throw new InvalidOperationException($"Docker network create failed with exit code {networkExitCode}");
                }
            }
            else
            {
                Trace.Info("Skipping creation of a new docker network. Reusing the host network.");
            }

            // Expose docker network to env
            executionContext.Variables.Set(Constants.Variables.Agent.ContainerNetwork, network);
        }

        private async Task RemoveContainerNetworkAsync(IExecutionContext executionContext, string network)
        {
            Trace.Entering();
            ArgUtil.NotNull(executionContext, nameof(executionContext));
            ArgUtil.NotNull(network, nameof(network));

            if (network != "host")
            {
                executionContext.Output($"Remove container network: {network}");

                int removeExitCode = await _dockerManger.DockerNetworkRemove(executionContext, network);
                if (removeExitCode != 0)
                {
                    executionContext.Warning($"Docker network rm failed with exit code {removeExitCode}");
                }
            }

            // Remove docker network from env
            executionContext.Variables.Set(Constants.Variables.Agent.ContainerNetwork, null);
        }

        private async Task ContainerHealthcheck(IExecutionContext executionContext, ContainerInfo container)
        {
            string healthCheck = "--format=\"{{if .Config.Healthcheck}}{{print .State.Health.Status}}{{end}}\"";
            string serviceHealth = await _dockerManger.DockerInspect(context: executionContext, dockerObject: container.ContainerId, options: healthCheck);
            if (string.IsNullOrEmpty(serviceHealth))
            {
                // Container has no HEALTHCHECK
                return;
            }
            var retryCount = 0;
            while (string.Equals(serviceHealth, "starting", StringComparison.OrdinalIgnoreCase))
            {
                TimeSpan backoff = BackoffTimerHelper.GetExponentialBackoff(retryCount, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(32), TimeSpan.FromSeconds(2));
                executionContext.Output($"{container.ContainerNetworkAlias} service is starting, waiting {backoff.Seconds} seconds before checking again.");
                await Task.Delay(backoff, executionContext.CancellationToken);
                serviceHealth = await _dockerManger.DockerInspect(context: executionContext, dockerObject: container.ContainerId, options: healthCheck);
                retryCount++;
            }
            if (string.Equals(serviceHealth, "healthy", StringComparison.OrdinalIgnoreCase))
            {
                executionContext.Output($"{container.ContainerNetworkAlias} service is healthy.");
            }
            else
            {
                throw new InvalidOperationException($"Failed to initialize, {container.ContainerNetworkAlias} service is {serviceHealth}.");
            }
        }

        private async Task<List<string>> DockerExec(IExecutionContext context, string containerId, string command)
        {
            Trace.Info($"Docker-exec is going to execute: `{command}`; container id: `{containerId}`");
            List<string> output = new List<string>();
            int exitCode = await _dockerManger.DockerExec(context, containerId, string.Empty, command, output);
            string commandOutput = "command does not have output";
            if (output.Count > 0)
            {
                commandOutput = $"command output: `{output[0]}`";
            }
            for (int i = 1; i < output.Count; i++)
            {
                commandOutput += $", `{output[i]}`";
            }
            string message = $"Docker-exec executed: `{command}`; container id: `{containerId}`; exit code: `{exitCode}`; {commandOutput}";
            if (exitCode != 0)
            {
                Trace.Error(message);
                throw new InvalidOperationException(message);
            }
            Trace.Info(message);
            return output;
        }

        private static string KeepAllowedLength(string name, int allowedLength, string suffix = "")
        {
            int keepNameLength = Math.Min(allowedLength - suffix.Length, name.Length);
            return $"{name.Substring(0, keepNameLength)}{suffix}";
        }

        private static void ThrowIfAlreadyInContainer()
        {
            if (PlatformUtil.RunningOnWindows)
            {
                // service CExecSvc is Container Execution Agent.
                ServiceController[] scServices = ServiceController.GetServices();
                if (scServices.Any(x => String.Equals(x.ServiceName, "cexecsvc", StringComparison.OrdinalIgnoreCase) && x.Status == ServiceControllerStatus.Running))
                {
                    throw new NotSupportedException(StringUtil.Loc("AgentAlreadyInsideContainer"));
                }
            }
            else
            {
                try
                {
                    var initProcessCgroup = File.ReadLines("/proc/1/cgroup");
                    if (initProcessCgroup.Any(x => x.IndexOf(":/docker/", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        throw new NotSupportedException(StringUtil.Loc("AgentAlreadyInsideContainer"));
                    }
                }
                catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException)
                {
                    // if /proc/1/cgroup doesn't exist, we are not inside a container
                }
            }
        }

        private static void ThrowIfWrongWindowsVersion(IExecutionContext executionContext)
        {
            if (!PlatformUtil.RunningOnWindows)
            {
                return;
            }

            // Check OS version (Windows server 1803 is required)
            object windowsInstallationType = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "InstallationType", defaultValue: null);
            ArgUtil.NotNull(windowsInstallationType, nameof(windowsInstallationType));
            object windowsReleaseId = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ReleaseId", defaultValue: null);
            ArgUtil.NotNull(windowsReleaseId, nameof(windowsReleaseId));
            executionContext.Debug($"Current Windows version: '{windowsReleaseId} ({windowsInstallationType})'");

            if (int.TryParse(windowsReleaseId.ToString(), out int releaseId))
            {
                if (releaseId < 1903) // >= 1903, support windows client and server
                {
                    if (!windowsInstallationType.ToString().StartsWith("Server", StringComparison.OrdinalIgnoreCase) || releaseId < 1803)
                    {
                        throw new NotSupportedException(StringUtil.Loc("ContainerWindowsVersionRequirement"));
                    }
                }
            }
            else
            {
                throw new ArgumentOutOfRangeException(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ReleaseId");
            }
        }
    }
}
