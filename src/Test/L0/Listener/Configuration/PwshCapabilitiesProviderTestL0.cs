// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.Services.Agent.Capabilities;
using Microsoft.VisualStudio.Services.Agent.Util;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Listener
{
    public sealed class PwshCapabilitiesProviderTestL0
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Agent")]
        public async Task AddsPwshCapabilityWhenPwshExists()
        {
            using var hc = new TestHostContext(this);
            using var tokenSource = new CancellationTokenSource();

            var pwshUtil = new Mock<IPwshExeUtil>();
            pwshUtil.Setup(x => x.GetPath()).Returns("/usr/bin/pwsh");
            hc.SetSingleton<IPwshExeUtil>(pwshUtil.Object);

            var provider = new PwshCapabilitiesProvider();
            provider.Initialize(hc);

            List<Capability> capabilities = await provider.GetCapabilitiesAsync(new AgentSettings(), tokenSource.Token);

            Capability pwshCapability = capabilities.SingleOrDefault(x => string.Equals(x.Name, "Pwsh", StringComparison.Ordinal));
            Assert.NotNull(pwshCapability);
            Assert.Equal("/usr/bin/pwsh", pwshCapability.Value);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Agent")]
        public async Task ReturnsNoPwshCapabilityWhenPwshIsUnavailable()
        {
            using var hc = new TestHostContext(this);
            using var tokenSource = new CancellationTokenSource();

            var pwshUtil = new Mock<IPwshExeUtil>();
            pwshUtil.Setup(x => x.GetPath()).Throws(new InvalidOperationException("pwsh missing"));
            hc.SetSingleton<IPwshExeUtil>(pwshUtil.Object);

            var provider = new PwshCapabilitiesProvider();
            provider.Initialize(hc);

            List<Capability> capabilities = await provider.GetCapabilitiesAsync(new AgentSettings(), tokenSource.Token);

            Assert.DoesNotContain(capabilities, x => string.Equals(x.Name, "Pwsh", StringComparison.Ordinal));
        }
    }
}
