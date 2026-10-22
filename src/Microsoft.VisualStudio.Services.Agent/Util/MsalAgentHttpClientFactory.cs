// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Net.Http;
using Microsoft.Identity.Client;

namespace Microsoft.VisualStudio.Services.Agent.Util
{
    public sealed class MsalAgentHttpClientFactory : IMsalHttpClientFactory, IDisposable
    {
        private readonly HttpClient _httpClient;
        private bool _disposed;

        public MsalAgentHttpClientFactory(IHostContext hostContext)
        {
            ArgUtil.NotNull(hostContext, nameof(hostContext));

#pragma warning disable CA2000 // HttpClient owns and disposes the handler.
#pragma warning disable CA5400 // Certificate revocation behavior is configured by the Agent handler.
            _httpClient = new HttpClient(hostContext.CreateHttpClientHandler());
#pragma warning restore CA5400
#pragma warning restore CA2000
        }

        public HttpClient GetHttpClient() => _httpClient;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _httpClient.Dispose();
        }
    }
}
