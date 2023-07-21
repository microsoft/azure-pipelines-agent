// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.VisualStudio.Services.Agent.Listener.Configuration;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebPlatform;

using Newtonsoft.Json;

namespace Microsoft.VisualStudio.Services.Agent.Listener.Telemetry
{
    [ServiceLocator(Default = typeof(TelemetryPublisher))]
    public interface IAgenetListenerTelemetryPublisher : IAgentService
    {
        public Task PublishEvent(IHostContext context, Command command);
    }

    public sealed class TelemetryPublisher : AgentService, IAgenetListenerTelemetryPublisher
    {
        private ICustomerIntelligenceServer _ciService;

        public string Name => "publish";
        public List<string> Aliases => null;


        public async Task PublishEvent(IHostContext context, Command command)
        {

            if (_ciService == null)
            {
                var credMgr = context.GetService<ICredentialManager>();
                VssCredentials creds = credMgr.LoadCredentials();

                ArgUtil.NotNull(creds, nameof(creds));

                var configManager = context.GetService<IConfigurationManager>();
                AgentSettings settings = configManager.LoadSettings();


                using var vssConnection = VssUtil.CreateConnection(new Uri(settings.ServerUrl), creds, Trace);
                try
                {
                    _ciService = context.GetService<ICustomerIntelligenceServer>();
                    _ciService.Initialize(vssConnection);
                }

                catch (Exception ex)
                {
                    Trace.Warning(StringUtil.Loc("TelemetryCommandFailed", ex.Message));
                    return;
                }
            }

            ArgUtil.NotNull(context, nameof(context));
            ArgUtil.NotNull(command, nameof(command));

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

            await PublishEventsAsync(context, ciEvent);
        }

        private async Task PublishEventsAsync(IHostContext context, CustomerIntelligenceEvent ciEvent)
        {
            try
            {
                await _ciService.PublishEventsAsync(new CustomerIntelligenceEvent[] { ciEvent });
            }
            catch (Exception ex)
            {
                Trace.Warning(StringUtil.Loc("TelemetryCommandFailed", ex.Message));
            }
        }
    }
    internal static class WellKnownEventTrackProperties
    {
        internal static readonly string Area = "area";
        internal static readonly string Feature = "feature";
    }
}