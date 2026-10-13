// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading;
using System.Threading.Tasks;
using Agent.Listener.Configuration;
using Microsoft.VisualStudio.Services.Common;
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

                // Nothing listens on port 1, so connecting to the server fails while creating the client.
                var settings = new AgentSettings() { ServerUrl = "http://127.0.0.1:1" };

                var featureFlag = await featureFlagProvider.GetFeatureFlagWithCred(hc, "DistributedTask.Agent.TestFeatureFlag", hc.GetTrace(), settings, new VssCredentials(), CancellationToken.None);

                Assert.Equal("Off", featureFlag.EffectiveState);
            }
        }
    }
}
