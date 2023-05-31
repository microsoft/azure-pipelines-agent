using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.VisualStudio.Services.Agent.Capabilities
{

    public sealed class UserCapabilitiesProvider : AgentService, ICapabilitiesProvider
    {
        public Type ExtensionType => typeof(ICapabilitiesProvider);

        public int Order => 2;

        public Task<List<Capability>> GetCapabilitiesAsync(AgentSettings settings, CancellationToken cancellationToken)
        {
            Trace.Entering();
            var capabilities = new List<Capability>();

            // Location of the .capabilities file
            var capbabilitiesFile = Path.Combine(HostContext.GetDirectory(WellKnownDirectory.Root), ".capabilities");
            if (File.Exists(capbabilitiesFile))
            {
                // Load the file content, and parse it like the .env file.
                Trace.Info($"Reading capabilities from '{capbabilitiesFile}'");
                var fileContents = File.ReadAllLines(capbabilitiesFile);
                foreach (var line in fileContents)
                {
                    if (!string.IsNullOrEmpty(line) && line.IndexOf('=') > 0)
                    {
                        string name = line.Substring(0, line.IndexOf('='));
                        string value = line.Substring(line.IndexOf('=') + 1);

                        Trace.Info($"Adding '{name}': '{value}'");
                        capabilities.Add(new Capability(name, value));
                    }
                }
            }

            return Task.FromResult(capabilities);
        }

    }
}
