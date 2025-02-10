using Agent.Plugins.Repository;
using Agent.Sdk;
using Moq;
using System.Collections.Generic;

namespace Test.L0.Plugin.TestGitSourceProvider
{
    public class MockAgentTaskPluginExecutionContext : AgentTaskPluginExecutionContext
    {
        public MockAgentTaskPluginExecutionContext(ITraceWriter trace) : base(trace) { }

        public override void PrependPath(string directory) { }
    }
}
