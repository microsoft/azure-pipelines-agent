// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Runtime.CompilerServices;
using Xunit;
using Moq;

namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    public sealed class VstsAgentWebProxyL0
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void CanProcessBypassHostsFromEnvironmentCorrectly()
        {
            using (var _hc = Setup(false))
            {
                var answers = new string[] {
                    "127\\.0\\.0\\.1",
                    "\\.ing\\.net",
                    "\\.intranet",
                    "\\.corp\\.int",
                    ".*corp\\.int",
                    "127\\.0\\.0\\.1"
                };

                // Ensure clean slate: remove any file-based bypass entries and clear any existing in-memory list
                var proxyBypassPath = _hc.GetConfigFile(WellKnownConfigFile.ProxyBypass);
                if (File.Exists(proxyBypassPath))
                {
                    File.Delete(proxyBypassPath);
                }

                // Preserve and set environment; restore after
                var prevNoProxy = Environment.GetEnvironmentVariable("no_proxy");
                try
                {
                    Environment.SetEnvironmentVariable("no_proxy", "127.0.0.1,.ing.net,.intranet,.corp.int,.*corp.int,127\\.0\\.0\\.1");

                    var vstsAgentWebProxy = new VstsAgentWebProxy();
                    vstsAgentWebProxy.Initialize(_hc);

                    // Clear any state on the instance to avoid accumulation across calls
                    vstsAgentWebProxy.ProxyBypassList.Clear();

                    vstsAgentWebProxy.LoadProxyBypassList();

                    // Assert strictly the six env-derived patterns in order
                    Assert.NotNull(vstsAgentWebProxy.ProxyBypassList);
                    Assert.Equal(6, vstsAgentWebProxy.ProxyBypassList.Count);
                    for (int i = 0; i < answers.Length; i++)
                    {
                        Assert.Equal(answers[i], vstsAgentWebProxy.ProxyBypassList[i]);
                    }
                }
                finally
                {
                    Environment.SetEnvironmentVariable("no_proxy", prevNoProxy);
                }
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void UseBasicAuthForProxySetupAndPersistence()
        {
            using (var _hc = Setup(false))
            {
                var vstsAgentWebProxy = new VstsAgentWebProxy();
                vstsAgentWebProxy.Initialize(_hc);

                // Test SetupProxy with basic auth enabled
                vstsAgentWebProxy.SetupProxy("http://proxy.example.com:8080", "testuser", "testpass", true);

                // Assert proxy properties are set correctly
                Assert.Equal("http://proxy.example.com:8080", vstsAgentWebProxy.ProxyAddress);
                Assert.Equal("testuser", vstsAgentWebProxy.ProxyUsername);
                Assert.Equal("testpass", vstsAgentWebProxy.ProxyPassword);
                Assert.True(vstsAgentWebProxy.UseBasicAuthForProxy);

                // Test SetupProxy with basic auth disabled (default behavior)
                vstsAgentWebProxy.SetupProxy("http://proxy2.example.com:8080", "testuser2", "testpass2", false);
                Assert.Equal("http://proxy2.example.com:8080", vstsAgentWebProxy.ProxyAddress);
                Assert.Equal("testuser2", vstsAgentWebProxy.ProxyUsername);
                Assert.Equal("testpass2", vstsAgentWebProxy.ProxyPassword);
                Assert.False(vstsAgentWebProxy.UseBasicAuthForProxy);

                // Test legacy SetupProxy method (should default to false)
                vstsAgentWebProxy.SetupProxy("http://proxy3.example.com:8080", "testuser3", "testpass3");
                Assert.Equal("http://proxy3.example.com:8080", vstsAgentWebProxy.ProxyAddress);
                Assert.Equal("testuser3", vstsAgentWebProxy.ProxyUsername);
                Assert.Equal("testpass3", vstsAgentWebProxy.ProxyPassword);
                Assert.False(vstsAgentWebProxy.UseBasicAuthForProxy); // Should default to false
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void CanSetupProxyWithBasicAuthFlag()
        {
            using (var _hc = Setup(false))
            {
                // Arrange
                var vstsAgentWebProxy = new VstsAgentWebProxy();
                vstsAgentWebProxy.Initialize(_hc);
                
                string proxyAddress = "http://proxy.example.com:8080";
                string proxyUsername = "testuser";
                string proxyPassword = "testpass";
                
                // Test basic auth enabled
                vstsAgentWebProxy.SetupProxy(proxyAddress, proxyUsername, proxyPassword, true);
                
                // Assert basic auth flag is set
                Assert.True(vstsAgentWebProxy.UseBasicAuthForProxy);
                Assert.Equal(proxyAddress, vstsAgentWebProxy.ProxyAddress);
                Assert.Equal(proxyUsername, vstsAgentWebProxy.ProxyUsername);
                Assert.Equal(proxyPassword, vstsAgentWebProxy.ProxyPassword);
                
                // Test basic auth disabled  
                vstsAgentWebProxy.SetupProxy(proxyAddress, proxyUsername, proxyPassword, false);
                
                // Assert basic auth flag is false
                Assert.False(vstsAgentWebProxy.UseBasicAuthForProxy);
                
                // Test legacy method (should default to false)
                vstsAgentWebProxy.SetupProxy(proxyAddress, proxyUsername, proxyPassword);
                
                // Assert basic auth defaults to false
                Assert.False(vstsAgentWebProxy.UseBasicAuthForProxy);
            }
        }

        public TestHostContext Setup(bool skipServerCertificateValidation, [CallerMemberName] string testName = "")
        {
            var _hc = new TestHostContext(this, testName);
            var certService = new Mock<IAgentCertificateManager>();

            certService.Setup(x => x.SkipServerCertificateValidation).Returns(skipServerCertificateValidation);

            _hc.SetSingleton(certService.Object);

            return _hc;
        }
    }
}
