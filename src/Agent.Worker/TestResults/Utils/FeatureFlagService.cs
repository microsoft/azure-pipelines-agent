// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.FeatureAvailability.WebApi;
using Microsoft.VisualStudio.Services.WebApi;
using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.VisualStudio.Services.Agent.Worker.TestResults.Utils
{
    [ServiceLocator(Default = typeof(FeatureFlagService))]
    public interface IFeatureFlagService : IAgentService
    {
        void InitializeFeatureService(IExecutionContext executionContext, VssConnection connection);

        bool GetFeatureFlagState(string featureFlagName, Guid serviceInstanceId);
    }

    public class FeatureFlagService : AgentService, IFeatureFlagService
    {
        private IExecutionContext _executionContext;
        private VssConnection _connection;

        public void InitializeFeatureService(IExecutionContext executionContext, VssConnection connection)
        {
            Trace.Entering();
            _executionContext = executionContext;
            _connection = connection;
            Trace.Leaving();
        }

        public bool GetFeatureFlagState(string featureFlagName, Guid serviceInstanceId)
        {
            try
            {
                FeatureAvailabilityHttpClient featureAvailabilityHttpClient = _connection.GetClient<FeatureAvailabilityHttpClient>(serviceInstanceId);
                var featureFlag = featureAvailabilityHttpClient?.GetFeatureFlagByNameAsync(featureFlagName).Result;
                if (featureFlag != null && featureFlag.EffectiveState.Equals("On", StringComparison.OrdinalIgnoreCase))
                {
                    _executionContext.Debug(StringUtil.Format("{0} is on", featureFlagName));
                    return true;
                }
                _executionContext.Debug(StringUtil.Format("{0} is off", featureFlagName));
            }
            catch(Exception ex)
            {
                var exceptionMessage = StringUtil.Format("Failed to get FF {0} Value. Error: {1}", featureFlagName, ex.ToString());
                _executionContext.Warning(exceptionMessage);
                Trace.Error(exceptionMessage);
            }
            return false;
        }
    }
}