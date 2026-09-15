// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.VisualStudio.Services.Agent.Capabilities
{
    public sealed class PwshCapabilitiesProvider : AgentService, ICapabilitiesProvider
    {
        public Type ExtensionType => typeof(ICapabilitiesProvider);

        public int Order => 2;

        public Task<List<Capability>> GetCapabilitiesAsync(AgentSettings settings, CancellationToken cancellationToken)
        {
            Trace.Entering();
            var capabilities = new List<Capability>();

            try
            {
                string pwsh = HostContext.GetService<IPwshExeUtil>().GetPath();
                capabilities.Add(new Capability("Pwsh", pwsh));
            }
            catch (Exception ex)
            {
                Trace.Info($"Pwsh capability not detected: {ex.Message}");
            }

            return Task.FromResult(capabilities);
        }
    }
}
