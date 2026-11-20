// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.VisualStudio.Services.Agent.Listener.Telemetry
{
    public static class ServiceScriptTelemetry
    {
        public static bool IsValidUpgradeStatus(string status)
        {
            return status == "Stock" || status == "Unrecognized" || status == "Missing" ||
                status == "NoService" || status == "Error";
        }

        public static async Task PublishUpgradeStatusAsync(IHostContext context, string status, Tracing trace)
        {
            if (!IsValidUpgradeStatus(status))
            {
                throw new ArgumentException("Invalid service-script status.", nameof(status));
            }

            var properties = new Dictionary<string, object>
            {
                { "ServiceScriptStatus", status }
            };
            await ListenerFailureTelemetry.PublishAsync(context, "ServiceScriptUpdateCheck", properties, trace);
        }
    }
}
