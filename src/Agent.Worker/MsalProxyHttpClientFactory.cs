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
    internal sealed class MsalProxyHttpClientFactory : IMsalHttpClientFactory, IDisposable
    {
        private readonly HttpClientHandler _handler;
        private readonly HttpClient _httpClient;
        private bool _disposed;

        public MsalProxyHttpClientFactory(IHostContext hostContext)
        {
            ArgUtil.NotNull(hostContext, nameof(hostContext));

            // CreateHttpClientHandler sets Proxy = IVstsAgentWebProxy.WebProxy, the same
            // proxy the rest of the agent's HTTP traffic already uses.
            _handler = hostContext.CreateHttpClientHandler();
            _httpClient = new HttpClient(_handler, disposeHandler: false);
        }

        public HttpClient GetHttpClient() => _httpClient;

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            _httpClient.Dispose();
            _handler.Dispose();
        }
    }
}
