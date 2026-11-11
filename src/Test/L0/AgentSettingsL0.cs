// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Microsoft.VisualStudio.Services.Agent.Util;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    public sealed class AgentSettingsL0
    {
        [Theory]
        [InlineData(null)]
        [InlineData("ca3d5eb1-c241-4e20-8d90-bcc7744a4b7f")]
        [InlineData("00000000-0000-0000-0000-000000000000")]
        [Trait("Level", "L0")]
        [Trait("Category", "ConfigurationManagement")]
        public void PreservesProviderAllocationIdAcrossSettingsSerialization(string allocationId)
        {
            string allocationSetting = allocationId == null ? string.Empty : $", \"AgentCloudRequestId\": \"{allocationId}\"";
            string providerSettings = "{\"AgentId\": 17, \"PoolId\": 42" + allocationSetting + "}";
            var settings = StringUtil.ConvertFromJson<AgentSettings>(providerSettings);
            Guid? expectedAllocationId = allocationId == null ? (Guid?)null : Guid.Parse(allocationId);

            Assert.Equal(expectedAllocationId, settings.AgentCloudRequestId);

            string savedSettings = StringUtil.ConvertToJson(settings);
            var reloadedSettings = StringUtil.ConvertFromJson<AgentSettings>(savedSettings);

            Assert.Equal(expectedAllocationId, reloadedSettings.AgentCloudRequestId);
            Assert.Equal(17, reloadedSettings.AgentId);
            Assert.Equal(42, reloadedSettings.PoolId);
            if (allocationId == null)
            {
                Assert.DoesNotContain(nameof(AgentSettings.AgentCloudRequestId), savedSettings, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
