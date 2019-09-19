using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.FeatureAvailability.WebApi;
using Microsoft.VisualStudio.Services.WebApi;
using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.VisualStudio.Services.Agent.Worker.TestResults
{
    [ServiceLocator(Default = typeof(FeatureFlagService))]
    public interface IFeatureFlagService : IAgentService
    {
        void InitializeFeatureService(IExecutionContext executionContext, VssConnection connection);

        bool GetFeatureFlagState(string FFName, Service service);
    }

    public class FeatureFlagService :  AgentService, IFeatureFlagService
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

        public bool GetFeatureFlagState(string FFName, Service service)
        {
            try
            {
                FeatureAvailabilityHttpClient featureAvailabilityHttpClient = GetFeatureAvailabilityHttpClient(service);
                var featureFlag = featureAvailabilityHttpClient?.GetFeatureFlagByNameAsync(FFName).Result;
                if (featureFlag != null && featureFlag.EffectiveState.Equals("On", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch
            {
                _executionContext.Debug(StringUtil.Format("Failed to get FF {0} Value. By default, publishing data to TCM.", FFName));
                return false;
            }
            return false;
        }

        private FeatureAvailabilityHttpClient GetFeatureAvailabilityHttpClient(Service service){
            FeatureAvailabilityHttpClient featureAvailabilityHttpClient;
            switch(service){
                case Service.TCM: 
                    featureAvailabilityHttpClient =  _connection.GetClient<FeatureAvailabilityHttpClient>(TestResultsConstants.TCMServiceInstanceGuid);
                    break;
                default:
                    featureAvailabilityHttpClient =  _connection.GetClient<FeatureAvailabilityHttpClient>(TestResultsConstants.TFSServiceInstanceGuid);
                    break;          
            }
            return featureAvailabilityHttpClient;
        }
    }

    public enum Service{
        TFS,
        TCM
    }
}