// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Services.Agent.Listener.Configuration;
using Microsoft.VisualStudio.Services.Agent.Listener.Telemetry;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.WebPlatform;
using Moq;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Listener
{
    public sealed class ListenerTelemetryL0
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Agent")]
        public async Task PassesCancellationToClientInitializationAndPublication()
        {
            using (var hc = new TestHostContext(this))
            using (var cancellation = new CancellationTokenSource())
            {
                var credentials = new Mock<ICredentialManager>();
                credentials.Setup(x => x.LoadCredentials()).Returns(new VssCredentials());
                hc.SetSingleton<ICredentialManager>(credentials.Object);
                var config = new Mock<IConfigurationManager>();
                config.Setup(x => x.LoadSettings()).Returns(new AgentSettings { ServerUrl = "https://dev.azure.com/example" });
                hc.SetSingleton<IConfigurationManager>(config.Object);
                var ciServer = new Mock<ICustomerIntelligenceServer>();
                hc.SetSingleton<ICustomerIntelligenceServer>(ciServer.Object);
                var command = new Command("telemetry", "publish") { Data = "{\"ExceptionType\":\"System.InvalidOperationException\"}" };
                command.Properties.Add("area", "AzurePipelinesAgent");
                command.Properties.Add("feature", "WorkerStartupFailure");
                var publisher = new TelemetryPublisher();
                publisher.Initialize(hc);

                await publisher.PublishEvent(hc, command, cancellation.Token);

                ciServer.Verify(x => x.InitializeAsync(It.IsAny<VssConnection>(), cancellation.Token), Times.Once);
                ciServer.Verify(x => x.PublishEventsAsync(
                    It.Is<CustomerIntelligenceEvent[]>(events => events.Length == 1 && events[0].Feature == "WorkerStartupFailure"),
                    cancellation.Token), Times.Once);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Agent")]
        public async Task DoesNotInitializeClientWhenReportingWasCanceled()
        {
            using (var hc = new TestHostContext(this))
            using (var cancellation = new CancellationTokenSource())
            {
                var ciServer = new Mock<ICustomerIntelligenceServer>();
                hc.SetSingleton<ICustomerIntelligenceServer>(ciServer.Object);
                var publisher = new TelemetryPublisher();
                publisher.Initialize(hc);
                cancellation.Cancel();

                await publisher.PublishEvent(hc, new Command("telemetry", "publish"), cancellation.Token);

                ciServer.Verify(x => x.InitializeAsync(It.IsAny<VssConnection>(), It.IsAny<CancellationToken>()), Times.Never);
            }
        }
    }
}
