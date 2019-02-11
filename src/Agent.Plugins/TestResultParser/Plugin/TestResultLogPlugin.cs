using System;
using System.Linq;
using System.Threading.Tasks;
using Agent.Plugins.Log.TestResultParser.Contracts;
using Agent.Sdk;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;

namespace Agent.Plugins.Log.TestResultParser.Plugin
{
    public class TestResultLogPlugin : IAgentLogPlugin
    {
        /// <inheritdoc />
        public string FriendlyName => "TestResultLogParser";

        /// <inheritdoc />
        public async Task<bool> InitializeAsync(IAgentLogPluginContext context)
        {
            try
            {
                context.Variables.TryGetValue("system.debug", out var systemDebug);
                var debugLoggingEnabled = false;

                if (string.Equals(systemDebug.Value, "true"))
                {
                    debugLoggingEnabled = true;
                }

                _logger = new TraceLogger(context, debugLoggingEnabled);
                _clientFactory = new ClientFactory(context.VssConnection);
                _telemetry = new TelemetryDataCollector(_clientFactory, _logger);

                _telemetry.AddToCumulativeTelemetry(null, TelemetryConstants.PluginInitialized, true);
                _telemetry.AddToCumulativeTelemetry(null, TelemetryConstants.PluginDisabled, true);

                PopulatePipelineConfig(context);

                if (DisablePlugin(context))
                {
                    return false; // disable the plugin
                }

                _telemetry.AddToCumulativeTelemetry(null, TelemetryConstants.PluginDisabled, false);

                await InputDataParser.InitializeAsync(_clientFactory, _pipelineConfig, _logger, _telemetry);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Unable to initialize {FriendlyName}");
                context.Trace(ex.ToString());
                return false;
            }

            return true;
        }

        /// <inheritdoc />
        public async Task ProcessLineAsync(IAgentLogPluginContext context, Pipelines.TaskStepDefinitionReference step, string line)
        {
            await InputDataParser.ProcessDataAsync(line);
        }

        /// <inheritdoc />
        public async Task FinalizeAsync(IAgentLogPluginContext context)
        {
            using (var timer = new SimpleTimer("Finalize", null, TelemetryConstants.FinalizeAsync, _logger,_telemetry,
                TimeSpan.FromMilliseconds(Int32.MaxValue)))
            {
                await InputDataParser.CompleteAsync();
            }

            await _telemetry.PublishCumulativeTelemetryAsync();
        }

        /// <summary>
        /// Return true if plugin needs to be disabled
        /// </summary>
        private bool DisablePlugin(IAgentLogPluginContext context)
        {
            // do we want to log that the plugin is disabled due to x reason here?
            if (context.Variables.TryGetValue("ForceEnableTestResultParsers", out var forceEnableTestResultParsers))
            {
                return false;
            }

            // _telemetry.AddToCumulativeTelemetry(null, "PluginDisabledReason", "xyz");

            // Enable only for build
            if (context.Variables.TryGetValue("system.hosttype", out var hostType)
                && !string.Equals("Build", hostType.Value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Disable for on-prem
            if (context.Variables.TryGetValue("system.servertype", out var serverType)
                && !string.Equals("Hosted", serverType.Value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // check for PTR task or some other tasks to enable/disable
            return context.Steps == null
                   || context.Steps.Any(x => x.Id.Equals(new Guid("0B0F01ED-7DDE-43FF-9CBB-E48954DAF9B1")))
                   || _pipelineConfig.BuildId == 0;
        }

        private void PopulatePipelineConfig(IAgentLogPluginContext context)
        {
            if (context.Variables.TryGetValue("system.teamProjectId", out var projectGuid))
            {
                _pipelineConfig.Project = new Guid(projectGuid.Value);
                _telemetry.AddToCumulativeTelemetry(null, "ProjectId", _pipelineConfig.Project);
            }

            if (context.Variables.TryGetValue("build.buildId", out var buildId))
            {
                _pipelineConfig.BuildId = int.Parse(buildId.Value);
                _telemetry.AddToCumulativeTelemetry(null, "BuildId", _pipelineConfig.BuildId);
            }
        }

        public ILogParserGateway InputDataParser { get; set; } = new LogParserGateway(); // for testing purpose
        private IClientFactory _clientFactory;
        private ITraceLogger _logger;
        private ITelemetryDataCollector _telemetry;
        private readonly IPipelineConfig _pipelineConfig = new PipelineConfig();
    }
}
