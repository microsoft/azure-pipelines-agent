using Microsoft.VisualStudio.Services.Agent.Capabilities;
using Microsoft.VisualStudio.Services.Agent.Listener.Configuration;
using Moq;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Listener
{
    public sealed class UserCapabilitiesProviderTestL0
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Agent")]
        public async void TestGetCapabilitiesWithDotCapabilities()
        {
            using (var hc = new TestHostContext(this))
            using (var tokenSource = new CancellationTokenSource())
            {
                var capFile = Path.Combine(hc.GetDirectory(WellKnownDirectory.Root), ".capabilities");
                try
                {
                    File.WriteAllText(capFile, "User.Capability=My Value");
                    Mock<IConfigurationManager> configurationManager = new Mock<IConfigurationManager>();
                    hc.SetSingleton<IConfigurationManager>(configurationManager.Object);

                    // Arrange
                    var provider = new UserCapabilitiesProvider();
                    provider.Initialize(hc);
                    var settings = new AgentSettings();

                    // Act
                    List<Capability> capabilities = await provider.GetCapabilitiesAsync(settings, tokenSource.Token);

                    // Assert
                    Assert.NotNull(capabilities);
                    Capability myCapability = capabilities.SingleOrDefault(x => string.Equals(x.Name, "User.Capability", StringComparison.Ordinal));
                    Assert.NotNull(myCapability);
                    Assert.Equal("My Value", myCapability.Value);
                    Assert.Equal(1, capabilities.Count);
                }
                finally
                {
                    File.Delete(capFile);
                }
            }
        }
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Agent")]
        public async void TestGetCapabilitiesWithoutDotCapabilities()
        {
            using (var hc = new TestHostContext(this))
            using (var tokenSource = new CancellationTokenSource())
            {
                Mock<IConfigurationManager> configurationManager = new Mock<IConfigurationManager>();
                hc.SetSingleton<IConfigurationManager>(configurationManager.Object);

                // Arrange
                var provider = new UserCapabilitiesProvider();
                provider.Initialize(hc);
                var settings = new AgentSettings() { AgentName = "IAmAgent007" };

                // Act
                List<Capability> capabilities = await provider.GetCapabilitiesAsync(settings, tokenSource.Token);

                // Assert
                Assert.NotNull(capabilities);
                Assert.Empty(capabilities);
            }
        }
    }
}
