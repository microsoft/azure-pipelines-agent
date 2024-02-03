// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using Agent.Sdk.Knob;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
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

        public static void PushTaskIntoAgentInfo(string taskName)
        {
            var headerValues = VssClientHttpRequestSettings.Default.UserAgent;

            if (headerValues  == null)
            {
                headerValues = new List<ProductInfoHeaderValue>();
            }

            headerValues.Add(new ProductInfoHeaderValue(string.Concat(TaskUserAgentPrefix, taskName , ")")));

            VssClientHttpRequestSettings.Default.UserAgent = headerValues;
        }

        public static void RemoveTaskFromAgentInfo()
        {
            var headerValues = VssClientHttpRequestSettings.Default.UserAgent;
            if (headerValues == null)
            {
                return;
            }

            foreach(var value in headerValues)
            {
                if (value.Comment.StartsWith(TaskUserAgentPrefix))
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

        private static bool CheckSupportOfCustomServerCertificateValidation(ITraceWriter trace)
        {
            using (var handler = new HttpClientHandler())
            {
                handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => { return true; };

                using (var client = new HttpClient(handler))
                {
                    try
                    {
                        client.GetAsync(_testUri).GetAwaiter().GetResult();
                    }
                    catch (Exception e)
                    {
                        trace.Verbose($"SSL diagnostic data collection is disabled, due to issue:\n{e.Message}");
                        return false;
                    }
                    return true;
                }
            }
        }
    }
}
