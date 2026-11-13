// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Microsoft.VisualStudio.Services.Agent.Listener.Telemetry
{
    internal static class ListenerFailureTelemetry
    {
        internal static async Task PublishAsync(IHostContext context, string feature, Dictionary<string, object> properties, Tracing trace)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                properties["AgentVersion"] = BuildConstants.AgentPackage.Version;
                var command = new Command("telemetry", "publish")
                {
                    Data = JsonConvert.SerializeObject(properties)
                };
                command.Properties.Add("area", "AzurePipelinesAgent");
                command.Properties.Add("feature", feature);
                var publisher = context.GetService<IAgenetListenerTelemetryPublisher>();
                await publisher.PublishEvent(context, command, timeout.Token);
            }
            catch (Exception ex)
            {
                trace.Warning($"Unable to publish {feature} telemetry: {ex}");
            }
        }
    }
}
