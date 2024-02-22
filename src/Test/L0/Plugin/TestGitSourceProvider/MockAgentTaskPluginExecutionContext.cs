using Agent.Sdk;

namespace Test.L0.Plugin.TestGitSourceProvider
{
    public class MockAgentTaskPluginExecutionContext : AgentTaskPluginExecutionContext
    {
        public MockAgentTaskPluginExecutionContext(ITraceWriter trace) : base(trace) { }
    }
}
