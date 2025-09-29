// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using Agent.Sdk.Knob;
using Azure.Core;
using Azure.Identity;
using Microsoft.Identity.Client;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker.Container;
using Microsoft.VisualStudio.Services.Agent.Worker.Handlers;
using Microsoft.VisualStudio.Services.Agent.Worker.Telemetry;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;

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
        private const string c_tenantId = "tenantid";
        private const string c_clientId = "servicePrincipalId";
        private const string c_activeDirectoryServiceEndpointResourceId = "activeDirectoryServiceEndpointResourceId";
        private const string c_workloadIdentityFederationScheme = "WorkloadIdentityFederation";
        private const string c_managedServiceIdentityScheme = "ManagedServiceIdentity";

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
            using (Trace.EnteringWithDuration())
            {
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
        }

        public async Task StopContainersAsync(IExecutionContext executionContext, object data)
        {
            using (Trace.EnteringWithDuration())
            {
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
        }

        private async Task<string> GetMSIAccessToken(IExecutionContext executionContext)
        {
            using (Trace.EnteringWithDuration())
            {
                CancellationToken cancellationToken = executionContext.CancellationToken;
                executionContext.Debug("MSI access token retrieval initiated");

                // Check environment variable for debugging
                var envVar = Environment.GetEnvironmentVariable("DEBUG_MSI_LOGIN_INFO");
                bool isDebugMode = envVar == "1";

                try
                {
                    // Future: Set this client id. This is the MSI client ID.
                    ChainedTokenCredential credential = isDebugMode
                        ? new ChainedTokenCredential(new ManagedIdentityCredential(clientId: null), new VisualStudioCredential(), new AzureCliCredential())
                        : new ChainedTokenCredential(new ManagedIdentityCredential(clientId: null));
                    executionContext.Debug("Retrieving AAD token using MSI authentication...");
                    AccessToken accessToken = await credential.GetTokenAsync(new TokenRequestContext(new[] {
                    "https://management.core.windows.net/"
                }), cancellationToken);

                    return accessToken.Token.ToString();
                }
                catch (AuthenticationFailedException ex)
                {
                    executionContext.Error($"MSI authentication failed: {ex.Message}");
                    if (isDebugMode)
                    {
                        executionContext.Error("Debug mode authentication failure - check ManagedIdentity, VisualStudio, and AzureCLI configurations");
                        executionContext.Error("Verify that at least one authentication method is properly configured");
                    }
                    else
                    {
                        executionContext.Error("Production MSI authentication failure - verify Managed Identity is enabled and properly configured");
                        executionContext.Error("Check that the system/user-assigned managed identity has appropriate permissions");
                    }
                    throw;
                }
                catch (OperationCanceledException)
                {
                    executionContext.Warning("MSI token retrieval was cancelled due to timeout or cancellation request");
                    throw;
                }
                catch (Exception ex)
                {
                    executionContext.Error($"Unexpected error during MSI token retrieval: {ex.Message}");
                    executionContext.Error($"Exception type: {ex.GetType().Name}");
                    throw;
                }
            }
        }

        private async Task<string> GetAccessTokenUsingWorkloadIdentityFederation(IExecutionContext executionContext, ServiceEndpoint registryEndpoint)
        {
            using (Trace.EnteringWithDuration())
            {
                ArgumentNullException.ThrowIfNull(executionContext);
                ArgumentNullException.ThrowIfNull(registryEndpoint);

                CancellationToken cancellationToken = executionContext.CancellationToken;
                executionContext.Debug("Workload Identity Federation access token retrieval initiated");

                try
                {
                    var tenantId = string.Empty;
                    if (!registryEndpoint.Authorization?.Parameters?.TryGetValue(c_tenantId, out tenantId) ?? false)
                    {
                        executionContext.Error($"Failed to read required parameter: {c_tenantId}");
                        throw new InvalidOperationException($"Could not read {c_tenantId}");
                    }
                    executionContext.Debug($"Tenant ID extracted: {tenantId}");

                    var clientId = string.Empty;
                    if (!registryEndpoint.Authorization?.Parameters?.TryGetValue(c_clientId, out clientId) ?? false)
                    {
                        executionContext.Error($"Failed to read required parameter: {c_clientId}");
                        throw new InvalidOperationException($"Could not read {c_clientId}");
                    }
                    executionContext.Debug($"Client ID extracted: {clientId}");

                    var resourceId = string.Empty;
                    if (!registryEndpoint.Data?.TryGetValue(c_activeDirectoryServiceEndpointResourceId, out resourceId) ?? false)
                    {
                        executionContext.Error($"Failed to read required parameter: {c_activeDirectoryServiceEndpointResourceId}");
                        throw new InvalidOperationException($"Could not read {c_activeDirectoryServiceEndpointResourceId}");
                    }
                    executionContext.Debug($"Resource ID extracted: {resourceId}");

                    executionContext.Debug("Building MSAL ConfidentialClientApplication with Workload Identity Federation");
                    var app = ConfidentialClientApplicationBuilder.Create(clientId)
                        .WithAuthority(AzureCloudInstance.AzurePublic, tenantId)
                        .WithClientAssertion(async (AssertionRequestOptions options) =>
                        {
                            executionContext.Debug("Creating OIDC token for client assertion");
                            var systemConnection = executionContext.Endpoints.SingleOrDefault(x => string.Equals(x.Name, WellKnownServiceEndpointNames.SystemVssConnection, StringComparison.Ordinal));
                            ArgUtil.NotNull(systemConnection, nameof(systemConnection));
                            executionContext.Debug("System connection retrieved for OIDC token creation");

                            VssCredentials vssCredentials = VssUtil.GetVssCredential(systemConnection);
                            var collectionUri = new Uri(executionContext.Variables.System_CollectionUrl);
                            executionContext.Debug($"Collection URI: {collectionUri}");

                            using VssConnection vssConnection = VssUtil.CreateConnection(collectionUri, vssCredentials, trace: Trace);
                            TaskHttpClient taskClient = vssConnection.GetClient<TaskHttpClient>();

                            var teamProjectId = executionContext.Variables.System_TeamProjectId ?? throw new ArgumentException("Unknown team Project ID");
                            var hostType = Enum.GetName(typeof(HostTypes), executionContext.Variables.System_HostType);
                            var planId = new Guid(executionContext.Variables.System_PlanId);
                            var jobId = new Guid(executionContext.Variables.System_JobId);

                            executionContext.Debug($"OIDC token parameters - Project ID: {teamProjectId}, Host Type: {hostType}, Plan ID: {planId}, Job ID: {jobId}, Service Connection ID: {registryEndpoint.Id}");

                            var idToken = await taskClient.CreateOidcTokenAsync(
                            scopeIdentifier: executionContext.Variables.System_TeamProjectId ?? throw new ArgumentException("Unknown team Project ID"),
                            hubName: Enum.GetName(typeof(HostTypes), executionContext.Variables.System_HostType),
                            planId: new Guid(executionContext.Variables.System_PlanId),
                            jobId: new Guid(executionContext.Variables.System_JobId),
                                serviceConnectionId: registryEndpoint.Id,
                                claims: null,
                                cancellationToken: cancellationToken
                            );

                            executionContext.Debug("OIDC token created successfully");
                            return idToken.OidcToken;
                        })
                        .Build();

                    executionContext.Debug($"Acquiring access token for resource scope: {resourceId}/.default");
                    var authenticationResult = await app.AcquireTokenForClient(new string[] { $"{resourceId}/.default" }).ExecuteAsync(cancellationToken);

                    executionContext.Debug("Access token acquired successfully using Workload Identity Federation");
                    executionContext.Debug($"Token expires at: {authenticationResult.ExpiresOn:yyyy-MM-dd HH:mm:ss} UTC");

                    return authenticationResult.AccessToken;
                }
                catch (OperationCanceledException)
                {
                    executionContext.Warning("Workload Identity Federation token acquisition was cancelled");
                    throw;
                }
                catch (AuthenticationFailedException ex)
                {
                    executionContext.Error($"Workload Identity Federation authentication failed: {ex.Message}");
                    executionContext.Error("Verify that the service connection is properly configured for Workload Identity Federation");
                    executionContext.Error("Check that the federated identity credential is correctly set up in Azure AD");
                    throw;
                }
                catch (Exception ex)
                {
                    executionContext.Error($"Unexpected error during Workload Identity Federation token acquisition: {ex.Message}");
                    executionContext.Error($"Exception type: {ex.GetType().Name}");
                    throw;
                }
            }
        }

        private async Task<string> GetAcrPasswordFromAADToken(IExecutionContext executionContext, string AADToken, string tenantId, string registryServer, string loginServer)
        {
            using (Trace.EnteringWithDuration())
            {
                CancellationToken cancellationToken = executionContext.CancellationToken;
                Uri url = new Uri(registryServer + "/oauth2/exchange");
                executionContext.Debug($"ACR OAuth2 exchange endpoint: {url}");
                const int retryLimit = 5;

                try
                {
                    using HttpClientHandler httpClientHandler = HostContext.CreateHttpClientHandler();
                    using HttpClient httpClient = new HttpClient(httpClientHandler);
                    httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Content-Type", "application/x-www-form-urlencoded");
                    executionContext.Debug("HTTP client configured for ACR token exchange");

                    List<KeyValuePair<string, string>> keyValuePairs = new List<KeyValuePair<string, string>>
                    {
                        new KeyValuePair<string, string>("grant_type", "access_token"),
                        new KeyValuePair<string, string>("service", loginServer),
                        new KeyValuePair<string, string>("tenant", tenantId),
                        new KeyValuePair<string, string>("access_token", AADToken)
                    };
                    executionContext.Debug($"Token exchange parameters configured - Grant type: access_token, Service: {loginServer}, Tenant: {tenantId}");

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

                            try
                            {
                                Dictionary<string, string> list = JsonConvert.DeserializeObject<Dictionary<string, string>>(result);
                                if (list != null && list.ContainsKey("refresh_token"))
                                {
                                    AcrPassword = list["refresh_token"];
                                    executionContext.Debug("ACR refresh token successfully extracted from response");
                                }
                                else
                                {
                                    executionContext.Error("ACR token exchange response missing refresh_token field");
                                    throw new InvalidOperationException("ACR token exchange response missing refresh_token field");
                                }
                            }
                            catch (JsonException ex)
                            {
                                executionContext.Error($"Failed to parse ACR token exchange response JSON: {ex.Message}");
                                throw new InvalidOperationException($"Failed to parse ACR token exchange response: {ex.Message}");
                            }
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

                    // Mark retrieved password as secret
                    executionContext.Debug("ACR password successfully retrieved and will be masked");
                    HostContext.SecretMasker.AddValue(AcrPassword, origin: "AcrPassword");

                    return AcrPassword;
                }
                catch (OperationCanceledException)
                {
                    executionContext.Warning("ACR token exchange was cancelled");
                    throw;
                }
                catch (HttpRequestException ex)
                {
                    executionContext.Error($"HTTP request failed during ACR token exchange: {ex.Message}");
                    throw;
                }
                catch (Exception ex)
                {
                    executionContext.Error($"Unexpected error during ACR token exchange: {ex.Message}");
                    executionContext.Error($"Exception type: {ex.GetType().Name}");
                    throw;
                }
            }
        }

        private async Task PullContainerAsync(IExecutionContext executionContext, ContainerInfo container)
        {
            using (Trace.EnteringWithDuration())
            {
                ArgUtil.NotNull(executionContext, nameof(executionContext));
                ArgUtil.NotNull(container, nameof(container));
                ArgUtil.NotNullOrEmpty(container.ContainerImage, nameof(container.ContainerImage));

                executionContext.Debug("PullContainerAsync initiated");

                Trace.Info($"Container name: {container.ContainerName}");
                Trace.Info($"Container image: {container.ContainerImage}");
                Trace.Info($"Container registry: {container.ContainerRegistryEndpoint.ToString()}");
                Trace.Info($"Container options: {container.ContainerCreateOptions}");
                Trace.Info($"Skip container image pull: {container.SkipContainerImagePull}");

                // Log registry authentication requirements
                if (container.ContainerRegistryEndpoint != Guid.Empty)
                {
                    executionContext.Debug("Private registry authentication required");
                }
                else
                {
                    executionContext.Debug("Using public Docker registry (no authentication)");
                }

                // Login to private docker registry
                string registryServer = string.Empty;
                if (container.ContainerRegistryEndpoint != Guid.Empty)
                {
                    var registryEndpoint = executionContext.Endpoints.FirstOrDefault(x => x.Type == "dockerregistry" && x.Id == container.ContainerRegistryEndpoint);
                    ArgUtil.NotNull(registryEndpoint, nameof(registryEndpoint));

                    // Log registry endpoint details for troubleshooting
                    executionContext.Debug($"Registry endpoint ID: {registryEndpoint.Id}");
                    executionContext.Debug($"Registry endpoint name: {registryEndpoint.Name ?? "not specified"}");
                    executionContext.Debug($"Registry endpoint URL: {registryEndpoint.Url?.ToString() ?? "not specified"}");

                    string username = string.Empty;
                    string password = string.Empty;
                    string registryType = string.Empty;
                    string authType = string.Empty;

                    registryEndpoint.Data?.TryGetValue("registrytype", out registryType);
                    executionContext.Debug($"Registry type determined: {registryType ?? "not specified"}");

                    if (string.Equals(registryType, "ACR", StringComparison.OrdinalIgnoreCase))
                    {
                        executionContext.Debug("Processing Azure Container Registry (ACR) authentication");

                        try
                        {
                            executionContext.Debug("Attempting to get endpoint authorization scheme...");
                            authType = registryEndpoint.Authorization?.Scheme;

                            if (string.IsNullOrEmpty(authType))
                            {
                                executionContext.Debug("Attempting to get endpoint authorization scheme as an authorization parameter...");
                                registryEndpoint.Authorization?.Parameters?.TryGetValue("scheme", out authType);
                            }

                            executionContext.Debug($"ACR authorization scheme resolved: {authType ?? "not specified"}");
                        }
                        catch (Exception ex)
                        {
                            executionContext.Debug("Failed to get endpoint authorization scheme as an authorization parameter. Will default authorization scheme to ServicePrincipal");
                            executionContext.Debug($"Exception details: {ex.Message}");
                            authType = "ServicePrincipal";
                        }

                        string loginServer = string.Empty;
                        registryEndpoint.Authorization?.Parameters?.TryGetValue("loginServer", out loginServer);
                        if (loginServer != null)
                        {
                            loginServer = loginServer.ToLower();
                            executionContext.Debug($"ACR login server: {loginServer}");
                        }
                        else
                        {
                            executionContext.Warning("ACR login server not found in endpoint parameters");
                        }

                        registryServer = $"https://{loginServer}";
                        executionContext.Debug($"Constructed registry server URL: {registryServer}");

                        if (string.Equals(authType, c_managedServiceIdentityScheme, StringComparison.OrdinalIgnoreCase))
                        {
                            executionContext.Debug("Using Managed Service Identity (MSI) authentication for ACR");

                            string tenantId = string.Empty;
                            registryEndpoint.Authorization?.Parameters?.TryGetValue(c_tenantId, out tenantId);
                            executionContext.Debug($"Tenant ID for MSI authentication: {tenantId ?? "not specified"}");

                            // Documentation says to pass username through this way
                            username = Guid.Empty.ToString("D");
                            executionContext.Debug("Set MSI username to empty GUID as per Azure documentation");

                            string AADToken = await GetMSIAccessToken(executionContext);
                            executionContext.Debug("Successfully retrieved AAD token using the MSI authentication scheme.");
                            // change to getting password from string
                            password = await GetAcrPasswordFromAADToken(executionContext, AADToken, tenantId, registryServer, loginServer);
                            executionContext.Debug("Successfully retrieved ACR password from AAD token");
                        }
                        else if (string.Equals(authType, c_workloadIdentityFederationScheme, StringComparison.OrdinalIgnoreCase))
                        {
                            executionContext.Debug("Using Workload Identity Federation authentication for ACR");

                            string tenantId = string.Empty;
                            registryEndpoint.Authorization?.Parameters?.TryGetValue(c_tenantId, out tenantId);
                            executionContext.Debug($"Tenant ID for Workload Identity Federation: {tenantId ?? "not specified"}");

                            username = Guid.Empty.ToString("D");
                            executionContext.Debug("Set Workload Identity Federation username to empty GUID");

                            string AADToken = await GetAccessTokenUsingWorkloadIdentityFederation(executionContext, registryEndpoint);
                            executionContext.Debug("Successfully retrieved AAD token using the workload identity federation authentication scheme.");
                            password = await GetAcrPasswordFromAADToken(executionContext, AADToken, tenantId, registryServer, loginServer);
                            executionContext.Debug("Successfully retrieved ACR password from AAD token");
                            executionContext.Debug("Successfully retrieved password from AAD token using the workload identity federation authentication scheme.");
                        }
                        else
                        {
                            executionContext.Debug("Using Service Principal authentication for ACR (fallback method)");
                            registryEndpoint.Authorization?.Parameters?.TryGetValue("serviceprincipalid", out username);
                            registryEndpoint.Authorization?.Parameters?.TryGetValue("serviceprincipalkey", out password);

                            executionContext.Debug($"Service Principal ID retrieved: {(!string.IsNullOrEmpty(username) ? "Yes" : "No")}");
                            executionContext.Debug($"Service Principal Key retrieved: {(!string.IsNullOrEmpty(password) ? "Yes" : "No")}");
                        }
                    }
                    else
                    {
                        executionContext.Debug("Using standard registry authentication (non-ACR)");
                        registryEndpoint.Authorization?.Parameters?.TryGetValue("registry", out registryServer);
                        registryEndpoint.Authorization?.Parameters?.TryGetValue("username", out username);
                        registryEndpoint.Authorization?.Parameters?.TryGetValue("password", out password);

                        executionContext.Debug($"Registry server from parameters: {registryServer ?? "not found"}");
                        executionContext.Debug($"Username retrieved: {(!string.IsNullOrEmpty(username) ? "Yes" : "No")}");
                        executionContext.Debug($"Password retrieved: {(!string.IsNullOrEmpty(password) ? "Yes" : "No")}");
                    }

                    ArgUtil.NotNullOrEmpty(registryServer, nameof(registryServer));
                    ArgUtil.NotNullOrEmpty(username, nameof(username));
                    ArgUtil.NotNullOrEmpty(password, nameof(password));

                    int loginExitCode = await _dockerManger.DockerLogin(
                        executionContext,
                        registryServer,
                        username,
                        password);

                    if (loginExitCode != 0)
                    {
                        throw new InvalidOperationException($"Docker login fail with exit code {loginExitCode}");
                    }
                    else
                    {
                        executionContext.Debug($"Docker login successful to {registryServer}");
                    }
                }

                try
                {
                    if (!container.SkipContainerImagePull)
                    {
                        executionContext.Output($"Pulling container image: {container.ContainerImage}");
                        executionContext.Debug("Starting image pull operation");

                        // Parse image information for better logging
                        var imageParts = container.ContainerImage.Split('/');
                        var imageRepo = imageParts.Length > 1 ? string.Join("/", imageParts.Take(imageParts.Length - 1)) : "";
                        var imageNameTag = imageParts.Last();
                        var tagSeparator = imageNameTag.LastIndexOf(':');
                        var imageName = tagSeparator > 0 ? imageNameTag.Substring(0, tagSeparator) : imageNameTag;
                        var imageTag = tagSeparator > 0 ? imageNameTag.Substring(tagSeparator + 1) : "latest";

                        executionContext.Debug($"Image repository: {imageRepo}");
                        executionContext.Debug($"Image name: {imageName}");
                        executionContext.Debug($"Image tag: {imageTag}");

                        // Handle image URL prefixing for non-Docker Hub registries
                        var originalImage = container.ContainerImage;
                        if (!string.IsNullOrEmpty(registryServer) &&
                            registryServer.IndexOf("index.docker.io", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            executionContext.Debug("Checking if image URL needs registry prefix for non-Docker Hub registry");

                            var registryServerUri = new Uri(registryServer);
                            if (!container.ContainerImage.StartsWith(registryServerUri.Authority, StringComparison.OrdinalIgnoreCase))
                            {
                                container.ContainerImage = $"{registryServerUri.Authority}/{container.ContainerImage}";
                                executionContext.Debug($"Modified image URL from '{originalImage}' to '{container.ContainerImage}'");
                            }
                            else
                            {
                                executionContext.Debug("Image URL already contains registry authority, no modification needed");
                            }
                        }
                        else
                        {
                            executionContext.Debug("Using Docker Hub registry or registry server not specified, no URL modification needed");
                        }

                        // Execute Docker pull with timing
                        var pullStartTime = DateTime.UtcNow;
                        executionContext.Debug($"Executing docker pull for image: {container.ContainerImage}");

                        int pullExitCode = await _dockerManger.DockerPull(
                            executionContext,
                            container.ContainerImage);

                        if (pullExitCode != 0)
                        {
                            throw new InvalidOperationException($"Docker pull failed with exit code {pullExitCode}");
                        }
                        else
                        {
                            executionContext.Debug($"Docker pull completed successfully for {container.ContainerImage}");
                        }
                    }
                    else
                    {
                        executionContext.Output("Skipping container image pull as requested");
                        executionContext.Debug($"SkipContainerImagePull flag is set to true for image: {container.ContainerImage}");
                    }

                    // Platform-specific container OS detection and compatibility checks
                    executionContext.Debug("Starting container OS detection and platform compatibility verification");

                    if (PlatformUtil.RunningOnMacOS)
                    {
                        container.ImageOS = PlatformUtil.OS.Linux;
                        executionContext.Debug("Container will run in Linux mode on macOS host");
                    }
                    // if running on Windows, and attempting to run linux container, require container to have node
                    else if (PlatformUtil.RunningOnWindows)
                    {
                        executionContext.Debug("Detecting container OS for Windows host compatibility");
                        string containerOS = await _dockerManger.DockerInspect(context: executionContext,
                                                                    dockerObject: container.ContainerImage,
                                                                    options: $"--format=\"{{{{.Os}}}}\"");
                        executionContext.Debug($"Detected container OS: {containerOS}");
                        if (string.Equals("linux", containerOS, StringComparison.OrdinalIgnoreCase))
                        {
                            container.ImageOS = PlatformUtil.OS.Linux;
                            executionContext.Debug("Container will run in Linux mode on Windows host");
                        }
                        else
                        {
                            executionContext.Debug("Container will run in Windows mode on Windows host");
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
        }

#pragma warning disable CA1505
        private async Task StartContainerAsync(IExecutionContext executionContext, ContainerInfo container)
        {
            using (Trace.EnteringWithDuration())
            {
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
                    executionContext.Debug($"Setting up volume mounts for Linux container");
                    string workspace = executionContext.Variables.Get(Constants.Variables.Pipeline.Workspace);
                    workspace = container.TranslateContainerPathForImageOS(PlatformUtil.HostOS, container.TranslateToContainerPath(workspace));
                    string mountWorkspace = container.TranslateToHostPath(workspace);
                    executionContext.Debug($"Workspace: {workspace}");
                    executionContext.Debug($"Mount Workspace: {mountWorkspace}");
                    container.MountVolumes.Add(new MountVolume(mountWorkspace, workspace, readOnly: container.isReadOnlyVolume(Constants.DefaultContainerMounts.Work)));

                    container.MountVolumes.Add(new MountVolume(HostContext.GetDirectory(WellKnownDirectory.Temp), container.TranslateToContainerPath(HostContext.GetDirectory(WellKnownDirectory.Temp))));
                    container.MountVolumes.Add(new MountVolume(HostContext.GetDirectory(WellKnownDirectory.Tasks), container.TranslateToContainerPath(HostContext.GetDirectory(WellKnownDirectory.Tasks)),
                        readOnly: container.isReadOnlyVolume(Constants.DefaultContainerMounts.Tasks)));
                }
                else
                {
                    executionContext.Debug($"Setting up volume mounts for Windows container");
                    container.MountVolumes.Add(new MountVolume(HostContext.GetDirectory(WellKnownDirectory.Work), container.TranslateToContainerPath(HostContext.GetDirectory(WellKnownDirectory.Work)),
                        readOnly: container.isReadOnlyVolume(Constants.DefaultContainerMounts.Work)));

                    if (AgentKnobs.AllowMountTasksReadonlyOnWindows.GetValue(executionContext).AsBoolean())
                    {
                        executionContext.Debug("Windows tasks mount enabled via agent knob");
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
                        executionContext.Debug("Creating .taskkey file for container mount");
                        File.WriteAllText(taskKeyFile, string.Empty);
                    }
                    else
                    {
                        executionContext.Debug("Found existing .taskkey file for container mount");
                    }
                    container.MountVolumes.Add(new MountVolume(taskKeyFile, container.TranslateToContainerPath(taskKeyFile)));
                }

                // Log complete mount configuration
                var mountSummary = container.MountVolumes.Select(m =>
                    $"{m.SourceVolumePath ?? "anonymous"}:{m.TargetVolumePath}{(m.ReadOnly ? ":ro" : "")}");
                executionContext.Debug($"Configured {container.MountVolumes.Count} volume mounts: {string.Join(", ", mountSummary)}");

                bool useNode20ToStartContainer = AgentKnobs.UseNode20ToStartContainer.GetValue(executionContext).AsBoolean();
                bool useAgentNode = false;

                string labelContainerStartupUsingNode20 = "container-startup-using-node-20";
                string labelContainerStartupUsingNode16 = "container-startup-using-node-16";
                string labelContainerStartupFailed = "container-startup-failed";

                string containerNodePath(string nodeFolder)
                {
                    return container.TranslateToContainerPath(Path.Combine(HostContext.GetDirectory(WellKnownDirectory.Externals), nodeFolder, "bin", $"node{IOUtil.ExeExtension}"));
                }

                string nodeContainerPath = containerNodePath(NodeHandler.NodeFolder);
                string node16ContainerPath = containerNodePath(NodeHandler.Node16Folder);
                string node20ContainerPath = containerNodePath(NodeHandler.Node20_1Folder);

                if (container.IsJobContainer)
                {
                    executionContext.Debug("Configuring Node.js for job container");
                    // See if this container brings its own Node.js
                    container.CustomNodePath = await _dockerManger.DockerInspect(context: executionContext,
                                                                        dockerObject: container.ContainerImage,
                                                                        options: $"--format=\"{{{{index .Config.Labels \\\"{_nodeJsPathLabel}\\\"}}}}\"");

                    string nodeSetInterval(string node)
                    {
                        return $"'{node}' -e 'setInterval(function(){{}}, 24 * 60 * 60 * 1000);'";
                    }

                    string useDoubleQuotes(string value)
                    {
                        return value.Replace('\'', '"');
                    }

                    if (!string.IsNullOrEmpty(container.CustomNodePath))
                    {
                        executionContext.Debug($"Container provides custom Node.js at: {container.CustomNodePath}");
                        container.ContainerCommand = useDoubleQuotes(nodeSetInterval(container.CustomNodePath));
                        container.ResultNodePath = container.CustomNodePath;
                    }
                    else if (PlatformUtil.RunningOnMacOS || (PlatformUtil.RunningOnWindows && container.ImageOS == PlatformUtil.OS.Linux))
                    {
                        // require container to have node if running on macOS, or if running on Windows and attempting to run Linux container
                        executionContext.Debug("Platform requires container to provide Node.js, using 'node' command");
                        container.CustomNodePath = "node";
                        container.ContainerCommand = useDoubleQuotes(nodeSetInterval(container.CustomNodePath));
                        container.ResultNodePath = container.CustomNodePath;
                    }
                    else
                    {
                        useAgentNode = true;
                        executionContext.Debug($"Using agent-provided Node.js. Node20 enabled: {useNode20ToStartContainer}");
                        executionContext.Debug($"Node paths - Default: {nodeContainerPath}, Node16: {node16ContainerPath}, Node20: {node20ContainerPath}");
                        string sleepCommand = useNode20ToStartContainer ? $"'{node20ContainerPath}' --version && echo '{labelContainerStartupUsingNode20}' && {nodeSetInterval(node20ContainerPath)} || '{node16ContainerPath}' --version && echo '{labelContainerStartupUsingNode16}' && {nodeSetInterval(node16ContainerPath)} || echo '{labelContainerStartupFailed}'" : nodeSetInterval(nodeContainerPath);
                        container.ContainerCommand = PlatformUtil.RunningOnWindows ? $"cmd.exe /c call {useDoubleQuotes(sleepCommand)}" : $"bash -c \"{sleepCommand}\"";
                        container.ResultNodePath = nodeContainerPath;
                    }
                }

                executionContext.Output("Creating Docker container...");
                executionContext.Debug($"Docker create command will be executed with image: {container.ContainerImage}");

                // Log resource constraints if specified
                if (!string.IsNullOrEmpty(container.ContainerCreateOptions))
                {
                    if (container.ContainerCreateOptions.Contains("--memory"))
                    {
                        executionContext.Debug("Container has memory constraints specified");
                    }
                    if (container.ContainerCreateOptions.Contains("--cpus"))
                    {
                        executionContext.Debug("Container has CPU constraints specified");
                    }
                    if (container.ContainerCreateOptions.Contains("--ulimit"))
                    {
                        executionContext.Debug("Container has ulimit constraints specified");
                    }
                }

                try
                {
                    container.ContainerId = await _dockerManger.DockerCreate(executionContext, container);
                    ArgUtil.NotNullOrEmpty(container.ContainerId, nameof(container.ContainerId));

                    executionContext.Debug($"Container created successfully. ID: {container.ContainerId.Substring(0, 12)}");
                }
                catch (Exception ex)
                {
                    executionContext.Error($"Docker container creation failed for image {container.ContainerImage}");
                    executionContext.Error($"Container options: {container.ContainerCreateOptions}");
                    executionContext.Error($"Registry endpoint: {container.ContainerRegistryEndpoint}");
                    throw new InvalidOperationException($"Failed to create container from image {container.ContainerImage}: {ex.Message}", ex);
                }

                if (container.IsJobContainer)
                {
                    executionContext.Variables.Set(Constants.Variables.Agent.ContainerId, container.ContainerId);
                    executionContext.Debug($"Set job container ID variable: {container.ContainerId}");
                }

                // Start container
                executionContext.Output("Starting Docker container...");
                int startExitCode = await _dockerManger.DockerStart(executionContext, container.ContainerId);
                if (startExitCode != 0)
                {
                    throw new InvalidOperationException($"Docker start failed with exit code {startExitCode} for container {container.ContainerId}");
                }

                executionContext.Output("Container started successfully");

                try
                {
                    executionContext.Debug("Verifying container is running...");

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
                    else if (useAgentNode && useNode20ToStartContainer)
                    {
                        bool containerStartupCompleted = false;
                        int containerStartupTimeoutInMilliseconds = 10000;
                        int delayInMilliseconds = 100;
                        int checksCount = 0;

                        while (true)
                        {
                            List<string> containerLogs = await _dockerManger.GetDockerLogs(executionContext, container.ContainerId);

                            foreach (string logLine in containerLogs)
                            {
                                if (logLine.Contains(labelContainerStartupUsingNode20))
                                {
                                    executionContext.Debug("Using Node 20 for container startup.");
                                    containerStartupCompleted = true;
                                    container.ResultNodePath = node20ContainerPath;
                                    break;
                                }
                                else if (logLine.Contains(labelContainerStartupUsingNode16))
                                {
                                    executionContext.Warning("Can not run Node 20 in container. Falling back to Node 16 for container startup.");
                                    containerStartupCompleted = true;
                                    container.ResultNodePath = node16ContainerPath;
                                    break;
                                }
                                else if (logLine.Contains(labelContainerStartupFailed))
                                {
                                    executionContext.Error("Can not run both Node 20 and Node 16 in container. Container startup failed.");
                                    containerStartupCompleted = true;
                                    break;
                                }
                            }

                            if (containerStartupCompleted)
                            {
                                break;
                            }

                            checksCount++;
                            if (checksCount * delayInMilliseconds > containerStartupTimeoutInMilliseconds)
                            {
                                executionContext.Warning("Can not get startup status from container.");
                                break;
                            }

                            await Task.Delay(delayInMilliseconds);
                        }
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
                        executionContext.Debug("Retrieving host user information...");
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

                        executionContext.Debug($"Host user: {container.CurrentUserName} (UID: {container.CurrentUserId}, GID: {container.CurrentGroupId}, Group: {container.CurrentGroupName})");
                        executionContext.Output(StringUtil.Loc("CreateUserWithSameUIDInsideContainer", container.CurrentUserId));

                        // Create an user with same uid as the agent run as user inside the container.
                        // All command execute in docker will run as Root by default,
                        // this will cause the agent on the host machine doesn't have permission to any new file/folder created inside the container.
                        // So, we create a user account with same UID inside the container and let all docker exec command run as that user.
                        string containerUserName = string.Empty;

                        // We need to find out whether there is a user with same UID inside the container
                        executionContext.Debug($"Looking for existing user with UID {container.CurrentUserId} in container...");
                        List<string> userNames = await DockerExec(executionContext, container.ContainerId, $"bash -c \"getent passwd {container.CurrentUserId} | cut -d: -f1 \"");

                        if (userNames.Count > 0)
                        {
                            executionContext.Debug($"Found {userNames.Count} potential usernames for UID {container.CurrentUserId}: {string.Join(", ", userNames)}");
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
                                    executionContext.Debug($"User {username} verification failed, checking next candidate");
                                    // check next username
                                }
                            }
                        }
                        else
                        {
                            executionContext.Debug($"No existing users found with UID {container.CurrentUserId}");
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

                        bool isAlpineBasedImage = false;
                        string detectAlpineMessage = "Alpine-based image detected.";
                        string detectAlpineCommand = $"bash -c \"if [[ -e '/etc/alpine-release' ]]; then echo '{detectAlpineMessage}'; fi\"";
                        List<string> detectAlpineOutput = await DockerExec(executionContext, container.ContainerId, detectAlpineCommand);
                        if (detectAlpineOutput.Contains(detectAlpineMessage))
                        {
                            Trace.Info(detectAlpineMessage);
                            isAlpineBasedImage = true;
                        }

                        // List of commands
                        Func<string, string> addGroup;
                        Func<string, string, string> addGroupWithId;
                        Func<string, string, string> addUserWithId;
                        Func<string, string, string, string> addUserWithIdAndGroup;
                        Func<string, string, string> addUserToGroup;

                        bool useShadowIfAlpine = false;

                        if (isAlpineBasedImage)
                        {
                            List<string> shadowInfoOutput = await DockerExec(executionContext, container.ContainerId, "apk list --installed | grep shadow");
                            bool shadowPreinstalled = false;

                            foreach (string shadowInfoLine in shadowInfoOutput)
                            {
                                if (shadowInfoLine.Contains("{shadow}", StringComparison.Ordinal))
                                {
                                    Trace.Info("The 'shadow' package is preinstalled and therefore will be used.");
                                    shadowPreinstalled = true;
                                    break;
                                }
                            }

                            bool userIdIsOutsideAdduserCommandRange = Int64.Parse(container.CurrentUserId) > 256000;

                            if (userIdIsOutsideAdduserCommandRange && !shadowPreinstalled)
                            {
                                Trace.Info("User ID is outside the range of the 'adduser' command, therefore the 'shadow' package will be installed and used.");

                                try
                                {
                                    await DockerExec(executionContext, container.ContainerId, "apk add shadow");
                                }
                                catch (InvalidOperationException)
                                {
                                    throw new InvalidOperationException(StringUtil.Loc("ApkAddShadowFailed"));
                                }
                            }

                            useShadowIfAlpine = shadowPreinstalled || userIdIsOutsideAdduserCommandRange;
                        }

                        if (isAlpineBasedImage && !useShadowIfAlpine)
                        {
                            addGroup = (groupName) => $"addgroup {groupName}";
                            addGroupWithId = (groupName, groupId) => $"addgroup -g {groupId} {groupName}";
                            addUserWithId = (userName, userId) => $"adduser -D -u {userId} {userName}";
                            addUserWithIdAndGroup = (userName, userId, groupName) => $"adduser -D -G {groupName} -u {userId} {userName}";
                            addUserToGroup = (userName, groupName) => $"addgroup {userName} {groupName}";
                        }
                        else
                        {
                            addGroup = (groupName) => $"groupadd {groupName}";
                            addGroupWithId = (groupName, groupId) => $"groupadd -g {groupId} {groupName}";
                            addUserWithId = (userName, userId) => $"useradd -m -u {userId} {userName}";
                            addUserWithIdAndGroup = (userName, userId, groupName) => $"useradd -m -g {groupName} -u {userId} {userName}";
                            addUserToGroup = (userName, groupName) => $"usermod -a -G {groupName} {userName}";
                        }

                        if (string.IsNullOrEmpty(containerUserName))
                        {
                            executionContext.Debug($"Creating new container user with UID {container.CurrentUserId}");
                            string nameSuffix = "_azpcontainer";

                            // Linux allows for a 32-character username
                            containerUserName = KeepAllowedLength(container.CurrentUserName, 32, nameSuffix);
                            executionContext.Debug($"Generated container username: {containerUserName}");

                            // Create a new user with same UID as on the host
                            string fallback = addUserWithId(containerUserName, container.CurrentUserId);

                            if (useHostGroupId)
                            {
                                try
                                {
                                    executionContext.Debug($"Creating user with matching host group ID {container.CurrentGroupId}");
                                    // Linux allows for a 32-character groupname
                                    string containerGroupName = KeepAllowedLength(container.CurrentGroupName, 32, nameSuffix);

                                    // Create a new user with the same UID and the same GID as on the host
                                    await DockerExec(executionContext, container.ContainerId, addGroupWithId(containerGroupName, container.CurrentGroupId));
                                    await DockerExec(executionContext, container.ContainerId, addUserWithIdAndGroup(containerUserName, container.CurrentUserId, containerGroupName));
                                    executionContext.Debug($"Successfully created user {containerUserName} with group {containerGroupName}");
                                }
                                catch (Exception ex) when (ex is InvalidOperationException)
                                {
                                    Trace.Info($"Falling back to the '{fallback}' command.");
                                    await DockerExec(executionContext, container.ContainerId, fallback);
                                    executionContext.Debug($"Created user using fallback command: {fallback}");
                                }
                            }
                            else
                            {
                                await DockerExec(executionContext, container.ContainerId, fallback);
                                executionContext.Debug($"Created user using standard command: {fallback}");
                            }
                        }
                        else
                        {
                            executionContext.Debug($"Using existing container user: {containerUserName}");
                        }

                        executionContext.Output(StringUtil.Loc("GrantContainerUserSUDOPrivilege", containerUserName));

                        string sudoGroupName = "azure_pipelines_sudo";

                        // Create a new group for giving sudo permission
                        await DockerExec(executionContext, container.ContainerId, addGroup(sudoGroupName));

                        // Add the new created user to the new created sudo group.
                        await DockerExec(executionContext, container.ContainerId, addUserToGroup(containerUserName, sudoGroupName));

                        // Allow the new sudo group run any sudo command without providing password.
                        await DockerExec(executionContext, container.ContainerId, $"su -c \"echo '%{sudoGroupName} ALL=(ALL:ALL) NOPASSWD:ALL' >> /etc/sudoers\"");

                        if (AgentKnobs.SetupDockerGroup.GetValue(executionContext).AsBoolean())
                        {
                            executionContext.Debug($"Docker group setup enabled via agent knob");
                            executionContext.Output(StringUtil.Loc("AllowContainerUserRunDocker", containerUserName));
                            // Get docker.sock group id on Host
                            string statFormatOption = "-c %g";
                            if (PlatformUtil.RunningOnMacOS)
                            {
                                statFormatOption = "-f %g";
                            }
                            string dockerSockGroupId = (await ExecuteCommandAsync(executionContext, "stat", $"{statFormatOption} /var/run/docker.sock")).FirstOrDefault();
                            executionContext.Debug($"Host docker.sock group ID: {dockerSockGroupId}");

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
                                executionContext.Debug($"Creating new docker group '{existingGroupName}' with GID {dockerSockGroupId}");
                                await DockerExec(executionContext, container.ContainerId, addGroupWithId(existingGroupName, dockerSockGroupId));
                            }
                            else
                            {
                                executionContext.Debug($"Found existing docker group '{existingGroupName}' with matching GID {dockerSockGroupId}");
                            }
                            // Add the new created user to the docker socket group.
                            await DockerExec(executionContext, container.ContainerId, addUserToGroup(containerUserName, existingGroupName));

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

                        if (PlatformUtil.RunningOnLinux)
                        {
                            bool useNode20InUnsupportedSystem = AgentKnobs.UseNode20InUnsupportedSystem.GetValue(executionContext).AsBoolean();

                            if (!useNode20InUnsupportedSystem)
                            {
                                var node20 = container.TranslateToContainerPath(Path.Combine(HostContext.GetDirectory(WellKnownDirectory.Externals), NodeHandler.Node20_1Folder, "bin", $"node{IOUtil.ExeExtension}"));

                                string node20TestCmd = $"bash -c \"{node20} -v\"";
                                List<string> nodeVersionOutput = await DockerExec(executionContext, container.ContainerId, node20TestCmd, noExceptionOnError: true);

                                container.NeedsNode16Redirect = WorkerUtilities.IsCommandResultGlibcError(executionContext, nodeVersionOutput, out string nodeInfoLine);

                                if (container.NeedsNode16Redirect)
                                {
                                    PublishTelemetry(
                                        executionContext,
                                        new Dictionary<string, string>
                                        {
                                        {  "ContainerNode20to16Fallback", container.NeedsNode16Redirect.ToString() }
                                        }
                                    );
                                }
                            }

                        }

                        if (!string.IsNullOrEmpty(containerUserName))
                        {
                            container.CurrentUserName = containerUserName;
                            executionContext.Debug($"Container user setup completed. Final user: {containerUserName}");
                        }

                        executionContext.Debug("Container user setup completed successfully");
                    }
                }

                executionContext.Output("Container startup completed successfully");
                Trace.Info($"StartContainerAsync completed for {container.ContainerName} ({container.ContainerId?.Substring(0, 12)})");
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
            using (Trace.EnteringWithDuration())
            {
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
        }

        private async Task RemoveContainerNetworkAsync(IExecutionContext executionContext, string network)
        {
            using (Trace.EnteringWithDuration())
            {
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

        private async Task<List<string>> DockerExec(IExecutionContext context, string containerId, string command, bool noExceptionOnError = false)
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
                if (!noExceptionOnError)
                {
                    throw new InvalidOperationException(message);
                }
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
#pragma warning disable CA1416 // SupportedOSPlatform checks not respected in lambda usage
                // service CExecSvc is Container Execution Agent.
                ServiceController[] scServices = ServiceController.GetServices();
                if (scServices.Any(x => String.Equals(x.ServiceName, "cexecsvc", StringComparison.OrdinalIgnoreCase) && x.Status == ServiceControllerStatus.Running))
                {
                    throw new NotSupportedException(StringUtil.Loc("AgentAlreadyInsideContainer"));
                }
#pragma warning restore CA1416
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

            executionContext.Debug("Retrieving Windows release ID from registry...");
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

        private void PublishTelemetry(
            IExecutionContext executionContext,
            object telemetryData,
            string feature = nameof(ContainerOperationProvider))
        {
            var cmd = new Command("telemetry", "publish")
            {
                Data = JsonConvert.SerializeObject(telemetryData, Formatting.None)
            };
            cmd.Properties.Add("area", "PipelinesTasks");
            cmd.Properties.Add("feature", feature);

            var publishTelemetryCmd = new TelemetryCommandExtension();
            publishTelemetryCmd.Initialize(HostContext);
            publishTelemetryCmd.ProcessCommand(executionContext, cmd);
        }
    }
}
