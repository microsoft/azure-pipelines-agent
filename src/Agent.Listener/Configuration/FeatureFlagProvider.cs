// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using Microsoft.VisualStudio.Services.Agent;
using Microsoft.VisualStudio.Services.Agent.Listener.Configuration;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.FeatureAvailability;
using Microsoft.VisualStudio.Services.FeatureAvailability.WebApi;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Agent.Listener.Configuration
{
    [ServiceLocator(Default = typeof(FeatureFlagProvider))]
    public interface IFeatureFlagProvider : IAgentService
    {
        /// <summary>
        /// Gets the status of a feature flag from the specified service endpoint.
        /// If request fails, the feature flag is assumed to be off.
        /// </summary>
        /// <param name="context">Agent host contexts</param>
        /// <param name="featureFlagName">The name of the feature flag to get the status of.</param>
        /// <param name="traceWriter">Trace writer for output</param>
        /// <returns>The status of the feature flag.</returns>
        /// <exception cref="InvalidOperationException">Thrown if agent is not configured</exception>
        public Task<FeatureFlag> GetFeatureFlagAsync(IHostContext context, string featureFlagName, ITraceWriter traceWriter, CancellationToken ctk = default);

        public Task<FeatureFlag> GetFeatureFlagWithCred(IHostContext context, string featureFlagName, ITraceWriter traceWriter, AgentSettings settings, VssCredentials creds, CancellationToken ctk = default);
    }
    
    public class FeatureFlagProvider : AgentService, IFeatureFlagProvider
    {

        public async Task<FeatureFlag> GetFeatureFlagAsync(IHostContext context, string featureFlagName,
            ITraceWriter traceWriter, CancellationToken ctk = default)
        {
            traceWriter.Verbose(nameof(GetFeatureFlagAsync));
            ArgUtil.NotNull(featureFlagName, nameof(featureFlagName));

            var credMgr = context.GetService<ICredentialManager>();
            VssCredentials creds = credMgr.LoadCredentials();
            var configManager = context.GetService<IConfigurationManager>();
            AgentSettings settings = configManager.LoadSettings();

            return await GetFeatureFlagWithCred(context, featureFlagName, traceWriter, settings, creds, ctk);
        }

        public async Task<FeatureFlag> GetFeatureFlagWithCred(IHostContext context, string featureFlagName,
            ITraceWriter traceWriter, AgentSettings settings, VssCredentials creds, CancellationToken ctk)
        {
            var agentCertManager = context.GetService<IAgentCertificateManager>();

            ArgUtil.NotNull(creds, nameof(creds));

            using var vssConnection = VssUtil.CreateConnection(new Uri(settings.ServerUrl), creds, traceWriter, agentCertManager.SkipServerCertificateValidation);
            var client = vssConnection.GetClient<FeatureAvailabilityHttpClient>();
            try
            {
                return await client.GetFeatureFlagByNameAsync(featureFlagName, checkFeatureExists: false, ctk);
            }
            catch (Exception e)
            {
                Trace.Warning("Unable to retrieve feature flag status: " + e.ToString());
                return new FeatureFlag(featureFlagName, "", "", "Off", "Off");
            }
        }
    }
}
