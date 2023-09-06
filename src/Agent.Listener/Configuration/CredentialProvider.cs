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
        private IPublicClientApplication _app = null;

        private readonly string _clientId = "97877f11-0fc6-4aee-b1ff-febb0519dd00";

        private readonly string _userImpersonationScope = "499b84ac-1321-427f-aa17-267ca6975798/.default";
        public AadDeviceCodeAccessToken() : base(Constants.Configuration.AAD) { }

        public override VssCredentials GetVssCredentials(IHostContext context)
        {
            ArgUtil.NotNull(context, nameof(context));
            Tracing trace = context.GetTrace(nameof(AadDeviceCodeAccessToken));
            trace.Info(nameof(GetVssCredentials));

            CredentialData.Data.TryGetValue(Constants.Agent.CommandLine.Args.Url, out string serverUrl);
            ArgUtil.NotNullOrEmpty(serverUrl, nameof(serverUrl));

            var tenantAuthorityUrl = GetTenantAuthorityUrl(context, serverUrl);
            if (tenantAuthorityUrl == null)
            {
                throw new NotSupportedException($"This Azure DevOps organization '{serverUrl}' is not backed by Azure Active Directory.");
            }

            if (_app == null)
                _app = PublicClientApplicationBuilder.Create(_clientId).Build();

            var authResult = AcquireATokenFromCacheOrDeviceCodeFlowAsync(context, _app, new string[] { _userImpersonationScope }).GetAwaiter().GetResult();

            var aadCred = new VssAadCredential(new VssAadToken(authResult.TokenType, authResult.AccessToken));
            VssCredentials creds = new VssCredentials(null, aadCred, CredentialPromptType.DoNotPrompt);
            trace.Info("cred created");
            return creds;
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

        private Uri GetTenantAuthorityUrl(IHostContext context, string serverUrl)
        {
            Tracing trace = context.GetTrace(nameof(AadDeviceCodeAccessToken));

            using (var handler = context.CreateHttpClientHandler())
            using (var client = new HttpClient(handler))
            {
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.Add("X-TFS-FedAuthRedirect", "Suppress");
                client.DefaultRequestHeaders.UserAgent.Clear();
                client.DefaultRequestHeaders.UserAgent.AddRange(VssClientHttpRequestSettings.Default.UserAgent);
                using (var requestMessage = new HttpRequestMessage(HttpMethod.Head, $"{serverUrl.Trim('/')}/_apis/connectiondata"))
                {
                    HttpResponseMessage response;
                    try
                    {
                        response = client.SendAsync(requestMessage).GetAwaiter().GetResult();
                    }
                    catch (SocketException e)
                    {
                        ExceptionsUtil.HandleSocketException(e, serverUrl, trace.Error);
                        throw;
                    }

                    // Get the tenant from the Login URL, MSA backed accounts will not return `Bearer` www-authenticate header.
                    var bearerResult = response.Headers.WwwAuthenticate.Where(p => p.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
                    if (bearerResult != null && bearerResult.Parameter.StartsWith("authorization_uri=", StringComparison.OrdinalIgnoreCase))
                    {
                        var authorizationUri = bearerResult.Parameter.Substring("authorization_uri=".Length);
                        if (Uri.TryCreate(authorizationUri, UriKind.Absolute, out Uri aadTenantUrl))
                        {
                            return aadTenantUrl;
                        }
                    }

                    return null;
                }
            }
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
            catch (MsalServiceException ex)
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
                result = null;
            }
            catch (MsalClientException ex)
            {
                trace.Warning(ex.Message);
                result = null;
            }
            return result;
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

            trace.Info("token retrieved: {0} chars", token.Length);

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

            trace.Info("token retrieved: {0} chars", token.Length);

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

            trace.Info("username retrieved: {0} chars", username.Length);
            trace.Info("password retrieved: {0} chars", password.Length);

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
            trace.Info("tenant id retrieved: {0} chars", tenantId.Length);

            CredentialData.Data.TryGetValue(Constants.Agent.CommandLine.Args.ClientId, out string clientId);
            ArgUtil.NotNullOrEmpty(clientId, nameof(clientId));
            trace.Info("client id retrieved: {0} chars", clientId.Length);

            CredentialData.Data.TryGetValue(Constants.Agent.CommandLine.Args.ClientSecret, out string clientSecret);
            ArgUtil.NotNullOrEmpty(clientSecret, nameof(clientSecret));
            trace.Info("client secret retrieved: {0} chars", clientSecret.Length);

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
