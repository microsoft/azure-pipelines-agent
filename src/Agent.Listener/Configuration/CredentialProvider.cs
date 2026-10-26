// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Threading.Tasks;

using Agent.Sdk;
using Agent.Sdk.Util;

using Microsoft.Identity.Client;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Client;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Azure.Identity;
using System.Threading;
using Azure.Core;

namespace Microsoft.VisualStudio.Services.Agent.Listener.Configuration
{
    public interface ICredentialProvider
    {
        Boolean RequireInteractive { get; }
        CredentialData CredentialData { get; set; }
        VssCredentials GetVssCredentials(IHostContext context);
        void EnsureCredential(IHostContext context, CommandSettings command, string serverUrl);
    }

    public abstract class CredentialProvider : ICredentialProvider
    {
        public CredentialProvider(string scheme)
        {
            CredentialData = new CredentialData();
            CredentialData.Scheme = scheme;
        }

        public virtual Boolean RequireInteractive => false;
        public CredentialData CredentialData { get; set; }

        public abstract VssCredentials GetVssCredentials(IHostContext context);
        public abstract void EnsureCredential(IHostContext context, CommandSettings command, string serverUrl);
    }

    public sealed class AadDeviceCodeAccessToken : CredentialProvider
    {
        private readonly string _clientId = "97877f11-0fc6-4aee-b1ff-febb0519dd00";

        private readonly string _userImpersonationScope = "499b84ac-1321-427f-aa17-267ca6975798/.default";
        private VssCredentials _credentials;
        public AadDeviceCodeAccessToken() : base(Constants.Configuration.AAD) { }

        public override VssCredentials GetVssCredentials(IHostContext context)
        {
            ArgUtil.NotNull(context, nameof(context));
            Tracing trace = context.GetTrace(nameof(AadDeviceCodeAccessToken));
            trace.Info(nameof(GetVssCredentials));

            if (_credentials != null)
            {
                return _credentials;
            }

            CredentialData.Data.TryGetValue(Constants.Agent.CommandLine.Args.Url, out string serverUrl);
            ArgUtil.NotNullOrEmpty(serverUrl, nameof(serverUrl));

            var tenantAuthorityUrl = AadAuthorityDiscovery.GetTenantAuthorityUrl(context, serverUrl);
            if (tenantAuthorityUrl == null)
            {
                throw new NotSupportedException($"This Azure DevOps organization '{serverUrl}' is not backed by Azure Active Directory.");
            }

            using var httpClientFactory = new MsalAgentHttpClientFactory(context);
            var app = PublicClientApplicationBuilder.Create(_clientId)
                .WithAuthority(tenantAuthorityUrl)
                .WithHttpClientFactory(httpClientFactory)
                .Build();
            var authResult = AcquireATokenFromCacheOrDeviceCodeFlowAsync(context, app, new string[] { _userImpersonationScope }).GetAwaiter().GetResult();

            var aadCred = new VssAadCredential(new VssAadToken(authResult.TokenType, authResult.AccessToken));
            _credentials = new VssCredentials(null, aadCred, CredentialPromptType.DoNotPrompt);
            trace.Info("cred created");
            return _credentials;
        }
        public override void EnsureCredential(IHostContext context, CommandSettings command, string serverUrl)
        {
            ArgUtil.NotNull(context, nameof(context));
            Tracing trace = context.GetTrace(nameof(AadDeviceCodeAccessToken));
            trace.Info(nameof(EnsureCredential));
            ArgUtil.NotNull(command, nameof(command));
            CredentialData.Data[Constants.Agent.CommandLine.Args.Url] = serverUrl;
        }

        private async Task<AuthenticationResult> AcquireATokenFromCacheOrDeviceCodeFlowAsync(IHostContext context, IPublicClientApplication app, IEnumerable<String> scopes)
        {
            AuthenticationResult result = null;
            var accounts = await app.GetAccountsAsync().ConfigureAwait(false);

            if (accounts.Any())
            {

                // Attempt to get a token from the cache (or refresh it silently if needed)
                result = await app.AcquireTokenSilent(scopes, accounts.FirstOrDefault())
                    .ExecuteAsync().ConfigureAwait(false);

            }

            // Cache empty or no token for account in the cache, attempt by device code flow
            if (result == null)
            {
                result = await GetTokenUsingDeviceCodeFlowAsync(context, app, scopes).ConfigureAwait(false);
            }

            return result;
        }

        /// <summary>
        /// Gets an access token so that the application accesses the web api in the name of the user
        /// who signs-in on a separate device
        /// </summary>
        /// <returns>An authentication result, or null if the user canceled sign-in, or did not sign-in on a separate device
        /// after a timeout (15 mins)</returns>
        private async Task<AuthenticationResult> GetTokenUsingDeviceCodeFlowAsync(IHostContext context, IPublicClientApplication app, IEnumerable<string> scopes)
        {
            Tracing trace = context.GetTrace(nameof(AadDeviceCodeAccessToken));
            AuthenticationResult result;
            try
            {
                result = await app.AcquireTokenWithDeviceCode(scopes,
                    deviceCodeCallback =>
                    {
                        // This will print the message on the console which tells the user where to go sign-in using 
                        // a separate browser and the code to enter once they sign in.
                        var term = context.GetService<ITerminal>();
                        term.WriteLine($"Please finish AAD device code flow in browser ({deviceCodeCallback.VerificationUrl}), user code: {deviceCodeCallback.UserCode}"); return Task.FromResult(0);
                    }).ExecuteAsync().ConfigureAwait(false);
            }

            catch (MsalServiceException)
            {
                // AADSTS50059: No tenant-identifying information found in either the request or implied by any provided credentials.
                // AADSTS90133: Device Code flow is not supported under /common or /consumers endpoint.
                // AADSTS90002: Tenant <tenantId or domain you used in the authority> not found. This may happen if there are 
                // no active subscriptions for the tenant. Check with your subscription administrator.
                throw;
            }
            catch (OperationCanceledException ex)
            {
                trace.Warning(ex.Message);
                throw;
            }
            catch (MsalClientException ex)
            {
                trace.Warning(ex.Message);
                throw;
            }
            return result;
        }

    }

    public sealed class AadInteractiveAccessToken : CredentialProvider
    {
        private const string ClientId = "97877f11-0fc6-4aee-b1ff-febb0519dd00";
        private const string UserImpersonationScope = "499b84ac-1321-427f-aa17-267ca6975798/.default";
        private static readonly TimeSpan AuthenticationTimeout = TimeSpan.FromMinutes(15);
        private VssCredentials _credentials;

        public AadInteractiveAccessToken() : base(Constants.Configuration.AADInteractive) { }

        public override Boolean RequireInteractive => true;

        public override void EnsureCredential(IHostContext context, CommandSettings command, string serverUrl)
        {
            ArgUtil.NotNull(context, nameof(context));
            ArgUtil.NotNull(command, nameof(command));
            ArgUtil.NotNullOrEmpty(serverUrl, nameof(serverUrl));

            EnsureWindows();

            CredentialData.Data[Constants.Agent.CommandLine.Args.Url] = serverUrl;
        }

        private static void EnsureWindows()
        {
            if (!PlatformUtil.RunningOnWindows)
            {
                throw new NotSupportedException("Authentication type 'AADI' is supported only on Windows.");
            }
        }

        public override VssCredentials GetVssCredentials(IHostContext context)
        {
            ArgUtil.NotNull(context, nameof(context));
            Tracing trace = context.GetTrace(nameof(AadInteractiveAccessToken));

            if (_credentials != null)
            {
                return _credentials;
            }

            EnsureWindows();

            CredentialData.Data.TryGetValue(Constants.Agent.CommandLine.Args.Url, out string serverUrl);
            ArgUtil.NotNullOrEmpty(serverUrl, nameof(serverUrl));

            Uri tenantAuthorityUrl = AadAuthorityDiscovery.GetTenantAuthorityUrl(context, serverUrl);
            if (tenantAuthorityUrl == null)
            {
                throw new NotSupportedException($"This Azure DevOps organization '{serverUrl}' is not backed by Azure Active Directory.");
            }

            using var httpClientFactory = new MsalAgentHttpClientFactory(context);
            IPublicClientApplication app = PublicClientApplicationBuilder.Create(ClientId)
                .WithAuthority(tenantAuthorityUrl)
                .WithRedirectUri("http://localhost")
                .WithHttpClientFactory(httpClientFactory)
                .Build();

            context.GetService<ITerminal>().WriteLine(
                "Opening the system browser to sign in on this machine. Complete all Microsoft Entra and Conditional Access prompts.");

            using var timeoutSource = new CancellationTokenSource(AuthenticationTimeout);
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
                timeoutSource.Token,
                context.AgentShutdownToken);

            AuthenticationResult authResult;
            try
            {
                authResult = app.AcquireTokenInteractive(new[] { UserImpersonationScope })
                    .WithUseEmbeddedWebView(false)
                    .WithPrompt(Prompt.SelectAccount)
                    .ExecuteAsync(linkedSource.Token)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (OperationCanceledException ex) when (timeoutSource.IsCancellationRequested && !context.AgentShutdownToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"Interactive Microsoft Entra authentication did not complete within {AuthenticationTimeout.TotalMinutes:0} minutes.",
                    ex);
            }

            trace.Info("Interactive Microsoft Entra credential created.");
            var aadCredential = new VssAadCredential(new VssAadToken(authResult.TokenType, authResult.AccessToken));
            _credentials = new VssCredentials(null, aadCredential, CredentialPromptType.DoNotPrompt);
            return _credentials;
        }
    }

    public sealed class AzureCliAccessToken : CredentialProvider
    {
        private const string UserImpersonationScope = "499b84ac-1321-427f-aa17-267ca6975798/.default";
        private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(2);
        private VssCredentials _credentials;

        public AzureCliAccessToken() : base(Constants.Configuration.AzureCLI) { }

        public override void EnsureCredential(IHostContext context, CommandSettings command, string serverUrl)
        {
            ArgUtil.NotNull(context, nameof(context));
            ArgUtil.NotNull(command, nameof(command));
            ArgUtil.NotNullOrEmpty(serverUrl, nameof(serverUrl));

            EnsureWindows();
            CredentialData.Data[Constants.Agent.CommandLine.Args.Url] = serverUrl;
        }

        public override VssCredentials GetVssCredentials(IHostContext context)
        {
            ArgUtil.NotNull(context, nameof(context));

            if (_credentials != null)
            {
                return _credentials;
            }

            EnsureWindows();

            CredentialData.Data.TryGetValue(Constants.Agent.CommandLine.Args.Url, out string serverUrl);
            ArgUtil.NotNullOrEmpty(serverUrl, nameof(serverUrl));

            Uri tenantAuthorityUrl = AadAuthorityDiscovery.GetTenantAuthorityUrl(context, serverUrl);
            if (tenantAuthorityUrl == null)
            {
                throw new NotSupportedException($"This Azure DevOps organization '{serverUrl}' is not backed by Azure Active Directory.");
            }

            string tenantId = AadAuthorityDiscovery.GetTenantId(tenantAuthorityUrl);
            context.GetService<ITerminal>().WriteLine(
                $"Using the existing Azure CLI sign-in for tenant '{tenantId}' to register the Agent.");

            var credential = new AzureCliCredential(new AzureCliCredentialOptions
            {
                TenantId = tenantId,
                ProcessTimeout = ProcessTimeout,
            });

            AccessToken accessToken;
            try
            {
                accessToken = credential.GetToken(
                    new TokenRequestContext(new[] { UserImpersonationScope }),
                    context.AgentShutdownToken);
            }
            catch (CredentialUnavailableException ex)
            {
                throw CreateAzureCliAuthenticationException(tenantId, ex);
            }
            catch (AuthenticationFailedException ex)
            {
                throw CreateAzureCliAuthenticationException(tenantId, ex);
            }

            var aadCredential = new VssAadCredential(new VssAadToken("Bearer", accessToken.Token));
            _credentials = new VssCredentials(null, aadCredential, CredentialPromptType.DoNotPrompt);
            return _credentials;
        }

        private static void EnsureWindows()
        {
            if (!PlatformUtil.RunningOnWindows)
            {
                throw new NotSupportedException("Authentication type 'AZCLI' is supported only on Windows.");
            }
        }

        private static InvalidOperationException CreateAzureCliAuthenticationException(string tenantId, Exception innerException)
        {
            return new InvalidOperationException(
                $"Azure CLI could not provide an Azure DevOps token for tenant '{tenantId}'. " +
                "Install Azure CLI, enable its Windows broker, and sign in before configuring the Agent: " +
                "'az config set core.enable_broker_on_windows=true', then " +
                $"'az login --tenant {tenantId}'.",
                innerException);
        }
    }

    internal static class AadAuthorityDiscovery
    {
        private const string AuthorizationUriParameter = "authorization_uri";

        internal static Uri GetTenantAuthorityUrl(IHostContext context, string serverUrl)
        {
            Tracing trace = context.GetTrace(nameof(AadAuthorityDiscovery));

            using var handler = context.CreateHttpClientHandler();
            using var client = new HttpClient(handler);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Add("X-TFS-FedAuthRedirect", "Suppress");
            client.DefaultRequestHeaders.UserAgent.Clear();
            client.DefaultRequestHeaders.UserAgent.AddRange(VssClientHttpRequestSettings.Default.UserAgent);

            using var requestMessage = new HttpRequestMessage(HttpMethod.Head, $"{serverUrl.Trim('/')}/_apis/connectiondata");
            try
            {
                using HttpResponseMessage response = client.SendAsync(requestMessage).GetAwaiter().GetResult();
                return TryGetTenantAuthorityUrl(response.Headers.WwwAuthenticate, out Uri authority) ? authority : null;
            }
            catch (SocketException e)
            {
                ExceptionsUtil.HandleSocketException(e, serverUrl, message => trace.Error(message));
                throw;
            }
        }

        internal static bool TryGetTenantAuthorityUrl(
            IEnumerable<AuthenticationHeaderValue> authenticateHeaders,
            out Uri authority)
        {
            authority = null;

            foreach (AuthenticationHeaderValue header in authenticateHeaders)
            {
                if (!string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(header.Parameter))
                {
                    continue;
                }

                string parameter = GetParameterValue(header.Parameter, AuthorizationUriParameter);
                if (Uri.TryCreate(parameter, UriKind.Absolute, out Uri candidate) &&
                    string.Equals(candidate.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                {
                    authority = candidate;
                    return true;
                }
            }

            return false;
        }

        internal static string GetTenantId(Uri authority)
        {
            ArgUtil.NotNull(authority, nameof(authority));

            string[] segments = authority.AbsolutePath.Split(
                new[] { '/' },
                StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Microsoft Entra authority '{authority}' does not contain a tenant identifier.");
            }

            return segments[0];
        }

        private static string GetParameterValue(string parameters, string name)
        {
            int nameIndex = parameters.IndexOf(name, StringComparison.OrdinalIgnoreCase);
            while (nameIndex >= 0)
            {
                int equalsIndex = nameIndex + name.Length;
                if ((nameIndex == 0 || parameters[nameIndex - 1] == ',' || char.IsWhiteSpace(parameters[nameIndex - 1])) &&
                    equalsIndex < parameters.Length &&
                    parameters[equalsIndex] == '=')
                {
                    int valueStart = equalsIndex + 1;
                    while (valueStart < parameters.Length && char.IsWhiteSpace(parameters[valueStart]))
                    {
                        valueStart++;
                    }

                    if (valueStart < parameters.Length && parameters[valueStart] == '"')
                    {
                        int quoteEnd = parameters.IndexOf('"', valueStart + 1);
                        return quoteEnd > valueStart
                            ? parameters.Substring(valueStart + 1, quoteEnd - valueStart - 1)
                            : null;
                    }

                    int valueEnd = parameters.IndexOf(',', valueStart);
                    if (valueEnd < 0)
                    {
                        valueEnd = parameters.Length;
                    }

                    return parameters.Substring(valueStart, valueEnd - valueStart).Trim();
                }

                nameIndex = parameters.IndexOf(name, nameIndex + name.Length, StringComparison.OrdinalIgnoreCase);
            }

            return null;
        }
    }

    public sealed class PersonalAccessToken : CredentialProvider
    {
        public PersonalAccessToken() : base(Constants.Configuration.PAT) { }

        public override VssCredentials GetVssCredentials(IHostContext context)
        {
            ArgUtil.NotNull(context, nameof(context));
            Tracing trace = context.GetTrace(nameof(PersonalAccessToken));
            trace.Info(nameof(GetVssCredentials));
            ArgUtil.NotNull(CredentialData, nameof(CredentialData));
            string token;
            if (!CredentialData.Data.TryGetValue(Constants.Agent.CommandLine.Args.Token, out token))
            {
                token = null;
            }

            ArgUtil.NotNullOrEmpty(token, nameof(token));

            trace.Info(StringUtil.Format("token retrieved: {0} chars", token.Length));

            // PAT uses a basic credential
            VssBasicCredential basicCred = new VssBasicCredential("VstsAgent", token);
            VssCredentials creds = new VssCredentials(null, basicCred, CredentialPromptType.DoNotPrompt);
            trace.Info("cred created");

            return creds;
        }

        public override void EnsureCredential(IHostContext context, CommandSettings command, string serverUrl)
        {
            ArgUtil.NotNull(context, nameof(context));
            Tracing trace = context.GetTrace(nameof(PersonalAccessToken));
            trace.Info(nameof(EnsureCredential));
            ArgUtil.NotNull(command, nameof(command));
            CredentialData.Data[Constants.Agent.CommandLine.Args.Token] = command.GetToken();
        }
    }

    public sealed class ServiceIdentityCredential : CredentialProvider
    {
        public ServiceIdentityCredential() : base(Constants.Configuration.ServiceIdentity) { }

        public override VssCredentials GetVssCredentials(IHostContext context)
        {
            ArgUtil.NotNull(context, nameof(context));
            Tracing trace = context.GetTrace(nameof(ServiceIdentityCredential));
            trace.Info(nameof(GetVssCredentials));
            ArgUtil.NotNull(CredentialData, nameof(CredentialData));
            string token;
            if (!CredentialData.Data.TryGetValue(Constants.Agent.CommandLine.Args.Token, out token))
            {
                token = null;
            }

            string username;
            if (!CredentialData.Data.TryGetValue(Constants.Agent.CommandLine.Args.UserName, out username))
            {
                username = null;
            }

            ArgUtil.NotNullOrEmpty(token, nameof(token));
            ArgUtil.NotNullOrEmpty(username, nameof(username));

            trace.Info(StringUtil.Format("token retrieved: {0} chars", token.Length));

            // ServiceIdentity uses a service identity credential
            VssServiceIdentityToken identityToken = new VssServiceIdentityToken(token);
            VssServiceIdentityCredential serviceIdentityCred = new VssServiceIdentityCredential(username, "", identityToken);
            VssCredentials creds = new VssCredentials(null, serviceIdentityCred, CredentialPromptType.DoNotPrompt);
            trace.Info("cred created");

            return creds;
        }

        public override void EnsureCredential(IHostContext context, CommandSettings command, string serverUrl)
        {
            ArgUtil.NotNull(context, nameof(context));
            Tracing trace = context.GetTrace(nameof(ServiceIdentityCredential));
            trace.Info(nameof(EnsureCredential));
            ArgUtil.NotNull(command, nameof(command));
            CredentialData.Data[Constants.Agent.CommandLine.Args.Token] = command.GetToken();
            CredentialData.Data[Constants.Agent.CommandLine.Args.UserName] = command.GetUserName();
        }
    }

    public sealed class AlternateCredential : CredentialProvider
    {
        public AlternateCredential() : base(Constants.Configuration.Alternate) { }

        public override VssCredentials GetVssCredentials(IHostContext context)
        {
            ArgUtil.NotNull(context, nameof(context));
            Tracing trace = context.GetTrace(nameof(AlternateCredential));
            trace.Info(nameof(GetVssCredentials));

            string username;
            if (!CredentialData.Data.TryGetValue(Constants.Agent.CommandLine.Args.UserName, out username))
            {
                username = null;
            }

            string password;
            if (!CredentialData.Data.TryGetValue(Constants.Agent.CommandLine.Args.Password, out password))
            {
                password = null;
            }

            ArgUtil.NotNull(username, nameof(username));
            ArgUtil.NotNull(password, nameof(password));

            trace.Info(StringUtil.Format("username retrieved: {0} chars", username.Length));
            trace.Info(StringUtil.Format("password retrieved: {0} chars", password.Length));

            VssBasicCredential loginCred = new VssBasicCredential(username, password);
            VssCredentials creds = new VssCredentials(null, loginCred, CredentialPromptType.DoNotPrompt);
            trace.Info("cred created");

            return creds;
        }

        public override void EnsureCredential(IHostContext context, CommandSettings command, string serverUrl)
        {
            ArgUtil.NotNull(context, nameof(context));
            Tracing trace = context.GetTrace(nameof(AlternateCredential));
            trace.Info(nameof(EnsureCredential));
            ArgUtil.NotNull(command, nameof(command));
            CredentialData.Data[Constants.Agent.CommandLine.Args.UserName] = command.GetUserName();
            CredentialData.Data[Constants.Agent.CommandLine.Args.Password] = command.GetPassword();
        }
    }

    public sealed class ServicePrincipalCredential : CredentialProvider
    {
        public ServicePrincipalCredential() : base(Constants.Configuration.ServicePrincipal) { }

        public override VssCredentials GetVssCredentials(IHostContext context)
        {
            ArgUtil.NotNull(context, nameof(context));
            Tracing trace = context.GetTrace(nameof(ServicePrincipalCredential));
            trace.Info(nameof(GetVssCredentials));

            CredentialData.Data.TryGetValue(Constants.Agent.CommandLine.Args.TenantId, out string tenantId);
            ArgUtil.NotNullOrEmpty(tenantId, nameof(tenantId));
            trace.Info(StringUtil.Format("tenant id retrieved: {0} chars", tenantId.Length));

            CredentialData.Data.TryGetValue(Constants.Agent.CommandLine.Args.ClientId, out string clientId);
            ArgUtil.NotNullOrEmpty(clientId, nameof(clientId));
            trace.Info(StringUtil.Format("client id retrieved: {0} chars", clientId.Length));

            CredentialData.Data.TryGetValue(Constants.Agent.CommandLine.Args.ClientSecret, out string clientSecret);
            ArgUtil.NotNullOrEmpty(clientSecret, nameof(clientSecret));
            trace.Info(StringUtil.Format("client secret retrieved: {0} chars", clientSecret.Length));

            var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);

            var tokenRequestContext = new TokenRequestContext(VssAadSettings.DefaultScopes);
            var accessToken = credential.GetTokenAsync(tokenRequestContext, CancellationToken.None).GetAwaiter().GetResult();

            var vssAadToken = new VssAadToken("Bearer", accessToken.Token);
            var vssAadCredentials = new VssAadCredential(vssAadToken);

            var creds = new VssCredentials(vssAadCredentials);
            trace.Info("cred created");

            return creds;
        }
        public override void EnsureCredential(IHostContext context, CommandSettings command, string serverUrl)
        {
            ArgUtil.NotNull(context, nameof(context));
            Tracing trace = context.GetTrace(nameof(ServicePrincipalCredential));
            trace.Info(nameof(EnsureCredential));
            ArgUtil.NotNull(command, nameof(command));
            CredentialData.Data[Constants.Agent.CommandLine.Args.ClientId] = command.GetClientId();
            CredentialData.Data[Constants.Agent.CommandLine.Args.TenantId] = command.GetTenantId();
            CredentialData.Data[Constants.Agent.CommandLine.Args.ClientSecret] = command.GetClientSecret();
        }
    }
}
