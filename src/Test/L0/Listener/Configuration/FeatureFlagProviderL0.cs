// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading;
using System.Threading.Tasks;
using Agent.Listener.Configuration;
using Microsoft.VisualStudio.Services.Agent.Listener.Telemetry;
using Microsoft.VisualStudio.Services.Common;
using Moq;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Listener.Configuration
{
    public sealed class FeatureFlagProviderL0
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Agent")]
        public async Task GetFeatureFlagReturnsOffWhenServerUnreachable()
        {
            using (var hc = new TestHostContext(this))
            {
                var featureFlagProvider = new FeatureFlagProvider();
                featureFlagProvider.Initialize(hc);
                var publisher = new Mock<IAgenetListenerTelemetryPublisher>();
                hc.SetSingleton<IAgenetListenerTelemetryPublisher>(publisher.Object);

                // Nothing listens on port 1, so connecting to the server fails while creating the client.
                var settings = new AgentSettings() { ServerUrl = "http://127.0.0.1:1" };

                var featureFlag = await featureFlagProvider.GetFeatureFlagWithCred(hc, "DistributedTask.Agent.TestFeatureFlag", hc.GetTrace(), settings, new VssCredentials(), CancellationToken.None);

                Assert.Equal("Off", featureFlag.EffectiveState);
                publisher.Verify(x => x.PublishEvent(It.IsAny<IHostContext>(), It.IsAny<Command>()), Times.Never);
                publisher.Verify(x => x.PublishEvent(It.IsAny<IHostContext>(), It.IsAny<Command>(), It.IsAny<CancellationToken>()), Times.Never);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Agent")]
        public async Task GetFeatureFlagReturnsOffWithoutTelemetryWhenConnectionCreationFails()
        {
            using (var hc = new TestHostContext(this))
            {
                var provider = new FeatureFlagProvider();
                provider.Initialize(hc);
                var publisher = new Mock<IAgenetListenerTelemetryPublisher>();
                hc.SetSingleton<IAgenetListenerTelemetryPublisher>(publisher.Object);

                var flag = await provider.GetFeatureFlagWithCred(hc, "DistributedTask.Agent.TestFeatureFlag", hc.GetTrace(),
                    new AgentSettings { ServerUrl = "not-a-valid-server-url" }, new VssCredentials(), CancellationToken.None);

                Assert.Equal("Off", flag.EffectiveState);
                publisher.Verify(x => x.PublishEvent(It.IsAny<IHostContext>(), It.IsAny<Command>()), Times.Never);
                publisher.Verify(x => x.PublishEvent(It.IsAny<IHostContext>(), It.IsAny<Command>(), It.IsAny<CancellationToken>()), Times.Never);
            }
        }
    }
}
