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

        public TestResultLogPlugin()
        {

        }

        /// <summary>
        /// For UTs only
        /// </summary>
        public TestResultLogPlugin(ILogParserGateway inputDataParser, ITraceLogger logger, ITelemetryDataCollector telemetry)
        {
            _logger = logger;
            _telemetry = telemetry;
            _inputDataParser = inputDataParser;
        }

        /// <inheritdoc />
        public async Task<bool> InitializeAsync(IAgentLogPluginContext context)
        {
            try
            {
                context.Variables.TryGetValue("system.debug", out var systemDebug);
                var debugLoggingEnabled = false;

                if (string.Equals(systemDebug?.Value, "true"))
                {
                    debugLoggingEnabled = true;
                }

                if (_logger == null)
                {
                    _logger = new TraceLogger(context, debugLoggingEnabled);
                }

                _clientFactory = new ClientFactory(context.VssConnection);

                if (_telemetry == null)
                {
                    _telemetry = new TelemetryDataCollector(_clientFactory, _logger);
                }

                PopulatePipelineConfig(context);

                _telemetry.AddToCumulativeTelemetry(null, TelemetryConstants.PluginInitialized, true);
                _telemetry.AddToCumulativeTelemetry(null, TelemetryConstants.PluginDisabled, true);

                if (DisablePlugin(context))
                {
                    return false; // disable the plugin
                }

                _telemetry.AddToCumulativeTelemetry(null, TelemetryConstants.PluginDisabled, false);

                await _inputDataParser.InitializeAsync(_clientFactory, _pipelineConfig, _logger, _telemetry);
            }
            catch (Exception ex)
            {
                context.Trace(ex.ToString());
                _logger?.Warning($"Unable to initialize {FriendlyName}.");
                _telemetry?.AddToCumulativeTelemetry(null, TelemetryConstants.InitialzieFailed, ex);
                await _telemetry?.PublishCumulativeTelemetryAsync();
                return false;
            }

            return true;
        }

        /// <inheritdoc />
        public async Task ProcessLineAsync(IAgentLogPluginContext context, Pipelines.TaskStepDefinitionReference step, string line)
        {
            await _inputDataParser.ProcessDataAsync(line);
        }

        /// <inheritdoc />
        public async Task FinalizeAsync(IAgentLogPluginContext context)
        {
            using (var timer = new SimpleTimer("Finalize", null, TelemetryConstants.FinalizeAsync, _logger,_telemetry,
                TimeSpan.FromMilliseconds(Int32.MaxValue)))
            {
                await _inputDataParser.CompleteAsync();
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

        private ILogParserGateway _inputDataParser = new LogParserGateway();
        private IClientFactory _clientFactory;
        private ITraceLogger _logger;
        private ITelemetryDataCollector _telemetry;
        private readonly IPipelineConfig _pipelineConfig = new PipelineConfig();
    }
}
