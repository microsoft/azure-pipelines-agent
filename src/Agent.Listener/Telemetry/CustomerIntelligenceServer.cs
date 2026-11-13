// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using System.Threading;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.CustomerIntelligence.WebApi;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.WebPlatform;

namespace Microsoft.VisualStudio.Services.Agent.Listener.Telemetry
{
    [ServiceLocator(Default = typeof(CustomerIntelligenceServer))]
    public interface ICustomerIntelligenceServer : IAgentService
    {
        void Initialize(VssConnection connection);
        Task InitializeAsync(VssConnection connection, CancellationToken cancellationToken);
        Task PublishEventsAsync(CustomerIntelligenceEvent[] ciEvents);
        Task PublishEventsAsync(CustomerIntelligenceEvent[] ciEvents, CancellationToken cancellationToken);
    }

    // This service is used for tracking task events which are applicable for VSTS internal tasks
    public class CustomerIntelligenceServer : AgentService, ICustomerIntelligenceServer
    {
        private CustomerIntelligenceHttpClient _ciClient;

        public void Initialize(VssConnection connection)
        {
            ArgUtil.NotNull(connection, nameof(connection));
            _ciClient = connection.GetClient<CustomerIntelligenceHttpClient>();
        }

        public Task PublishEventsAsync(CustomerIntelligenceEvent[] ciEvents)
        {
            return _ciClient.PublishEventsAsync(events: ciEvents);
        }

        public async Task InitializeAsync(VssConnection connection, CancellationToken cancellationToken)
        {
            ArgUtil.NotNull(connection, nameof(connection));
            _ciClient = await connection.GetClientAsync<CustomerIntelligenceHttpClient>(cancellationToken);
        }

        public Task PublishEventsAsync(CustomerIntelligenceEvent[] ciEvents, CancellationToken cancellationToken)
        {
            return _ciClient.PublishEventsAsync(events: ciEvents, cancellationToken: cancellationToken);
        }
    }
}
