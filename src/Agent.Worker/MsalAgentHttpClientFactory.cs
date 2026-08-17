// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Net.Http;
using Microsoft.Identity.Client;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    // Supplies MSAL with an HttpClient that honors the agent's configured web proxy.
    // Without this, MSAL uses its own default HttpClient with no proxy, so Microsoft
    // Entra token acquisition bypasses the agent proxy and fails on proxy-restricted
    // self-hosted agents.
    internal sealed class MsalAgentHttpClientFactory : IMsalHttpClientFactory, IDisposable
    {
        private readonly HttpClient _httpClient;
        private bool _disposed;

        public MsalAgentHttpClientFactory(IHostContext hostContext)
        {
            ArgUtil.NotNull(hostContext, nameof(hostContext));

            // CreateHttpClientHandler sets Proxy = IVstsAgentWebProxy.WebProxy, the same
            // proxy the rest of the agent's HTTP traffic already uses.
#pragma warning disable CA2000 // The HttpClient takes ownership of the handler (disposeHandler defaults to true) and disposes it.
            _httpClient = new HttpClient(hostContext.CreateHttpClientHandler());
#pragma warning restore CA2000
        }

        public HttpClient GetHttpClient() => _httpClient;

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            _httpClient.Dispose();
        }
    }
}
