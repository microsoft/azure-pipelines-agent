// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Services.Agent.Listener;
using Microsoft.VisualStudio.Services.Agent.Listener.Telemetry;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Listener
{
    public sealed class ServiceScriptL0
    {
        [Fact]
        [Trait("Level", "L0")]
        public void DetectsMatchingAndDifferentServiceScripts()
        {
            string directory = Path.Combine(Path.GetTempPath(), $"agent-service-script-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            try
            {
                string service = Path.Combine(directory, "runsvc.sh");
                string shipped = Path.Combine(directory, "shipped.sh");
                File.WriteAllText(shipped, "stock");
                File.WriteAllText(service, "stock");
                Assert.Equal(ServiceScriptStatus.Match, ServiceScriptInspector.GetStatus(service, shipped));

                File.WriteAllText(service, "other");
                Assert.Equal(ServiceScriptStatus.Different, ServiceScriptInspector.GetStatus(service, shipped));

                File.Delete(service);
                Assert.Equal(ServiceScriptStatus.Missing, ServiceScriptInspector.GetStatus(service, shipped));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        public void ReportsMissingShippedScriptAsAnError()
        {
            string directory = Path.Combine(Path.GetTempPath(), $"agent-service-script-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            try
            {
                string service = Path.Combine(directory, "runsvc.sh");
                File.WriteAllText(service, "stock");
                Assert.Throws<FileNotFoundException>(() =>
                    ServiceScriptInspector.GetStatus(service, Path.Combine(directory, "missing.sh")));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        public void DetectsSymlinkedServiceScript()
        {
            if (TestUtil.IsWindows())
            {
                return;
            }

            string directory = Path.Combine(Path.GetTempPath(), $"agent-service-script-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            try
            {
                string shipped = Path.Combine(directory, "shipped.sh");
                string service = Path.Combine(directory, "runsvc.sh");
                File.WriteAllText(shipped, "stock");
                File.CreateSymbolicLink(service, shipped);
                Assert.Equal(ServiceScriptStatus.Different, ServiceScriptInspector.GetStatus(service, shipped));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Theory]
        [Trait("Level", "L0")]
        [InlineData("Unrecognized")]
        [InlineData("Node24Unsupported")]
        public async Task PublishesOnlyTheUpgradeClassificationAndAgentVersion(string status)
        {
            using (var context = new TestHostContext(this))
            {
                var publisher = new Mock<IAgenetListenerTelemetryPublisher>();
                Command published = null;
                publisher.Setup(p => p.PublishEvent(context, It.IsAny<Command>(), It.IsAny<CancellationToken>()))
                    .Callback<IHostContext, Command, CancellationToken>((_, command, _) => published = command)
                    .Returns(Task.CompletedTask);
                context.SetSingleton(publisher.Object);

                await ServiceScriptTelemetry.PublishUpgradeStatusAsync(context, status, context.GetTrace(nameof(ServiceScriptL0)));

                Assert.NotNull(published);
                Assert.Equal("AzurePipelinesAgent", published.Properties["area"]);
                Assert.Equal("ServiceScriptUpdateCheck", published.Properties["feature"]);
                JObject properties = JObject.Parse(published.Data);
                Assert.Equal(status, (string)properties["ServiceScriptStatus"]);
                Assert.Equal(2, properties.Count);
                Assert.NotNull(properties["AgentVersion"]);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        public async Task RejectsUnknownTelemetryClassifications()
        {
            using (var context = new TestHostContext(this))
            {
                await Assert.ThrowsAsync<ArgumentException>(() =>
                    ServiceScriptTelemetry.PublishUpgradeStatusAsync(context, "arbitrary script content", context.GetTrace(nameof(ServiceScriptL0))));
            }
        }
    }
}
