using Microsoft.VisualStudio.Services.CustomerIntelligence.WebApi;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.WebPlatform;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.VisualStudio.Services.Agent.Worker.TestResults
{
    public class TelemetryPublisher
    {
        private static CustomerIntelligenceHttpClient _ciClient;

        public TelemetryPublisher(VssConnection connection)
        {
            _ciClient = connection.GetClient<CustomerIntelligenceHttpClient>();
        }

        public async Task PublishAsync(string area, string feature, Dictionary<string, object> telemetryProperties)
        {
            CustomerIntelligenceEvent ciEvent = new CustomerIntelligenceEvent()
            {
                Area = area,
                Feature = feature,
                Properties = telemetryProperties
            };

            await _ciClient.PublishEventsAsync(new CustomerIntelligenceEvent[] { ciEvent });
        }
    }
}
