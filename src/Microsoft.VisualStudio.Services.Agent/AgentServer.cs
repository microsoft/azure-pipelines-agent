// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.DistributedTask.WebApi;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.OAuth;
using Agent.Sdk.Util;

namespace Microsoft.VisualStudio.Services.Agent
{
    public enum AgentConnectionType
    {
        Generic,
        MessageQueue,
        JobRequest
    }

    [ServiceLocator(Default = typeof(AgentServer))]
    public interface IAgentServer : IAgentService
    {
        Task ConnectAsync(Uri serverUrl, VssCredentials credentials);

        Task RefreshConnectionAsync(AgentConnectionType connectionType, TimeSpan timeout);

        void SetConnectionTimeout(AgentConnectionType connectionType, TimeSpan timeout);

        // Configuration
        Task<TaskAgent> AddAgentAsync(Int32 agentPoolId, TaskAgent agent);
        Task DeleteAgentAsync(int agentPoolId, int agentId);
        Task<List<TaskAgentPool>> GetAgentPoolsAsync(string agentPoolName, TaskAgentPoolType poolType = TaskAgentPoolType.Automation);
        Task<List<TaskAgent>> GetAgentsAsync(int agentPoolId, string agentName = null);
        Task<TaskAgent> UpdateAgentAsync(int agentPoolId, TaskAgent agent);

        // messagequeue
        Task<TaskAgentSession> CreateAgentSessionAsync(Int32 poolId, TaskAgentSession session, CancellationToken cancellationToken);
        Task DeleteAgentMessageAsync(Int32 poolId, Int64 messageId, Guid sessionId, CancellationToken cancellationToken);
        Task DeleteAgentSessionAsync(Int32 poolId, Guid sessionId, CancellationToken cancellationToken);
        Task<TaskAgentMessage> GetAgentMessageAsync(Int32 poolId, Guid sessionId, Int64? lastMessageId, CancellationToken cancellationToken);

        // job request
        Task<TaskAgentJobRequest> GetAgentRequestAsync(int poolId, long requestId, CancellationToken cancellationToken);
        Task<TaskAgentJobRequest> RenewAgentRequestAsync(int poolId, long requestId, Guid lockToken, CancellationToken cancellationToken);
        Task<TaskAgentJobRequest> FinishAgentRequestAsync(int poolId, long requestId, Guid lockToken, DateTime finishTime, TaskResult result, CancellationToken cancellationToken);

        // agent package
        Task<List<PackageMetadata>> GetPackagesAsync(string packageType, string platform, int top, CancellationToken cancellationToken);
        Task<PackageMetadata> GetPackageAsync(string packageType, string platform, string version, CancellationToken cancellationToken);

        // agent update
        Task<TaskAgent> UpdateAgentUpdateStateAsync(int agentPoolId, int agentId, string currentState);
    }

    public sealed class AgentServer : AgentService, IAgentServer
    {
        private bool _hasGenericConnection;
        private bool _hasMessageConnection;
        private bool _hasRequestConnection;
        private VssConnection _genericConnection;
        private VssConnection _messageConnection;
        private VssConnection _requestConnection;
        private TaskAgentHttpClient _genericTaskAgentClient;
        private TaskAgentHttpClient _messageTaskAgentClient;
        private TaskAgentHttpClient _requestTaskAgentClient;

        public async Task ConnectAsync(Uri serverUrl, VssCredentials credentials)
        {

            // Establish the first connection before doing the rest in parallel to eliminate the redundant 401s.
            // issue: https://github.com/microsoft/azure-pipelines-agent/issues/3149
            Task<VssConnection> task1 = EstablishVssConnection(serverUrl, credentials, TimeSpan.FromSeconds(100));

            _genericConnection = await task1;

            Task<VssConnection> task2 = EstablishVssConnection(serverUrl, credentials, TimeSpan.FromSeconds(60));
            Task<VssConnection> task3 = EstablishVssConnection(serverUrl, credentials, TimeSpan.FromSeconds(60));

            await Task.WhenAll(task2, task3);

            _messageConnection = task2.Result;
            _requestConnection = task3.Result;

            _genericTaskAgentClient = _genericConnection.GetClient<TaskAgentHttpClient>();
            _messageTaskAgentClient = _messageConnection.GetClient<TaskAgentHttpClient>();
            _requestTaskAgentClient = _requestConnection.GetClient<TaskAgentHttpClient>();

            _hasGenericConnection = true;
            _hasMessageConnection = true;
            _hasRequestConnection = true;
        }

        // Refresh connection is best effort. it should never throw exception
        public async Task RefreshConnectionAsync(AgentConnectionType connectionType, TimeSpan timeout)
        {
            Trace.Info($"Refresh {connectionType} VssConnection to get on a different AFD node.");
            VssConnection newConnection = null;
            
            switch (connectionType)
            {
                case AgentConnectionType.MessageQueue:
                    try
                    {
                        _hasMessageConnection = false;
                        newConnection = await EstablishVssConnection(_messageConnection.Uri, _messageConnection.Credentials, timeout);
                        var client = newConnection.GetClient<TaskAgentHttpClient>();
                        _messageConnection = newConnection;
                        _messageTaskAgentClient = client;
                    }
                    catch (SocketException ex)
                    {
                        ExceptionsUtil.HandleSocketException(ex, _requestConnection.Uri.ToString(), (msg) => Trace.Error(msg));
                        newConnection?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        // Enhanced diagnostics for connection failures
                        if (ex is VssUnauthorizedException)
                        {
                            Trace.Error($"Authentication failure during {connectionType} refresh - token may need renewal");
                        }
                        else if (ex is System.Net.Http.HttpRequestException httpEx && httpEx.InnerException is SocketException sockEx &&
                                 (sockEx.SocketErrorCode == SocketError.AddressAlreadyInUse || sockEx.SocketErrorCode == SocketError.AddressNotAvailable))
                        {
                            Trace.Error($"Socket exhaustion detected during {connectionType} refresh: {sockEx.SocketErrorCode}");
                        }
                        
                        Trace.Error($"Catch exception during reset {connectionType} connection.");
                        Trace.Error(ex);
                        newConnection?.Dispose();
                    }
                    finally
                    {
                        _hasMessageConnection = true;
                    }
                    break;
                case AgentConnectionType.JobRequest:
                    try
                    {
                        _hasRequestConnection = false;
                        newConnection = await EstablishVssConnection(_requestConnection.Uri, _requestConnection.Credentials, timeout);
                        var client = newConnection.GetClient<TaskAgentHttpClient>();
                        _requestConnection = newConnection;
                        _requestTaskAgentClient = client;
                    }
                    catch (SocketException ex)
                    {
                        ExceptionsUtil.HandleSocketException(ex, _requestConnection.Uri.ToString(), (msg) => Trace.Error(msg));
                        newConnection?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        // Enhanced diagnostics for connection failures
                        if (ex is VssUnauthorizedException)
                        {
                            Trace.Error($"Authentication failure during {connectionType} refresh - token may need renewal");
                        }
                        else if (ex is System.Net.Http.HttpRequestException httpEx && httpEx.InnerException is SocketException sockEx &&
                                 (sockEx.SocketErrorCode == SocketError.AddressAlreadyInUse || sockEx.SocketErrorCode == SocketError.AddressNotAvailable))
                        {
                            Trace.Error($"Socket exhaustion detected during {connectionType} refresh: {sockEx.SocketErrorCode}");
                        }
                        
                        Trace.Error($"Catch exception during reset {connectionType} connection.");
                        Trace.Error(ex);
                        newConnection?.Dispose();
                    }
                    finally
                    {
                        _hasRequestConnection = true;
                    }
                    break;
                case AgentConnectionType.Generic:
                    try
                    {
                        _hasGenericConnection = false;
                        newConnection = await EstablishVssConnection(_genericConnection.Uri, _genericConnection.Credentials, timeout);
                        var client = newConnection.GetClient<TaskAgentHttpClient>();
                        _genericConnection = newConnection;
                        _genericTaskAgentClient = client;
                    }
                    catch (SocketException ex)
                    {
                        ExceptionsUtil.HandleSocketException(ex, _requestConnection.Uri.ToString(), (msg) => Trace.Error(msg));
                        newConnection?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        // Enhanced diagnostics for connection failures
                        if (ex is VssUnauthorizedException)
                        {
                            Trace.Error($"Authentication failure during {connectionType} refresh - token may need renewal");
                        }
                        else if (ex is System.Net.Http.HttpRequestException httpEx && httpEx.InnerException is SocketException sockEx &&
                                 (sockEx.SocketErrorCode == SocketError.AddressAlreadyInUse || sockEx.SocketErrorCode == SocketError.AddressNotAvailable))
                        {
                            Trace.Error($"Socket exhaustion detected during {connectionType} refresh: {sockEx.SocketErrorCode}");
                        }
                        
                        Trace.Error($"Catch exception during reset {connectionType} connection.");
                        Trace.Error(ex);
                        newConnection?.Dispose();
                    }
                    finally
                    {
                        _hasGenericConnection = true;
                    }
                    break;
                default:
                    Trace.Error($"Unexpected connection type: {connectionType}.");
                    break;
            }
        }

        public void SetConnectionTimeout(AgentConnectionType connectionType, TimeSpan timeout)
        {
            Trace.Info($"Set {connectionType} VssConnection's timeout to {timeout.TotalSeconds} seconds.");
            switch (connectionType)
            {
                case AgentConnectionType.JobRequest:
                    _requestConnection.Settings.SendTimeout = timeout;
                    break;
                case AgentConnectionType.MessageQueue:
                    _messageConnection.Settings.SendTimeout = timeout;
                    break;
                case AgentConnectionType.Generic:
                    _genericConnection.Settings.SendTimeout = timeout;
                    break;
                default:
                    Trace.Error($"Unexpected connection type: {connectionType}.");
                    break;
            }
        }

        private async Task<VssConnection> EstablishVssConnection(Uri serverUrl, VssCredentials credentials, TimeSpan timeout)
        {
            Trace.Info($"Starting VssConnection establishment with {timeout.TotalSeconds} seconds timeout.");
            
            // Enhanced logging for connection diagnostics
            Trace.Info($"Establishing connection to: {serverUrl}");
            Trace.Info($"Connection timeout: {timeout.TotalSeconds}s");
            
            // Log credential information for debugging
            if (credentials?.Federated is VssOAuthAccessTokenCredential oauthCred)
            {
                // Don't log the actual token, just metadata about it
                Trace.Info("Using OAuth access token credential");
                // Note: We cannot safely access token content or expiry without risking exposing secrets
                // The VssOAuthAccessTokenCredential doesn't expose expiry information publicly
            }
            else if (credentials?.Federated != null)
            {
                var credType = credentials.Federated.GetType().Name;
                Trace.Info($"Using federated credential type: {credType}");
            }
            else if (credentials?.Storage != null)
            {
                Trace.Info("Using stored credential (PAT/Basic)");
            }
            else
            {
                Trace.Warning("Unknown credential type or null credentials");
            }
            
            int attemptCount = 5;
            var agentCertManager = HostContext.GetService<IAgentCertificateManager>();

            while (attemptCount-- > 0)
            {
                var connection = VssUtil.CreateConnection(serverUrl, credentials, timeout: timeout, trace: Trace, skipServerCertificateValidation: agentCertManager.SkipServerCertificateValidation);
                var connectStartTime = DateTime.UtcNow;
                try
                {
                    Trace.Info($"Attempting connection (attempt {5 - attemptCount}/5)...");
                    
                    await connection.ConnectAsync();
                    
                    var connectDuration = DateTime.UtcNow - connectStartTime;
                    Trace.Info($"Connection established successfully in {connectDuration.TotalMilliseconds:F0}ms");
                    
                    // Log authentication state
                    if (connection.HasAuthenticated)
                    {
                        Trace.Info("Connection authenticated successfully");
                        try
                        {
                            var authIdentity = connection.AuthorizedIdentity;
                            if (authIdentity != null)
                            {
                                Trace.Info($"Authenticated as: {authIdentity.DisplayName} (ID: {authIdentity.Id})");
                            }
                        }
                        catch (Exception ex)
                        {
                            Trace.Warning($"Could not retrieve authenticated identity info: {ex.Message}");
                        }
                    }
                    else
                    {
                        Trace.Warning("Connection created but authentication status unclear");
                    }
                    
                    Trace.Info("VssConnection establishment completed successfully");
                    
                    return connection;
                }
                catch (Exception ex) when (attemptCount > 0)
                {
                    var connectDuration = DateTime.UtcNow - connectStartTime;
                    Trace.Info($"Connection attempt failed after {connectDuration.TotalMilliseconds:F0}ms. {attemptCount} attempt(s) left.");
                    
                    // Enhanced error logging for different types of failures
                    if (ex is System.Net.Http.HttpRequestException httpEx)
                    {
                        Trace.Warning($"HTTP request exception during connection: {httpEx.Message}");
                        if (httpEx.InnerException != null)
                        {
                            Trace.Warning($"Inner exception: {httpEx.InnerException.GetType().Name}: {httpEx.InnerException.Message}");
                            
                            // Check for socket-specific issues in inner exceptions
                            if (httpEx.InnerException is System.Net.Sockets.SocketException sockInnerEx)
                            {
                                Trace.Warning($"*** SOCKET ERROR in HTTP request *** Error: {sockInnerEx.SocketErrorCode} ({sockInnerEx.ErrorCode})");
                                
                                if (sockInnerEx.SocketErrorCode == System.Net.Sockets.SocketError.AddressAlreadyInUse ||
                                    sockInnerEx.SocketErrorCode == System.Net.Sockets.SocketError.AddressNotAvailable ||
                                    sockInnerEx.SocketErrorCode == System.Net.Sockets.SocketError.TooManyOpenSockets)
                                {
                                    Trace.Warning("*** SOCKET EXHAUSTION DETECTED *** Consider enabling legacy HTTP handler for connection pool isolation");
                                }
                            }
                        }
                    }
                    else if (ex is System.Net.Sockets.SocketException sockEx)
                    {
                        Trace.Warning($"Socket exception during connection: Error {sockEx.ErrorCode} - {sockEx.Message}");
                        Trace.Warning($"Socket error type: {sockEx.SocketErrorCode}");
                        
                        // Provide specific guidance for socket exhaustion
                        if (sockEx.SocketErrorCode == System.Net.Sockets.SocketError.AddressAlreadyInUse ||
                            sockEx.SocketErrorCode == System.Net.Sockets.SocketError.AddressNotAvailable ||
                            sockEx.SocketErrorCode == System.Net.Sockets.SocketError.TooManyOpenSockets)
                        {
                            Trace.Warning("*** SOCKET EXHAUSTION DETECTED *** This may be caused by PowerShellOnTargetMachine or similar tasks using many connections");
                        }
                    }
                    else if (ex is VssUnauthorizedException authEx)
                    {
                        Trace.Warning($"Authentication failed during connection: {authEx.Message}");
                        // This could indicate token expiry or invalid credentials
                        Trace.Warning("*** AUTHENTICATION FAILURE *** This may indicate expired or invalid authentication credentials");
                    }
                    else if (ex is System.Threading.Tasks.TaskCanceledException timeoutEx)
                    {
                        Trace.Warning($"Connection timeout: {timeoutEx.Message}");
                        if (timeoutEx.InnerException is System.TimeoutException)
                        {
                            Trace.Warning("Connection timed out - this may indicate server load or network issues");
                        }
                    }
                    else
                    {
                        Trace.Warning($"Unexpected connection error: {ex.GetType().Name}: {ex.Message}");
                    }
                    
                    Trace.Error(ex);

                    await HostContext.Delay(TimeSpan.FromMilliseconds(100), CancellationToken.None);
                    connection?.Dispose();
                }
            }

            // should never reach here.
            throw new InvalidOperationException(nameof(EstablishVssConnection));
        }

        private void CheckConnection(AgentConnectionType connectionType)
        {
            switch (connectionType)
            {
                case AgentConnectionType.Generic:
                    if (!_hasGenericConnection)
                    {
                        throw new InvalidOperationException($"SetConnection {AgentConnectionType.Generic}");
                    }
                    break;
                case AgentConnectionType.JobRequest:
                    if (!_hasRequestConnection)
                    {
                        throw new InvalidOperationException($"SetConnection {AgentConnectionType.JobRequest}");
                    }
                    break;
                case AgentConnectionType.MessageQueue:
                    if (!_hasMessageConnection)
                    {
                        throw new InvalidOperationException($"SetConnection {AgentConnectionType.MessageQueue}");
                    }
                    break;
                default:
                    throw new NotSupportedException(connectionType.ToString());
            }
        }

        //-----------------------------------------------------------------
        // Configuration
        //-----------------------------------------------------------------

        public Task<List<TaskAgentPool>> GetAgentPoolsAsync(string agentPoolName, TaskAgentPoolType poolType = TaskAgentPoolType.Automation)
        {
            CheckConnection(AgentConnectionType.Generic);
            return _genericTaskAgentClient.GetAgentPoolsAsync(agentPoolName, poolType: poolType);
        }

        public Task<TaskAgent> AddAgentAsync(Int32 agentPoolId, TaskAgent agent)
        {
            CheckConnection(AgentConnectionType.Generic);
            return _genericTaskAgentClient.AddAgentAsync(agentPoolId, agent);
        }

        public Task<List<TaskAgent>> GetAgentsAsync(int agentPoolId, string agentName = null)
        {
            CheckConnection(AgentConnectionType.Generic);
            return _genericTaskAgentClient.GetAgentsAsync(agentPoolId, agentName, false);
        }

        public Task<TaskAgent> UpdateAgentAsync(int agentPoolId, TaskAgent agent)
        {
            CheckConnection(AgentConnectionType.Generic);
            return _genericTaskAgentClient.ReplaceAgentAsync(agentPoolId, agent);
        }

        public Task DeleteAgentAsync(int agentPoolId, int agentId)
        {
            CheckConnection(AgentConnectionType.Generic);
            return _genericTaskAgentClient.DeleteAgentAsync(agentPoolId, agentId);
        }

        //-----------------------------------------------------------------
        // MessageQueue
        //-----------------------------------------------------------------

        public Task<TaskAgentSession> CreateAgentSessionAsync(Int32 poolId, TaskAgentSession session, CancellationToken cancellationToken)
        {
            CheckConnection(AgentConnectionType.MessageQueue);
            return _messageTaskAgentClient.CreateAgentSessionAsync(poolId, session, cancellationToken: cancellationToken);
        }

        public Task DeleteAgentMessageAsync(Int32 poolId, Int64 messageId, Guid sessionId, CancellationToken cancellationToken)
        {
            CheckConnection(AgentConnectionType.MessageQueue);
            return _messageTaskAgentClient.DeleteMessageAsync(poolId, messageId, sessionId, cancellationToken: cancellationToken);
        }

        public Task DeleteAgentSessionAsync(Int32 poolId, Guid sessionId, CancellationToken cancellationToken)
        {
            CheckConnection(AgentConnectionType.MessageQueue);
            return _messageTaskAgentClient.DeleteAgentSessionAsync(poolId, sessionId, cancellationToken: cancellationToken);
        }

        public Task<TaskAgentMessage> GetAgentMessageAsync(Int32 poolId, Guid sessionId, Int64? lastMessageId, CancellationToken cancellationToken)
        {
            CheckConnection(AgentConnectionType.MessageQueue);
            return _messageTaskAgentClient.GetMessageAsync(poolId, sessionId, lastMessageId, cancellationToken: cancellationToken);
        }

        //-----------------------------------------------------------------
        // JobRequest
        //-----------------------------------------------------------------

        public Task<TaskAgentJobRequest> RenewAgentRequestAsync(int poolId, long requestId, Guid lockToken, CancellationToken cancellationToken = default(CancellationToken))
        {
            CheckConnection(AgentConnectionType.JobRequest);
            return _requestTaskAgentClient.RenewAgentRequestAsync(poolId, requestId, lockToken, cancellationToken: cancellationToken);
        }

        public Task<TaskAgentJobRequest> FinishAgentRequestAsync(int poolId, long requestId, Guid lockToken, DateTime finishTime, TaskResult result, CancellationToken cancellationToken = default(CancellationToken))
        {
            CheckConnection(AgentConnectionType.JobRequest);
            return _requestTaskAgentClient.FinishAgentRequestAsync(poolId, requestId, lockToken, finishTime, result, cancellationToken: cancellationToken);
        }

        public Task<TaskAgentJobRequest> GetAgentRequestAsync(int poolId, long requestId, CancellationToken cancellationToken = default(CancellationToken))
        {
            CheckConnection(AgentConnectionType.JobRequest);
            return _requestTaskAgentClient.GetAgentRequestAsync(poolId, requestId, cancellationToken: cancellationToken);
        }

        //-----------------------------------------------------------------
        // Agent Package
        //-----------------------------------------------------------------
        public Task<List<PackageMetadata>> GetPackagesAsync(string packageType, string platform, int top, CancellationToken cancellationToken)
        {
            CheckConnection(AgentConnectionType.Generic);
            return _genericTaskAgentClient.GetPackagesAsync(packageType, platform, top, cancellationToken: cancellationToken);
        }

        public Task<PackageMetadata> GetPackageAsync(string packageType, string platform, string version, CancellationToken cancellationToken)
        {
            CheckConnection(AgentConnectionType.Generic);
            return _genericTaskAgentClient.GetPackageAsync(packageType, platform, version, cancellationToken: cancellationToken);
        }

        public Task<TaskAgent> UpdateAgentUpdateStateAsync(int agentPoolId, int agentId, string currentState)
        {
            CheckConnection(AgentConnectionType.Generic);
            return _genericTaskAgentClient.UpdateAgentUpdateStateAsync(agentPoolId, agentId, currentState);
        }
    }
}