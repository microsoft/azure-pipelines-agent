// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using Agent.Sdk.Knob;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Security;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.OAuth;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Net;

namespace Microsoft.VisualStudio.Services.Agent.Util
{
    public static class VssUtil
    {
        private static UtilKnobValueContext _knobContext = UtilKnobValueContext.Instance();

        private const string _testUri = "https://microsoft.com/";
        private const string TaskUserAgentPrefix = "(Task:";
        private static bool? _isCustomServerCertificateValidationSupported;

        public static void InitializeVssClientSettings(ProductInfoHeaderValue additionalUserAgent, IWebProxy proxy, IVssClientCertificateManager clientCert, bool SkipServerCertificateValidation)
        {
            var headerValues = new List<ProductInfoHeaderValue>();
            headerValues.Add(additionalUserAgent);
            headerValues.Add(new ProductInfoHeaderValue($"({RuntimeInformation.OSDescription.Trim()})"));

            if (VssClientHttpRequestSettings.Default.UserAgent != null && VssClientHttpRequestSettings.Default.UserAgent.Count > 0)
            {
                headerValues.AddRange(VssClientHttpRequestSettings.Default.UserAgent);
            }

            VssClientHttpRequestSettings.Default.UserAgent = headerValues;
            VssClientHttpRequestSettings.Default.ClientCertificateManager = clientCert;

            if (PlatformUtil.RunningOnLinux || PlatformUtil.RunningOnMacOS)
            {
                // The .NET Core 2.1 runtime switched its HTTP default from HTTP 1.1 to HTTP 2.
                // This causes problems with some versions of the Curl handler.
                // See GitHub issue https://github.com/dotnet/corefx/issues/32376
                VssClientHttpRequestSettings.Default.UseHttp11 = true;
            }

            VssHttpMessageHandler.DefaultWebProxy = proxy;

            if (SkipServerCertificateValidation)
            {
                VssClientHttpRequestSettings.Default.ServerCertificateValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            }
        }

        public static void PushTaskIntoAgentInfo(string taskName, string taskVersion)
        {
            var headerValues = VssClientHttpRequestSettings.Default.UserAgent;

            if (headerValues == null)
            {
                headerValues = new List<ProductInfoHeaderValue>();
            }

            headerValues.Add(new ProductInfoHeaderValue(string.Concat(TaskUserAgentPrefix, taskName , "-" , taskVersion, ")")));

            VssClientHttpRequestSettings.Default.UserAgent = headerValues;
        }

        public static void RemoveTaskFromAgentInfo()
        {
            var headerValues = VssClientHttpRequestSettings.Default.UserAgent;
            if (headerValues == null)
            {
                return;
            }

            foreach (var value in headerValues)
            {
                if (value.Comment != null && value.Comment.StartsWith(TaskUserAgentPrefix))
                {
                    headerValues.Remove(value);
                    break;
                }
            }

            VssClientHttpRequestSettings.Default.UserAgent = headerValues;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA2000:Dispose objects before losing scope", MessageId = "connection")]
        public static VssConnection CreateConnection(
            Uri serverUri,
            VssCredentials credentials,
            ITraceWriter trace,
            bool skipServerCertificateValidation = false,
            IEnumerable<DelegatingHandler> additionalDelegatingHandler = null,
            TimeSpan? timeout = null)
        {
            VssClientHttpRequestSettings settings = VssClientHttpRequestSettings.Default.Clone();

            // make sure MaxRetryRequest in range [3, 10]
            int maxRetryRequest = AgentKnobs.HttpRetryCount.GetValue(_knobContext).AsInt();
            settings.MaxRetryRequest = Math.Min(Math.Max(maxRetryRequest, 3), 10);

            // prefer parameter, otherwise use httpRequestTimeoutSeconds and make sure httpRequestTimeoutSeconds in range [100, 1200]
            int httpRequestTimeoutSeconds = AgentKnobs.HttpTimeout.GetValue(_knobContext).AsInt();
            settings.SendTimeout = timeout ?? TimeSpan.FromSeconds(Math.Min(Math.Max(httpRequestTimeoutSeconds, 100), 1200));

            // Enhanced logging for connection diagnostics
            if (trace != null)
            {
                trace.Info($"Creating VssConnection to {serverUri}");
                trace.Info($"Connection settings - MaxRetryRequest: {settings.MaxRetryRequest}, SendTimeout: {settings.SendTimeout.TotalSeconds}s");
                trace.Info($"Using legacy HTTP handler: {PlatformUtil.UseLegacyHttpHandler}");
                
                // Log credential type for diagnostics
                string credType = "Unknown";
                if (credentials?.Federated is Microsoft.VisualStudio.Services.OAuth.VssOAuthAccessTokenCredential oauthCred)
                {
                    credType = "OAuth AccessToken";
                    // Try to extract token information for expiry diagnostics without exposing the token
                    try
                    {
                        var tokenField = oauthCred.GetType().GetField("m_accessToken", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (tokenField != null)
                        {
                            var tokenValue = tokenField.GetValue(oauthCred) as string;
                            if (!string.IsNullOrEmpty(tokenValue))
                            {
                                // Parse JWT token to get expiry without exposing the token content
                                try
                                {
                                    var tokenParts = tokenValue.Split('.');
                                    if (tokenParts.Length >= 2)
                                    {
                                        var payload = tokenParts[1];
                                        // Add padding if needed for base64 decoding
                                        while (payload.Length % 4 != 0) payload += "=";
                                        var decodedBytes = Convert.FromBase64String(payload);
                                        var decodedString = System.Text.Encoding.UTF8.GetString(decodedBytes);
                                        if (decodedString.Contains("\"exp\":"))
                                        {
                                            var expMatch = System.Text.RegularExpressions.Regex.Match(decodedString, @"""exp"":(\d+)");
                                            if (expMatch.Success && long.TryParse(expMatch.Groups[1].Value, out var expUnix))
                                            {
                                                var expiry = DateTimeOffset.FromUnixTimeSeconds(expUnix);
                                                var timeUntilExpiry = expiry - DateTimeOffset.UtcNow;
                                                trace.Info($"OAuth token expiry: {expiry:yyyy-MM-dd HH:mm:ss} UTC (in {timeUntilExpiry.TotalMinutes:F1} minutes)");
                                                
                                                if (timeUntilExpiry.TotalMinutes < 5)
                                                {
                                                    trace.Info($"*** WARNING: OAuth token expires soon: {timeUntilExpiry.TotalMinutes:F1} minutes remaining ***");
                                                }
                                                else if (timeUntilExpiry.TotalSeconds < 0)
                                                {
                                                    trace.Info($"*** ERROR: OAuth token is EXPIRED by {Math.Abs(timeUntilExpiry.TotalMinutes):F1} minutes ***");
                                                }
                                            }
                                        }
                                    }
                                }
                                catch (Exception tokenParseEx)
                                {
                                    trace.Verbose($"Could not parse token expiry information: {tokenParseEx.Message}");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        trace.Verbose($"Could not access token metadata for expiry check: {ex.Message}");
                    }
                }
                else if (credentials?.Federated != null && credentials.Federated.GetType().Name.Contains("VssAad"))
                    credType = "AAD Credential";
                else if (credentials?.Storage != null)
                    credType = "Basic/PAT Credential";
                else if (credentials?.Windows != null)
                    credType = "Windows Credential";
                    
                trace.Info($"Credential type: {credType}");
                
                // Log additional delegating handlers
                if (additionalDelegatingHandler != null)
                {
                    int handlerCount = 0;
                    foreach (var handler in additionalDelegatingHandler)
                    {
                        handlerCount++;
                        trace.Info($"Additional HTTP handler {handlerCount}: {handler.GetType().Name}");
                    }
                    if (handlerCount == 0)
                    {
                        trace.Info("No additional HTTP handlers");
                    }
                }
                
                // Log socket and connection pool information
                try
                {
                    // Get system-level connection information for socket exhaustion diagnostics
                    if (PlatformUtil.RunningOnWindows)
                    {
                        var tcpConnections = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections();
                        int establishedConnections = 0;
                        int totalConnections = tcpConnections.Length;
                        
                        foreach (var conn in tcpConnections)
                        {
                            if (conn.State == System.Net.NetworkInformation.TcpState.Established)
                                establishedConnections++;
                        }
                        
                        trace.Info($"System TCP connections: {establishedConnections} established, {totalConnections} total");
                        
                        if (establishedConnections > 1000)
                        {
                            trace.Info($"*** WARNING: High number of established TCP connections ({establishedConnections}) - possible socket exhaustion ***");
                        }
                        
                        // Log connections to the target server specifically
                        var serverHost = serverUri.Host;
                        int serverConnections = 0;
                        foreach (var conn in tcpConnections)
                        {
                            if (conn.RemoteEndPoint.Address.ToString().Equals(serverHost, StringComparison.OrdinalIgnoreCase) ||
                                (System.Net.IPAddress.TryParse(serverHost, out var serverIp) && conn.RemoteEndPoint.Address.Equals(serverIp)))
                            {
                                serverConnections++;
                            }
                        }
                        
                        if (serverConnections > 0)
                        {
                            trace.Info($"Existing connections to {serverHost}: {serverConnections}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    trace.Verbose($"Could not retrieve system connection information: {ex.Message}");
                }
            }

            // Remove Invariant from the list of accepted languages.
            //
            // The constructor of VssHttpRequestSettings (base class of VssClientHttpRequestSettings) adds the current
            // UI culture to the list of accepted languages. The UI culture will be Invariant on OSX/Linux when the
            // LANG environment variable is not set when the program starts. If Invariant is in the list of accepted
            // languages, then "System.ArgumentException: The value cannot be null or empty." will be thrown when the
            // settings are applied to an HttpRequestMessage.
            settings.AcceptLanguages.Remove(CultureInfo.InvariantCulture);

            // Setting `ServerCertificateCustomValidation` to able to capture SSL data for diagnostic
            if (trace != null && IsCustomServerCertificateValidationSupported(trace))
            {
                SslUtil sslUtil = new SslUtil(trace, skipServerCertificateValidation);
                settings.ServerCertificateValidationCallback = sslUtil.RequestStatusCustomValidation;
            }

            VssConnection connection = new VssConnection(serverUri, new VssHttpMessageHandler(credentials, settings), additionalDelegatingHandler);
            
            // Enhanced logging for connection creation and socket diagnostics
            if (trace != null)
            {
                trace.Info($"VssConnection created for {serverUri.Host}:{serverUri.Port}");
                
                // Log handler type being used for socket pool diagnostics
                if (PlatformUtil.UseLegacyHttpHandler)
                {
                    trace.Info("Using LEGACY HTTP handler - isolated connection pools");
                }
                else
                {
                    trace.Info("Using MODERN SocketsHttpHandler - shared global connection pool");
                    trace.Info("*** SHARED POOL WARNING *** Heavy task connections may exhaust sockets for agent-server communication");
                }
                
                // Log timing for connection creation
                trace.Info($"Connection created at: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC");
            }
            
            return connection;
        }

        public static VssCredentials GetVssCredential(ServiceEndpoint serviceEndpoint)
        {
            ArgUtil.NotNull(serviceEndpoint, nameof(serviceEndpoint));
            ArgUtil.NotNull(serviceEndpoint.Authorization, nameof(serviceEndpoint.Authorization));
            ArgUtil.NotNullOrEmpty(serviceEndpoint.Authorization.Scheme, nameof(serviceEndpoint.Authorization.Scheme));

            if (serviceEndpoint.Authorization.Parameters.Count == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(serviceEndpoint));
            }

            VssCredentials credentials = null;
            string accessToken;
            if (serviceEndpoint.Authorization.Scheme == EndpointAuthorizationSchemes.OAuth &&
                serviceEndpoint.Authorization.Parameters.TryGetValue(EndpointAuthorizationParameters.AccessToken, out accessToken))
            {
                credentials = new VssCredentials(null, new VssOAuthAccessTokenCredential(accessToken), CredentialPromptType.DoNotPrompt);
            }

            return credentials;
        }

        public static bool IsCustomServerCertificateValidationSupported(ITraceWriter trace)
        {
            if (!PlatformUtil.RunningOnWindows && PlatformUtil.UseLegacyHttpHandler)
            {
                if (_isCustomServerCertificateValidationSupported == null)
                {
                    _isCustomServerCertificateValidationSupported = CheckSupportOfCustomServerCertificateValidation(trace);
                }
                return (bool)_isCustomServerCertificateValidationSupported;
            }
            return true;
        }

        // The function is to check if the custom server certificate validation is supported on the current platform.
        private static bool CheckSupportOfCustomServerCertificateValidation(ITraceWriter trace)
        {
            using (var handler = new HttpClientHandler())
            {
                handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => { return errors == SslPolicyErrors.None; };

                using (var client = new HttpClient(handler))
                {
                    try
                    {
                        client.GetAsync(_testUri).GetAwaiter().GetResult();
                        trace.Verbose("Custom Server Validation Callback Successful, SSL diagnostic data collection is enabled.");
                    }
                    catch (Exception e)
                    {
                        trace.Verbose($"Custom Server Validation Callback Unsuccessful, SSL diagnostic data collection is disabled, due to issue:\n{e.Message}");
                        return false;
                    }
                    return true;
                }
            }
        }
    }
}
