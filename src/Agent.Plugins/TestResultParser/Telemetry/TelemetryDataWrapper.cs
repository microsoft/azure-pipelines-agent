using Agent.Plugins.Log.TestResultParser.Contracts;

namespace Agent.Plugins.Log.TestResultParser.Plugin
{
    public class TelemetryDataWrapper
    {
        public TelemetryDataWrapper(ITelemetryDataCollector telemetry, string telemetryEventName, string telemetrySubArea = null)
        {
            TelemetryDataCollector = telemetry;
            TelemetryEventName = telemetryEventName;
            TelemetrySubArea = telemetrySubArea;
        }

        public void AddAndAggregate(object value)
        {

        }

        public string TelemetrySubArea { get; }

        public string TelemetryEventName { get; }

        public ITelemetryDataCollector TelemetryDataCollector { get; }
    }
}
