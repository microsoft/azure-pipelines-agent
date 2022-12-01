// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;
using Moq;

namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    public sealed class HostContextExtensionL0
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void CreateHttpClientHandlerForCertValidationSkipCert()
        {
            // Arrange.
            using (var _hc = Setup(true))
            {
                // Act.
                var httpHandler = _hc.CreateHttpClientHandler();

                // Assert.
                Assert.NotNull(httpHandler.ServerCertificateCustomValidationCallback);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void CreateHttpClientHandlerForCertValidationDontSkipCert()
        {
            // Arrange.
            using (var _hc = Setup(false))
            {
                // Act.
                var httpHandler = _hc.CreateHttpClientHandler();

                // Assert.
                Assert.Null(httpHandler.ServerCertificateCustomValidationCallback);
            }
        }

        public TestHostContext Setup(bool skipServerCertificateValidation, [CallerMemberName] string testName = "")
        {
            var _hc = new TestHostContext(this, testName);
            var certService = new Mock<IAgentCertificateManager>();
            var proxyConfig = new Mock<IVstsAgentWebProxy>();

            certService.Setup(x => x.SkipServerCertificateValidation).Returns(skipServerCertificateValidation);

            _hc.SetSingleton(proxyConfig.Object);
            _hc.SetSingleton(certService.Object);

            return _hc;
        }
    }
}
