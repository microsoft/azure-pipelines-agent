// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.WebPlatform;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading.Tasks;
using Agent.Sdk.Util;

namespace Microsoft.VisualStudio.Services.Agent.Worker.Telemetry
{
    public class TelemetryCommandExtension : BaseWorkerCommandExtension
    {
        public TelemetryCommandExtension()
        {
            CommandArea = "telemetry";
            SupportedHostTypes = HostTypes.All;
            InstallWorkerCommand(new PublishTelemetryCommand());
        }
        public TelemetryCommandExtension(bool IsAgentTelemetry = false)
        {
            CommandArea = "telemetry";
            SupportedHostTypes = HostTypes.All;
            InstallWorkerCommand(new PublishTelemetryCommand(IsAgentTelemetry: IsAgentTelemetry));
        }
    }

    [CommandRestriction(AllowedInRestrictedMode = true)]
    public sealed class PublishTelemetryCommand : IWorkerCommand
    {
        public string Name => "publish";
        public List<string> Aliases => null;

        public bool IsAgentTelemetry;

        public PublishTelemetryCommand() 
        {
            IsAgentTelemetry = false;
        }

        public PublishTelemetryCommand(bool IsAgentTelemetry = false)
        {
            this.IsAgentTelemetry = IsAgentTelemetry;
        }

        public void Execute(IExecutionContext context, Command command)
        {
            ArgUtil.NotNull(context, nameof(context));
            ArgUtil.NotNull(command, nameof(command));

            context.Variables.TryGetValue(Constants.Variables.Task.PublishTelemetry, out string publishTelemetryVar);
            if (bool.TryParse(publishTelemetryVar, out bool publishTelemetry) && !publishTelemetry && !IsAgentTelemetry)
            {
                return;
            }

            Dictionary<string, string> eventProperties = command.Properties;
            string data = command.Data;
            string area;
            if (!eventProperties.TryGetValue(WellKnownEventTrackProperties.Area, out area) || string.IsNullOrEmpty(area))
            {
                throw new ArgumentException(StringUtil.Loc("ArgumentNeeded", "Area"));
            }

            string feature;
            if (!eventProperties.TryGetValue(WellKnownEventTrackProperties.Feature, out feature) || string.IsNullOrEmpty(feature))
            {
                throw new ArgumentException(StringUtil.Loc("ArgumentNeeded", "Feature"));
            }

            if (string.IsNullOrEmpty(data))
            {
                throw new ArgumentException(StringUtil.Loc("ArgumentNeeded", "EventTrackerData"));
            }

            CustomerIntelligenceEvent ciEvent;
            try
            {
                var ciProperties = JsonConvert.DeserializeObject<Dictionary<string, object>>(data);
                ciEvent = new CustomerIntelligenceEvent()
                {
                    Area = area,
                    Feature = feature,
                    Properties = ciProperties
                };
            }
            catch (Exception ex)
            {
                throw new ArgumentException(StringUtil.Loc("TelemetryCommandDataError", data, ex.Message));
            }

            PublishEvent(context, ciEvent);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA2000:Dispose objects before losing scope", MessageId = "GetVssConnection")]
        public void PublishEvent(IExecutionContext context, CustomerIntelligenceEvent ciEvent)
        {
            ICustomerIntelligenceServer ciService;
            VssConnection vssConnection;
            try
            {
                ciService = context.GetHostContext().GetService<ICustomerIntelligenceServer>();
                vssConnection = WorkerUtilities.GetVssConnection(context);
                ciService.Initialize(vssConnection);
            }
            catch (SocketException ex)
            {
                ExceptionsUtil.HandleSocketException(ex, WorkerUtilities.GetVssConnection(context).Uri.ToString(), context.Warning);
                return;
            }
            catch (Exception ex)
            {
                context.Warning(StringUtil.Loc("TelemetryCommandFailed", ex.Message));
                return;
            }

            var commandContext = context.GetHostContext().CreateService<IAsyncCommandContext>();
            commandContext.InitializeCommandContext(context, StringUtil.Loc("Telemetry"));
            commandContext.Task = PublishEventsAsync(context, ciService, ciEvent);
        }

        private async Task PublishEventsAsync(IExecutionContext context, ICustomerIntelligenceServer ciService, CustomerIntelligenceEvent ciEvent)
        {
            try
            {
                await ciService.PublishEventsAsync(new CustomerIntelligenceEvent[] { ciEvent });
            }
            catch (Exception ex)
            {
                context.Warning(StringUtil.Loc("TelemetryCommandFailed", ex.Message));
            }
        }


        internal static class WellKnownEventTrackProperties
        {
            internal static readonly string Area = "area";
            internal static readonly string Feature = "feature";
        }
    }
}