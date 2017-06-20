using Microsoft.VisualStudio.Services.Agent.Listener.Capabilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Xunit;
using Moq;
using Microsoft.VisualStudio.Services.Agent.Listener.Configuration;

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

#if OS_WINDOWS
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Agent")]
        public async void TestInteractiveSessionCapability()
        {
            using (var hc = new TestHostContext(this))
            using (var tokenSource = new CancellationTokenSource())
            {
                Mock<IConfigurationManager> configurationManager = new Mock<IConfigurationManager>();
                hc.SetSingleton<IConfigurationManager>(configurationManager.Object);

                configurationManager.Setup(x => x.IsServiceConfigured()).Returns(false);

                // Arrange
                var provider = new AgentCapabilitiesProvider();
                provider.Initialize(hc);
                var settings = new AgentSettings() { AgentName = "IAmAgent007" };

                // Act
                List<Capability> capabilities = await provider.GetCapabilitiesAsync(settings, tokenSource.Token);

                // Assert
                Assert.NotNull(capabilities);
                Capability iSessionCapability = capabilities.SingleOrDefault(x => string.Equals(x.Name, "InteractiveSession", StringComparison.Ordinal));
                Assert.NotNull(iSessionCapability);
                
                Assert.True(iSessionCapability.Value.Equals("true", StringComparison.OrdinalIgnoreCase));
            }
        }
#endif
    }
}
