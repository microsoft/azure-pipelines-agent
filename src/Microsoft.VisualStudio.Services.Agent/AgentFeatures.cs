using System.Collections.Generic;

namespace Microsoft.VisualStudio.Services.Agent
{
    public interface IFeatureArea : IExtension
    {
        string FeatureAreaName { get; }
        string[] Features { get; }
    }

    [ServiceLocator(Default = typeof(AgentFeatureScanner))]
    public interface IAgentFeatureScanner : IAgentService
    {
        string[] GetAgentFeatures();
    }

    public class AgentFeatureScanner : AgentService, IAgentFeatureScanner
    {
        private readonly List<string> _features = new List<string>();
        private bool _featureScanned = false;

        public string[] GetAgentFeatures()
        {
            if (_featureScanned)
            {
                return _features.ToArray();
            }

            var extensionManager = HostContext.GetService<IExtensionManager>();
            var featureAreas = extensionManager.GetExtensions<IFeatureArea>();
            foreach (var area in featureAreas)
            {
                Trace.Info($"Register features from feature area: {area.FeatureAreaName}");
                if (area.Features != null && area.Features.Length > 0)
                {
                    Trace.Info($"Feature area '{area.FeatureAreaName}' has feature: {string.Join(",", area.Features)}.");
                    _features.AddRange(area.Features);
                }
            }

            _featureScanned = true;
            return _features.ToArray();
        }
    }
}