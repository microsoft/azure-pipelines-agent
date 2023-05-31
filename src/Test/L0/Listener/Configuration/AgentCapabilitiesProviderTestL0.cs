// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.Services.Agent.Capabilities;
using Microsoft.VisualStudio.Services.Agent.Listener.Configuration;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Listener
{
    public sealed class AgentCapabilitiesProviderTestL0
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Agent")]
        public async void TestGetCapabilities()
        {
            using (var hc = new TestHostContext(this))
            using (var tokenSource = new CancellationTokenSource())
            {
                Mock<IConfigurationManager> configurationManager = new Mock<IConfigurationManager>();
                hc.SetSingleton<IConfigurationManager>(configurationManager.Object);

                // Arrange
                var provider = new AgentCapabilitiesProvider();
                provider.Initialize(hc);
                var settings = new AgentSettings() { AgentName = "IAmAgent007" };

                // Act
                List<Capability> capabilities = await provider.GetCapabilitiesAsync(settings, tokenSource.Token);

                // Assert
                Assert.NotNull(capabilities);
                Capability agentNameCapability = capabilities.SingleOrDefault(x => string.Equals(x.Name, "Agent.Name", StringComparison.Ordinal));
                Assert.NotNull(agentNameCapability);
                Assert.Equal("IAmAgent007", agentNameCapability.Value);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Agent")]
        public async void TestInteractiveSessionCapability()
        {
            using (var hc = new TestHostContext(this))
            using (var tokenSource = new CancellationTokenSource())
            {
                hc.StartupType = StartupType.AutoStartup;
                await VerifyInteractiveSessionCapability(hc, true, tokenSource.Token);

                hc.StartupType = StartupType.Service;
                await VerifyInteractiveSessionCapability(hc, false, tokenSource.Token);

                hc.StartupType = StartupType.Manual;
                await VerifyInteractiveSessionCapability(hc, true, tokenSource.Token);
            }
        }

        private async Task VerifyInteractiveSessionCapability(IHostContext hc, bool expectedValue, CancellationToken token)
        {
            // Arrange
            var provider = new AgentCapabilitiesProvider();
            provider.Initialize(hc);
            var settings = new AgentSettings() { AgentName = "IAmAgent007" };

            // Act
            List<Capability> capabilities = await provider.GetCapabilitiesAsync(settings, token);

            // Assert
            Assert.NotNull(capabilities);
            Capability iSessionCapability = capabilities.SingleOrDefault(x => string.Equals(x.Name, "InteractiveSession", StringComparison.Ordinal));
            Assert.NotNull(iSessionCapability);
            bool.TryParse(iSessionCapability.Value, out bool isInteractive);
            Assert.Equal(expectedValue, isInteractive);
        }
    }
}
