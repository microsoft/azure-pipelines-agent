// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading;
using System.Threading.Tasks;
using System.IO;
using Agent.Listener.Configuration;
using Microsoft.VisualStudio.Services.Agent.Listener.Telemetry;
using Microsoft.VisualStudio.Services.Common;
using Moq;
using Newtonsoft.Json.Linq;
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
                Command telemetry = null;
                var publisher = new Mock<IAgenetListenerTelemetryPublisher>();
                publisher.Setup(x => x.PublishEvent(hc, It.IsAny<Command>(), It.IsAny<CancellationToken>()))
                    .Callback((IHostContext context, Command command, CancellationToken token) => telemetry = command)
                    .Returns(Task.CompletedTask);
                hc.SetSingleton<IAgenetListenerTelemetryPublisher>(publisher.Object);

                // Nothing listens on port 1, so connecting to the server fails while creating the client.
                var settings = new AgentSettings() { ServerUrl = "http://127.0.0.1:1" };

                var featureFlag = await featureFlagProvider.GetFeatureFlagWithCred(hc, "DistributedTask.Agent.TestFeatureFlag", hc.GetTrace(), settings, new VssCredentials(), CancellationToken.None);

                Assert.Equal("Off", featureFlag.EffectiveState);
                Assert.NotNull(telemetry);
                Assert.Equal("AzurePipelinesAgent", telemetry.Properties["area"]);
                Assert.Equal("FeatureFlagLookupFailure", telemetry.Properties["feature"]);
                var data = JObject.Parse(telemetry.Data);
                Assert.Equal("DistributedTask.Agent.TestFeatureFlag", data.Value<string>("FeatureFlagName"));
                Assert.Equal("Off", data.Value<string>("FallbackState"));
                Assert.False(string.IsNullOrEmpty(data.Value<string>("ExceptionType")));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Agent")]
        public async Task TelemetryFailureDoesNotPreventOffFallback()
        {
            using (var hc = new TestHostContext(this))
            {
                var provider = new FeatureFlagProvider();
                provider.Initialize(hc);
                var publisher = new Mock<IAgenetListenerTelemetryPublisher>();
                publisher.Setup(x => x.PublishEvent(hc, It.IsAny<Command>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new IOException("Telemetry unavailable"));
                hc.SetSingleton<IAgenetListenerTelemetryPublisher>(publisher.Object);

                var flag = await provider.GetFeatureFlagWithCred(hc, "DistributedTask.Agent.TestFeatureFlag", hc.GetTrace(),
                    new AgentSettings { ServerUrl = "not-a-valid-server-url" }, new VssCredentials(), CancellationToken.None);

                Assert.Equal("Off", flag.EffectiveState);
                publisher.Verify(x => x.PublishEvent(hc, It.IsAny<Command>(), It.Is<CancellationToken>(token => token.CanBeCanceled)), Times.Once);
            }
        }
    }
}
