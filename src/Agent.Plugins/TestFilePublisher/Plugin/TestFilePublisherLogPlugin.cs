using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Agent.Plugins.Log.TestResultParser.Contracts;
using Agent.Plugins.TestFilePublisher;
using Agent.Sdk;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;

namespace Agent.Plugins.Log.TestFilePublisher.Plugin
{
    public class TestFilePublisherLogPlugin : IAgentLogPlugin
    {
        /// <inheritdoc />
        public string FriendlyName => "TestFilePublisher";

        public TestFilePublisherLogPlugin()
        {
            // Default constructor
        }

        /// <summary>
        /// For UTs only
        /// </summary>
        public TestFilePublisherLogPlugin(ITraceLogger logger, ITelemetryDataCollector telemetry, ITestFilePublisher testFilePublisher)
        {
            _logger = logger;
            _telemetry = telemetry;
            _testFilePublisher = testFilePublisher;
        }

        /// <inheritdoc />
        public async Task<bool> InitializeAsync(IAgentLogPluginContext context)
        {
            try
            {
                _logger = _logger ?? new TraceLogger(context);
                _telemetry = _telemetry ?? new TelemetryDataCollector(new ClientFactory(context.VssConnection), _logger);

                await PopulatePipelineConfig(context);

                _telemetry.AddOrUpdate(TelemetryConstants.PluginInitialized, true);
                _telemetry.AddOrUpdate(TelemetryConstants.PluginDisabled, true);

                if (DisablePlugin(context))
                {
                    return false; // disable the plugin
                }

                _testFilePublisher = _testFilePublisher ??
                                     new Plugins.TestFilePublisher.TestFilePublisher(context.VssConnection, _pipelineConfig, new TestFileTraceListener(context));
            }
            catch (Exception ex)
            {
                context.Trace(ex.ToString());
                _logger?.Warning($"Unable to initialize {FriendlyName}.");
                _telemetry?.AddOrUpdate(TelemetryConstants.InitializeFailed, ex);
                return false;
            }
            finally
            {
                if (_telemetry != null)
                {
                    await _telemetry.PublishCumulativeTelemetryAsync();
                }
            }

            return true;
        }

        /// <inheritdoc />
        public async Task ProcessLineAsync(IAgentLogPluginContext context, Pipelines.TaskStepDefinitionReference step, string line)
        {
            await Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task FinalizeAsync(IAgentLogPluginContext context)
        {
            using (var timer = new SimpleTimer("Finalize", _logger, TimeSpan.FromMilliseconds(Int32.MaxValue),
                new TelemetryDataWrapper(_telemetry, TelemetryConstants.FinalizeAsync)))
            {
                await _testFilePublisher.InitializeAsync();
                await _testFilePublisher.PublishAsync();
            }

            await _telemetry.PublishCumulativeTelemetryAsync();
        }

        /// <summary>
        /// Return true if plugin needs to be disabled
        /// </summary>
        private bool DisablePlugin(IAgentLogPluginContext context)
        {
            // do we want to log that the plugin is disabled due to x reason here?
            if (context.Variables.TryGetValue("Agent.ForceEnable.TestFilePublisherLogPlugin", out var forceEnable)
                && string.Equals("true", forceEnable.Value, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Enable only for build
            if (!context.Variables.TryGetValue("system.hosttype", out var hostType)
                || !string.Equals("Build", hostType.Value, StringComparison.OrdinalIgnoreCase))
            {
                _telemetry.AddOrUpdate("PluginDisabledReason", "NotABuild");
                return true;
            }

            // Disable for on-prem
            if (!context.Variables.TryGetValue("system.servertype", out var serverType)
                || !string.Equals("Hosted", serverType.Value, StringComparison.OrdinalIgnoreCase))
            {
                _telemetry.AddOrUpdate("PluginDisabledReason", "NotHosted");
                return true;
            }

            // check for PTR task or some other tasks to enable/disable
            if (context.Steps == null)
            {
                _telemetry.AddOrUpdate("PluginDisabledReason", "NoSteps");
                return true;
            }

            if (context.Steps.Any(x => x.Id.Equals(new Guid("0B0F01ED-7DDE-43FF-9CBB-E48954DAF9B1"))))
            {
                _telemetry.AddOrUpdate("PluginDisabledReason", "ExplicitPublishTaskPresent");
                return true;
            }

            if (_pipelineConfig.BuildId == 0)
            {
                _telemetry.AddOrUpdate("PluginDisabledReason", "BuildIdZero");
                return true;
            }

            if (_pipelineConfig.Pattern == null)
            {
                _telemetry.AddOrUpdate("PluginDisabledReason", "PatternIsEmpty");
                return true;
            }

            if (_pipelineConfig.SearchFolders == null)
            {
                _telemetry.AddOrUpdate("PluginDisabledReason", "SearchFolderIsEmpty");
                return true;
            }

            return false;
        }

        private async Task PopulatePipelineConfig(IAgentLogPluginContext context)
        {
            var props = new Dictionary<string, Object>();

            if (context.Variables.TryGetValue("system.teamProject", out var projectName))
            {
                _pipelineConfig.ProjectName = projectName.Value;
                _telemetry.AddOrUpdate("ProjectName", _pipelineConfig.ProjectName);
                props.Add("ProjectName", _pipelineConfig.ProjectName);
            }

            if (context.Variables.TryGetValue("build.buildId", out var buildId))
            {
                _pipelineConfig.BuildId = int.Parse(buildId.Value);
                _telemetry.AddOrUpdate("BuildId", _pipelineConfig.BuildId);
                props.Add("BuildId", _pipelineConfig.BuildId);
            }

            if (context.Variables.TryGetValue("System.DefinitionId", out var buildDefinitionId))
            {
                _telemetry.AddOrUpdate("BuildDefinitionId", buildDefinitionId.Value);
            }

            if (context.Variables.TryGetValue("agent.testfilepublisher.pattern", out var pattern)
                && !string.IsNullOrWhiteSpace(pattern.Value))
            {
                _pipelineConfig.Pattern = pattern.Value;
            }

            if (context.Variables.TryGetValue("agent.testfilepublisher.searchfolders", out var searchFolders)
                && !string.IsNullOrWhiteSpace(searchFolders.Value))
            {
                PopulateSearchFolders(context, searchFolders.Value);
            }

            // Publish the initial telemetry event in case we are not able to fire the cumulative one for whatever reason
            await _telemetry.PublishTelemetryAsync("TestFilePublisherInitialize", props);
        }

        private void PopulateSearchFolders(IAgentLogPluginContext context, string searchFolders)
        {
            var folderVariables = searchFolders.Split(",");
            foreach (var folderVar in folderVariables)
            {
                if (context.Variables.TryGetValue(folderVar, out var folderValue))
                {
                    _pipelineConfig.SearchFolders.Add(folderValue.Value);
                }
            }
        }

        private ITraceLogger _logger;
        private ITelemetryDataCollector _telemetry;
        private ITestFilePublisher _testFilePublisher;
        private readonly PipelineConfig _pipelineConfig = new PipelineConfig();
    }
}
