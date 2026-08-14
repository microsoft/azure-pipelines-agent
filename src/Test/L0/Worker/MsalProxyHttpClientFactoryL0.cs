// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using Agent.Sdk;
using Microsoft.Identity.Client;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Moq;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker
{
    public sealed class MsalProxyHttpClientFactoryL0
    {
        // Reads the private HttpClientHandler backing the factory so we can assert
        // the proxy the agent configured is the one MSAL will actually use.
        private static HttpClientHandler GetHandler(MsalProxyHttpClientFactory factory)
        {
            FieldInfo handlerField = typeof(MsalProxyHttpClientFactory)
                .GetField("_handler", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(handlerField);
            return (HttpClientHandler)handlerField.GetValue(factory);
        }

        private TestHostContext Setup(IWebProxy webProxy, [System.Runtime.CompilerServices.CallerMemberName] string testName = "")
        {
            var hc = new TestHostContext(this, testName);

            var proxyConfig = new Mock<IVstsAgentWebProxy>();
            proxyConfig.Setup(x => x.WebProxy).Returns(webProxy);

            var certService = new Mock<IAgentCertificateManager>();
            certService.Setup(x => x.SkipServerCertificateValidation).Returns(false);

            hc.SetSingleton(proxyConfig.Object);
            hc.SetSingleton(certService.Object);

            return hc;
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void GetHttpClient_UsesAgentConfiguredProxy()
        {
            // Arrange - the agent is configured with a proxy (e.g. from the .proxy file).
            var expectedProxy = new WebProxy("http://127.0.0.1:8899");
            using (var hc = Setup(expectedProxy))
            using (var factory = new MsalProxyHttpClientFactory(hc))
            {
                // Act
                HttpClient client = factory.GetHttpClient();
                HttpClientHandler handler = GetHandler(factory);

                // Assert - MSAL's HttpClient routes through the agent's proxy (the fix).
                Assert.NotNull(client);
                Assert.Same(expectedProxy, handler.Proxy);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void GetHttpClient_ReturnsSameInstanceAcrossCalls()
        {
            using (var hc = Setup(new WebProxy("http://127.0.0.1:8899")))
            using (var factory = new MsalProxyHttpClientFactory(hc))
            {
                HttpClient first = factory.GetHttpClient();
                HttpClient second = factory.GetHttpClient();

                Assert.NotNull(first);
                Assert.Same(first, second);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Factory_IsMsalHttpClientFactory()
        {
            using (var hc = Setup(new WebProxy("http://127.0.0.1:8899")))
            using (var factory = new MsalProxyHttpClientFactory(hc))
            {
                // MSAL only accepts an IMsalHttpClientFactory via WithHttpClientFactory.
                Assert.IsAssignableFrom<IMsalHttpClientFactory>(factory);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void GetHttpClient_NoProxyConfigured_UsesBypassingAgentProxy()
        {
            // Real no-proxy case: WebProxy is never null, it's an unconfigured AgentWebProxy that bypasses everything.
            var noProxy = new AgentWebProxy();
            using (var hc = Setup(noProxy))
            using (var factory = new MsalProxyHttpClientFactory(hc))
            {
                HttpClient client = factory.GetHttpClient();
                HttpClientHandler handler = GetHandler(factory);

                // Must use the agent's proxy instance, not the .NET default/system proxy.
                Assert.NotNull(client);
                Assert.Same(noProxy, handler.Proxy);

                // With no address configured, the agent proxy bypasses everything (no traffic is proxied).
                var destination = new Uri("https://login.microsoftonline.com");
                Assert.True(handler.Proxy.IsBypassed(destination));
                Assert.Same(destination, handler.Proxy.GetProxy(destination));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Constructor_NullHostContext_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new MsalProxyHttpClientFactory(null));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Dispose_DoesNotThrow()
        {
            using (var hc = Setup(new WebProxy("http://127.0.0.1:8899")))
            {
                var factory = new MsalProxyHttpClientFactory(hc);
                _ = factory.GetHttpClient();

                factory.Dispose();
                factory.Dispose(); // idempotent
            }
        }
    }
}
