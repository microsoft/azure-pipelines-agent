// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;
using Moq;
using Microsoft.VisualStudio.Services.Agent;
using Agent.Sdk.Knob;

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

                // Act.
                Environment.SetEnvironmentVariable("no_proxy", "127.0.0.1,.ing.net,.intranet,.corp.int,.*corp.int,127\\.0\\.0\\.1");
                var vstsAgentWebProxy = new VstsAgentWebProxy();
                vstsAgentWebProxy.Initialize(_hc);
                vstsAgentWebProxy.LoadProxyBypassList();

                // Assert
                Assert.NotNull(vstsAgentWebProxy.ProxyBypassList);
                Assert.True(vstsAgentWebProxy.ProxyBypassList.Count == 6);
                for (int i = 0; i < answers.Length; i++)
                {
                    Assert.Equal(answers[i], vstsAgentWebProxy.ProxyBypassList[i]);
                }
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
