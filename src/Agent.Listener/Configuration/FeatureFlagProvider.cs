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
        /// <exception cref="VssUnauthorizedException">Thrown if token is not suitable for retriving feature flag status</exception>
        /// <exception cref="InvalidOperationException">Thrown if agent is not configured</exception>
        public Task<FeatureFlag> GetFeatureFlagAsync(IHostContext context, string featureFlagName, ITraceWriter traceWriter);

    }

    public class FeatureFlagProvider : AgentService, IFeatureFlagProvider
    {

        public async Task<FeatureFlag> GetFeatureFlagAsync(IHostContext context, string featureFlagName, ITraceWriter traceWriter)
        {
            traceWriter.Verbose(nameof(GetFeatureFlagAsync));
            ArgUtil.NotNull(featureFlagName, nameof(featureFlagName));

            var credMgr = context.GetService<ICredentialManager>();
            var configManager = context.GetService<IConfigurationManager>();

            VssCredentials creds = credMgr.LoadCredentials();
            ArgUtil.NotNull(creds, nameof(creds));

            AgentSettings settings = configManager.LoadSettings();
            using var vssConnection = VssUtil.CreateConnection(new Uri(settings.ServerUrl), creds, traceWriter);
            var client = vssConnection.GetClient<FeatureAvailabilityHttpClient>();
            try
            {
                // if feature flag doesn't exist, that is traced as an error and logged to Agent log like:
                // [2023-10-06 22:20:15Z ERR  VisualStudioServices] GET request to https://.../_apis/FeatureFlags/FFName failed. HTTP Status: NotFound, [...]
                // 
                // The problem is that we surface that in the console as: 
                // Error reported in diagnostic logs. Please examine the log for more details.
                // - /path/to/logs/_diag/Agent_20231006-231735-utc.log
                //
                // Reporting an error could cause confusion. Added a flag to the listener so we can temporarily intercept the trace error and convert to warning
                HostTraceListener.ErrorsAsWarnings = true;
                return await client.GetFeatureFlagByNameAsync(featureFlagName);
            }
            catch (VssServiceException e)
            {
                Trace.Warning("Unable to retrive feature flag status: " + e.ToString());
                return new FeatureFlag(featureFlagName, "", "", "Off", "Off");
            }
            finally
            {
                HostTraceListener.ErrorsAsWarnings = false;
            }
        }
    }
}
