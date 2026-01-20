// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Capabilities;
using Microsoft.VisualStudio.Services.Agent.Listener.Configuration;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Common;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.Services.OAuth;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Agent.Sdk.Knob;
using Agent.Sdk.Util;
using Agent.Listener.Configuration;

namespace Microsoft.VisualStudio.Services.Agent.Listener
{
    [ServiceLocator(Default = typeof(MessageListener))]
    public interface IMessageListener : IAgentService
    {
        Task<Boolean> CreateSessionAsync(CancellationToken token);
        Task DeleteSessionAsync();
        Task<TaskAgentMessage> GetNextMessageAsync(CancellationToken token);
        Task KeepAlive(CancellationToken token);
        Task DeleteMessageAsync(TaskAgentMessage message);
    }

    public sealed class MessageListener : AgentService, IMessageListener
    {
        private long? _lastMessageId;
        private AgentSettings _settings;
        private ITerminal _term;
        private IAgentServer _agentServer;
        private IFeatureFlagProvider _featureFlagProvider;
        private TaskAgentSession _session;
        private TimeSpan _getNextMessageRetryInterval;
        private TimeSpan _keepAliveRetryInterval;
        private bool _enableProgressiveBackoff = false;
        private bool _progressiveBackoffFetched = false;
        private TimeSpan _sessionCreationRetryInterval = TimeSpan.FromSeconds(30);
        private readonly TimeSpan _sessionConflictRetryLimit = TimeSpan.FromMinutes(4);
        private readonly TimeSpan _clockSkewRetryLimit = TimeSpan.FromMinutes(30);
        private readonly Dictionary<string, int> _sessionCreationExceptionTracker = new Dictionary<string, int>();
        private Stopwatch _sessionConflictStopwatch;
        private Stopwatch _clockSkewStopwatch;

        public override void Initialize(IHostContext hostContext)
        {
            base.Initialize(hostContext);

            _term = HostContext.GetService<ITerminal>();
            _agentServer = HostContext.GetService<IAgentServer>();
            _featureFlagProvider = HostContext.GetService<IFeatureFlagProvider>();
        }

        public async Task<Boolean> CreateSessionAsync(CancellationToken token)
        {
            Trace.Entering();

            // Settings
            var configManager = HostContext.GetService<IConfigurationManager>();
            _settings = configManager.LoadSettings();
            var serverUrl = _settings.ServerUrl;
            Trace.Info(_settings);

            // Capabilities.
            _term.WriteLine(StringUtil.Loc("ScanToolCapabilities"));
            Dictionary<string, string> systemCapabilities = await HostContext.GetService<ICapabilitiesManager>().GetCapabilitiesAsync(_settings, token);

            // Create connection.
            Trace.Info("Loading Credentials");
            var credMgr = HostContext.GetService<ICredentialManager>();
            VssCredentials creds = credMgr.LoadCredentials();

            var agent = new TaskAgentReference
            {
                Id = _settings.AgentId,
                Name = _settings.AgentName,
                Version = BuildConstants.AgentPackage.Version,
                OSDescription = RuntimeInformation.OSDescription,
            };
            string sessionName = $"{Environment.MachineName ?? "AGENT"}";
            var taskAgentSession = new TaskAgentSession(sessionName, agent, systemCapabilities);

            string errorMessage = string.Empty;
            bool encounteringError = false;
            int continuousError = 0;

            _term.WriteLine(StringUtil.Loc("ConnectToServer"));
            while (true)
            {
                token.ThrowIfCancellationRequested();
                Trace.Info($"Attempt to create session.");
                try
                {
                    Trace.Info("Connecting to the Agent Server...");
                    await _agentServer.ConnectAsync(new Uri(serverUrl), creds);
                    Trace.Info("VssConnection created");
                    // Fetch progressive backoff feature flag now that we have a connection
                    if (!_progressiveBackoffFetched)
                    {
                        try
                        {
                            var progressiveBackoffFeatureFlag = await _featureFlagProvider.GetFeatureFlagAsync(HostContext, "DistributedTask.Agent.EnableProgressiveRetryBackoff", Trace);
                            _enableProgressiveBackoff = progressiveBackoffFeatureFlag?.EffectiveState == "On";
                            _progressiveBackoffFetched = true;
                            Trace.Info($"Progressive backoff feature flag fetched: {_enableProgressiveBackoff}");
                        }
                        catch (Exception ex)
                        {
                            // Will use default value (false)
                            Trace.Info($"Unable to fetch progressive backoff feature flag: {ex.Message}");
                        }
                    }
                    
                    _session = await _agentServer.CreateAgentSessionAsync(
                                                        _settings.PoolId,
                                                        taskAgentSession,
                                                        token);

                    Trace.Info($"Session created.");
                    // Reset stopwatches on successful session creation
                    _sessionConflictStopwatch?.Reset();
                    _clockSkewStopwatch?.Reset();
                    
                    if (encounteringError)
                    {
                        _term.WriteLine(StringUtil.Loc("QueueConnected", DateTime.UtcNow));
                        _sessionCreationExceptionTracker.Clear();
                        encounteringError = false;
                        continuousError = 0;
                    }

                    return true;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    Trace.Info("Session creation has been cancelled.");
                    throw;
                }
                catch (TaskAgentAccessTokenExpiredException)
                {
                    Trace.Info("Agent OAuth token has been revoked. Session creation failed.");
                    throw;
                }
                catch (SocketException ex)
                {
                    ExceptionsUtil.HandleSocketException(ex, serverUrl, (msg) => Trace.Error(msg));
                    throw;
                }
                catch (Exception ex)
                {
                    Trace.Error("Catch exception during create session.");
                    Trace.Error(ex);

                    if (!IsSessionCreationExceptionRetriable(ex))
                    {
                        _term.WriteError(StringUtil.Loc("SessionCreateFailed", ex.Message));
                        return false;
                    }

                    continuousError++;
                    
                    //We use the cached value if available, otherwise default behavior
                    bool enableProgressiveBackoff = _enableProgressiveBackoff;
                    string backoffSource = _progressiveBackoffFetched ? "FeatureFlag (cached)" : "Default (not fetched)";
                    
                    Trace.Info($"CreateSession - Progressive backoff enabled: {enableProgressiveBackoff} (source: {backoffSource})");
                    
                    if (enableProgressiveBackoff)
                    {
                        if (continuousError <= 2)
                        {
                            // random backoff [15, 30]
                            _sessionCreationRetryInterval = BackoffTimerHelper.GetRandomBackoff(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), _sessionCreationRetryInterval);
                        }
                        else if (continuousError > 2 && continuousError <= 5)
                        {
                            // random backoff [60, 90]
                            _sessionCreationRetryInterval = BackoffTimerHelper.GetRandomBackoff(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(90), _sessionCreationRetryInterval);
                        }
                        else
                        {
                            // more aggressive backoff [150, 200]
                            _sessionCreationRetryInterval = BackoffTimerHelper.GetRandomBackoff(TimeSpan.FromSeconds(150), TimeSpan.FromSeconds(200), _sessionCreationRetryInterval);
                        }
                    }
                    else
                    {
                        // Default behavior: fixed 30 seconds
                        _sessionCreationRetryInterval = TimeSpan.FromSeconds(30);
                    }

                    if (!encounteringError) //print the message only on the first error
                    {
                        _term.WriteError(StringUtil.Loc("QueueConError", DateTime.UtcNow, ex.Message, _sessionCreationRetryInterval.TotalSeconds));
                        encounteringError = true;
                    }

                    Trace.Info($"Unable to create session in CreateSessionAsync (attempt {continuousError})");
                    Trace.Info(StringUtil.Format("Sleeping for {0} seconds before retrying.", _sessionCreationRetryInterval.TotalSeconds));
                    await HostContext.Delay(_sessionCreationRetryInterval, token);
                }
            }
        }

        public async Task DeleteSessionAsync()
        {
            if (_session != null && _session.SessionId != Guid.Empty)
            {
                using (var ts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                {
                    await _agentServer.DeleteAgentSessionAsync(_settings.PoolId, _session.SessionId, ts.Token);
                }
            }
        }

        public async Task<TaskAgentMessage> GetNextMessageAsync(CancellationToken token)
        {
            Trace.Entering();
            ArgUtil.NotNull(_session, nameof(_session));
            ArgUtil.NotNull(_settings, nameof(_settings));
            bool encounteringError = false;
            int continuousError = 0;
            string errorMessage = string.Empty;
            Stopwatch heartbeat = new Stopwatch();
            heartbeat.Restart();
            
            if (!_progressiveBackoffFetched)
            {
                try
                {
                    var progressiveBackoffFeatureFlag = await _featureFlagProvider.GetFeatureFlagAsync(HostContext, "DistributedTask.Agent.EnableProgressiveRetryBackoff", Trace);
                    _enableProgressiveBackoff = progressiveBackoffFeatureFlag?.EffectiveState == "On";
                    _progressiveBackoffFetched = true;
                    Trace.Info($"Progressive backoff feature flag fetched: {_enableProgressiveBackoff}");
                }
                catch (Exception ex)
                {   
                    // Will retry on next loop iteration
                    Trace.Info($"Unable to fetch progressive backoff feature flag: {ex.Message}");
                }
            }
            
            while (true)
            {
                token.ThrowIfCancellationRequested();
                TaskAgentMessage message = null;
                try
                {
                    message = await _agentServer.GetAgentMessageAsync(_settings.PoolId,
                                                                _session.SessionId,
                                                                _lastMessageId,
                                                                token);

                    // Decrypt the message body if the session is using encryption
                    message = DecryptMessage(message);

                    if (message != null)
                    {
                        _lastMessageId = message.MessageId;
                    }

                    if (encounteringError) //print the message once only if there was an error
                    {
                        _term.WriteLine(StringUtil.Loc("QueueConnected", DateTime.UtcNow));
                        encounteringError = false;
                        continuousError = 0;
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    Trace.Info("Get next message has been cancelled.");
                    throw;
                }
                catch (TaskAgentAccessTokenExpiredException)
                {
                    Trace.Info("Agent OAuth token has been revoked. Unable to pull message.");
                    throw;
                }
                catch (Exception ex)
                {
                    Trace.Error("Catch exception during get next message.");
                    Trace.Error(ex);

                    // don't retry if SkipSessionRecover = true, DT service will delete agent session to stop agent from taking more jobs.
                    if (ex is TaskAgentSessionExpiredException && !_settings.SkipSessionRecover && await CreateSessionAsync(token))
                    {
                        Trace.Info($"{nameof(TaskAgentSessionExpiredException)} received, recovered by recreate session.");
                    }
                    else if (!IsGetNextMessageExceptionRetriable(ex))
                    {
                        throw;
                    }
                    else
                    {
                        continuousError++;
                        
                        // Use cached/default progressive backoff value
                        bool enableProgressiveBackoff = _enableProgressiveBackoff;
                        string backoffSource = _progressiveBackoffFetched ? "FeatureFlag (cached)" : "Default (not fetched)";
                        
                        Trace.Info($"Progressive backoff enabled: {enableProgressiveBackoff} (source: {backoffSource})");
                        
                        if (enableProgressiveBackoff)
                        {
                            if (continuousError <= 2)
                            {
                                // random backoff [15, 30]
                                _getNextMessageRetryInterval = BackoffTimerHelper.GetRandomBackoff(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), _getNextMessageRetryInterval);
                            }
                            else if (continuousError > 2 && continuousError <= 5)
                            {
                                // random backoff [60, 90]
                                _getNextMessageRetryInterval = BackoffTimerHelper.GetRandomBackoff(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(90), _getNextMessageRetryInterval);
                            }
                            else
                            {
                                // more aggressive backoff [150, 200]
                                _getNextMessageRetryInterval = BackoffTimerHelper.GetRandomBackoff(TimeSpan.FromSeconds(150), TimeSpan.FromSeconds(200), _getNextMessageRetryInterval);
                            }
                        }
                        else
                        {
                            if (continuousError <= 5) 
                            {
                            // Default behavior: random backoff [15, 30]
                            _getNextMessageRetryInterval = BackoffTimerHelper.GetRandomBackoff(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), _getNextMessageRetryInterval);
                            }
                            else
                           {
                            // more aggressive backoff [30, 60]
                            _getNextMessageRetryInterval = BackoffTimerHelper.GetRandomBackoff(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), _getNextMessageRetryInterval);
                           }
                        }

                        if (!encounteringError)
                        {
                            //print error only on the first consecutive error
                            _term.WriteError(StringUtil.Loc("QueueConError", DateTime.UtcNow, ex.Message));
                            encounteringError = true;
                        }

                        // re-create VssConnection before next retry
                        await _agentServer.RefreshConnectionAsync(AgentConnectionType.MessageQueue);
                        
                        Trace.Info($"Unable to get next message in GetNextMessageAsync (attempt {continuousError})");
                        Trace.Info(StringUtil.Format("Sleeping for {0} seconds before retrying.", _getNextMessageRetryInterval.TotalSeconds));
                        await HostContext.Delay(_getNextMessageRetryInterval, token);
                    }
                }

                if (message == null)
                {
                    if (heartbeat.Elapsed > TimeSpan.FromMinutes(30))
                    {
                        Trace.Info($"No message retrieved from session '{_session.SessionId}' within last 30 minutes.");
                        heartbeat.Restart();
                    }
                    else
                    {
                        Trace.Verbose($"No message retrieved from session '{_session.SessionId}'.");
                    }

                    _getNextMessageRetryInterval = BackoffTimerHelper.GetRandomBackoff(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), _getNextMessageRetryInterval);
                    Trace.Info(StringUtil.Format("Sleeping for {0} seconds before retrying.", _getNextMessageRetryInterval.TotalSeconds));
                    await HostContext.Delay(_getNextMessageRetryInterval, token);
                    continue;
                }

                Trace.Info($"Message '{message.MessageId}' received from session '{_session.SessionId}'.");
                return message;
            }
        }

        public async Task DeleteMessageAsync(TaskAgentMessage message)
        {
            Trace.Entering();
            ArgUtil.NotNull(_session, nameof(_session));

            if (message != null && _session.SessionId != Guid.Empty)
            {
                using (var cs = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                {
                    await _agentServer.DeleteAgentMessageAsync(_settings.PoolId, message.MessageId, _session.SessionId, cs.Token);
                }
            }
        }

        public async Task KeepAlive(CancellationToken token)
        {
            int continuousError = 0;
            _keepAliveRetryInterval = TimeSpan.FromSeconds(30);

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await _agentServer.GetAgentMessageAsync(_settings.PoolId, _session.SessionId, null, token);
                    Trace.Info($"Sent GetAgentMessage to keep alive agent {_settings.AgentId}, session '{_session.SessionId}'.");
                    
                    // Reset on success
                    continuousError = 0;
                    _keepAliveRetryInterval = TimeSpan.FromSeconds(30);
                }
                catch (Exception ex)
                {
                    continuousError++;
                    Trace.Info($"Unable to sent GetAgentMessage to keep alive (attempt {continuousError}): {ex.Message}");
                    
                    // Use cached/default progressive backoff value
                    bool enableProgressiveBackoff = _enableProgressiveBackoff;
                    string backoffSource = _progressiveBackoffFetched ? "FeatureFlag (cached)" : "Default (not fetched)";
                    
                    Trace.Info($"KeepAlive - Progressive backoff enabled: {enableProgressiveBackoff} (source: {backoffSource})");
                    
                    if (enableProgressiveBackoff)
                    {
                        if (continuousError <= 2)
                        {
                            // random backoff [30, 35]
                            _keepAliveRetryInterval = BackoffTimerHelper.GetRandomBackoff(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(35), _keepAliveRetryInterval);
                        }
                        else if (continuousError > 2 && continuousError <= 5)
                        {
                            // random backoff [35, 40]
                            _keepAliveRetryInterval = BackoffTimerHelper.GetRandomBackoff(TimeSpan.FromSeconds(35), TimeSpan.FromSeconds(40), _keepAliveRetryInterval);
                        }
                        else
                        {
                            // capped backoff [40, 45]
                            _keepAliveRetryInterval = BackoffTimerHelper.GetRandomBackoff(TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(45), _keepAliveRetryInterval);
                        }
                    }
                    else
                    {
                        // Default behavior: fixed 30 seconds
                        _keepAliveRetryInterval = TimeSpan.FromSeconds(30);
                    }
                    
                    Trace.Info($"KeepAlive will retry in {_keepAliveRetryInterval.TotalSeconds} seconds.");
                }

                await HostContext.Delay(_keepAliveRetryInterval, token);
            }
        }
        private TaskAgentMessage DecryptMessage(TaskAgentMessage message)
        {
            if (_session.EncryptionKey == null ||
                _session.EncryptionKey.Value.Length == 0 ||
                message == null ||
                message.IV == null ||
                message.IV.Length == 0)
            {
                return message;
            }

            using (var aes = Aes.Create())
            using (var decryptor = GetMessageDecryptor(aes, message))
            using (var body = new MemoryStream(Convert.FromBase64String(message.Body)))
            using (var cryptoStream = new CryptoStream(body, decryptor, CryptoStreamMode.Read))
            using (var bodyReader = new StreamReader(cryptoStream, Encoding.UTF8))
            {
                message.Body = bodyReader.ReadToEnd();
            }

            return message;
        }

        private ICryptoTransform GetMessageDecryptor(
            Aes aes,
            TaskAgentMessage message)
        {
            if (_session.EncryptionKey.Encrypted)
            {
                // The agent session encryption key uses the AES symmetric algorithm
                var keyManager = HostContext.GetService<IRSAKeyManager>();
                using (var rsa = keyManager.GetKey())
                {
                    return aes.CreateDecryptor(rsa.Decrypt(_session.EncryptionKey.Value, RSAEncryptionPadding.OaepSHA1), message.IV);
                }
            }
            else
            {
                return aes.CreateDecryptor(_session.EncryptionKey.Value, message.IV);
            }
        }

        private bool IsGetNextMessageExceptionRetriable(Exception ex)
        {
            if (ex is TaskAgentNotFoundException ||
                ex is TaskAgentPoolNotFoundException ||
                ex is TaskAgentSessionExpiredException ||
                ex is AccessDeniedException ||
                ex is VssUnauthorizedException)
            {
                Trace.Info($"Non-retriable exception: {ex.Message}");
                return false;
            }
            else
            {
                Trace.Info($"Retriable exception: {ex.Message}");
                return true;
            }
        }

        private bool IsSessionCreationExceptionRetriable(Exception ex)
        {
            if (ex is TaskAgentNotFoundException)
            {
                Trace.Info("The agent no longer exists on the server. Stopping the agent.");
                _term.WriteError(StringUtil.Loc("MissingAgent"));
                return false;
            }
            else if (ex is TaskAgentSessionConflictException)
            {
                Trace.Info("The session for this agent already exists.");
                _term.WriteError(StringUtil.Loc("SessionExist"));
                // Reset clock skew stopwatch since we're now tracking session conflict
                _clockSkewStopwatch?.Reset();
                
                // Start or restart stopwatch for tracking continuous failures
                if (_sessionConflictStopwatch == null)
                {
                    _sessionConflictStopwatch = Stopwatch.StartNew();
                }
                else if (!_sessionConflictStopwatch.IsRunning)
                {
                    _sessionConflictStopwatch.Restart();
                }
                
                if (_sessionCreationExceptionTracker.ContainsKey(nameof(TaskAgentSessionConflictException)))
                {
                    _sessionCreationExceptionTracker[nameof(TaskAgentSessionConflictException)]++;
                    // Use actual elapsed time instead of count * interval for accurate tracking with dynamic backoff
                    if (_sessionConflictStopwatch.Elapsed >= _sessionConflictRetryLimit)
                    {
                        Trace.Info($"The session conflict exception have reached retry limit. Elapsed: {_sessionConflictStopwatch.Elapsed.TotalSeconds}s, Limit: {_sessionConflictRetryLimit.TotalSeconds}s");
                        _term.WriteError(StringUtil.Loc("SessionExistStopRetry", _sessionConflictRetryLimit.TotalSeconds));
                        return false;
                    }
                }
                else
                {
                    _sessionCreationExceptionTracker[nameof(TaskAgentSessionConflictException)] = 1;
                }

                Trace.Info("The session conflict exception haven't reached retry limit.");
                return true;
            }
            else if (ex is VssOAuthTokenRequestException && ex.Message.Contains("Current server time is"))
            {
                Trace.Info("Local clock might skewed.");
                _term.WriteError(StringUtil.Loc("LocalClockSkewed"));
                // Reset session conflict stopwatch since we're now tracking clock skew
                _sessionConflictStopwatch?.Reset();
                
                // Start or restart stopwatch for tracking continuous failures
                if (_clockSkewStopwatch == null)
                {
                    _clockSkewStopwatch = Stopwatch.StartNew();
                }
                else if (!_clockSkewStopwatch.IsRunning)
                {
                    _clockSkewStopwatch.Restart();
                }
                
                if (_sessionCreationExceptionTracker.ContainsKey(nameof(VssOAuthTokenRequestException)))
                {
                    _sessionCreationExceptionTracker[nameof(VssOAuthTokenRequestException)]++;
                    // Use actual elapsed time instead of count * interval for accurate tracking with dynamic backoff
                    if (_clockSkewStopwatch.Elapsed >= _clockSkewRetryLimit)
                    {
                        Trace.Info($"The OAuth token request exception have reached retry limit. Elapsed: {_clockSkewStopwatch.Elapsed.TotalSeconds}s, Limit: {_clockSkewRetryLimit.TotalSeconds}s");
                        _term.WriteError(StringUtil.Loc("ClockSkewStopRetry", _clockSkewRetryLimit.TotalSeconds));
                        return false;
                    }
                }
                else
                {
                    _sessionCreationExceptionTracker[nameof(VssOAuthTokenRequestException)] = 1;
                }

                Trace.Info("The OAuth token request exception haven't reached retry limit.");
                return true;
            }
            else if (ex is TaskAgentPoolNotFoundException ||
                     ex is AccessDeniedException ||
                     ex is VssUnauthorizedException)
            {
                Trace.Info($"Non-retriable exception: {ex.Message}");
                return false;
            }
            else
            {
                Trace.Info($"Retriable exception: {ex.Message}");
                return true;
            }
        }
    }
}