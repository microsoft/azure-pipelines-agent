// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Microsoft.VisualStudio.Services.Agent.Listener.Configuration
{
    public class ServiceControlManager : AgentService
    {
        public void CalculateServiceName(AgentSettings settings, string serviceNamePattern, string serviceDisplayNamePattern, out string serviceName, out string serviceDisplayName)
        {
            ArgUtil.NotNull(settings, nameof(settings));
            Trace.Entering();
            serviceName = string.Empty;
            serviceDisplayName = string.Empty;

            Uri accountUri = new Uri(settings.ServerUrl);
            string accountName = string.Empty;

            if (accountUri.Host.Equals("dev.azure.com", StringComparison.OrdinalIgnoreCase))
            {
                accountName = accountUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            }
            else
            {
                accountName = accountUri.Host.Split('.').FirstOrDefault();
            }

            if (string.IsNullOrEmpty(accountName))
            {
                throw new InvalidOperationException(StringUtil.Loc("CannotFindHostName", settings.ServerUrl));
            }

            serviceName = StringUtil.Format(serviceNamePattern, accountName, settings.PoolName, settings.AgentName);

            if (serviceName.Length > 80)
            {
                Trace.Verbose($"Calculated service name is too long (> 80 chars). Trying again by calculating a shorter name.");

                int exceededCharLength = serviceName.Length - 80;
                string accountNameSubstring = StringUtil.SubstringPrefix(accountName, 25);

                exceededCharLength -= accountName.Length - accountNameSubstring.Length;

                string poolNameSubstring = StringUtil.SubstringPrefix(settings.PoolName, 25);

                if (settings.PoolName != null)
                {
                    exceededCharLength -= settings.PoolName.Length - poolNameSubstring.Length;
                }

                string agentNameSubstring = settings.AgentName;

                // Only trim agent name if it's really necessary
                if (exceededCharLength > 0)
                {
                    agentNameSubstring = StringUtil.SubstringPrefix(settings.AgentName, settings.AgentName.Length - exceededCharLength);
                }

                serviceName = StringUtil.Format(serviceNamePattern, accountNameSubstring, poolNameSubstring, agentNameSubstring);
            }

            serviceDisplayName = StringUtil.Format(serviceDisplayNamePattern, accountName, settings.PoolName, settings.AgentName);

            Trace.Info($"Service name '{serviceName}' display name '{serviceDisplayName}' will be used for service configuration.");
        }
    }
}