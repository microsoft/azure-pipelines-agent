﻿// Copyright (c) Microsoft Corporation.
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
        /// <exception cref="VssServiceException">Thrown if network issue or feature flag not found on remote endpoint</exception>
        public Task<FeatureFlag> GetFeatureFlagAsync(IHostContext context, string featureFlagName, ITraceWriter traceWriter);

    }

    public class FeatureFlagProvider : AgentService, IFeatureFlagProvider
    {

        public async Task<FeatureFlag> GetFeatureFlagAsync(IHostContext context, string featureFlagName, ITraceWriter traceWriter)
        {
            Trace.Verbose(nameof(GetFeatureFlagAsync));
            ArgUtil.NotNull(featureFlagName, nameof(featureFlagName));
            var credMgr = context.GetService<ICredentialManager>();
            VssCredentials creds = credMgr.LoadCredentials();
            ArgUtil.NotNull(creds, nameof(creds));
            var configManager = context.GetService<IConfigurationManager>();
            AgentSettings settings = configManager.LoadSettings();
            using var vssConnection = VssUtil.CreateConnection(new Uri(settings.ServerUrl), creds, Trace);
            var client = vssConnection.GetClient<FeatureAvailabilityHttpClient>();
            var FeatureFlagStatus = await client.GetFeatureFlagByNameAsync(featureFlagName);
            return FeatureFlagStatus;
        }
    }
}