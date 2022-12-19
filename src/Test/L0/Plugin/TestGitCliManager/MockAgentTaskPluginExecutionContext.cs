using Agent.Sdk;
using System;
using System.Collections.Generic;
using System.Text;

namespace Test.L0.Plugin.TestGitCliManager
{
    public class MockAgentTaskPluginExecutionContext : AgentTaskPluginExecutionContext
    {
        public MockAgentTaskPluginExecutionContext(ITraceWriter trace) : base(trace) { }

        public override void PrependPath(string directory) { }
    }
}
