using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.Handlers;
using Moq;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    public sealed class NodeHandlerL0
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void UseNodeForNodeHandlerEnvVarNotSet()
        {
            using (TestHostContext thc = CreateTestHostContext())
            {
                Mock<IExecutionContext> tec = CreateTestExecutionContext(thc);
                NodeHandler nodeHandler = new NodeHandler();
                nodeHandler.Data = new NodeHandlerData();

                string nodeFolder;
                bool taskHasNode10Data, useNode10;
                
                (nodeFolder, taskHasNode10Data, useNode10) = nodeHandler.GetNodeFolder(tec.Object);

                Assert.Equal("node", nodeFolder);
                Assert.False(taskHasNode10Data);
                Assert.False(useNode10);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void UseNode10ForNode10Handler()
        {
            using (TestHostContext thc = CreateTestHostContext())
            {
                Mock<IExecutionContext> tec = CreateTestExecutionContext(thc);
                NodeHandler nodeHandler = new NodeHandler();
                nodeHandler.Data = new Node10HandlerData();

                string nodeFolder;
                bool taskHasNode10Data, useNode10;
                
                (nodeFolder, taskHasNode10Data, useNode10) = nodeHandler.GetNodeFolder(tec.Object);

                Assert.Equal("node10", nodeFolder);
                Assert.True(taskHasNode10Data);
                Assert.False(useNode10);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void UseNode10ForNodeHandlerEnvVarSet()
        {
            try
            {
                Environment.SetEnvironmentVariable("AGENT_USE_NODE10", "true");

                using (TestHostContext thc = CreateTestHostContext())
                {
                    Mock<IExecutionContext> tec = CreateTestExecutionContext(thc);
                    NodeHandler nodeHandler = new NodeHandler();
                    nodeHandler.Data = new NodeHandlerData();

                    string nodeFolder;
                    bool taskHasNode10Data, useNode10;
                    
                    (nodeFolder, taskHasNode10Data, useNode10) = nodeHandler.GetNodeFolder(tec.Object);

                    Assert.Equal("node10", nodeFolder);
                    Assert.False(taskHasNode10Data);
                    Assert.True(useNode10);
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("AGENT_USE_NODE10", null);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void UseNode10ForNodeHandlerHostContextVarSet()
        {
            using (TestHostContext thc = CreateTestHostContext())
            {
                var variables = new Dictionary<string, VariableValue>();

                variables.Add("AGENT_USE_NODE10", new VariableValue("true"));

                Mock<IExecutionContext> tec = CreateTestExecutionContext(thc, variables);
                NodeHandler nodeHandler = new NodeHandler();
                nodeHandler.Data = new NodeHandlerData();

                string nodeFolder;
                bool taskHasNode10Data, useNode10;
                
                (nodeFolder, taskHasNode10Data, useNode10) = nodeHandler.GetNodeFolder(tec.Object);

                Assert.Equal("node10", nodeFolder);
                Assert.False(taskHasNode10Data);
                Assert.True(useNode10);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void UseNode10ForNode10HandlerHostContextVarUnset()
        {
            using (TestHostContext thc = CreateTestHostContext())
            {
                var variables = new Dictionary<string, VariableValue>();

                // Explicitly set 'AGENT_USE_NODE10' feature flag to false
                variables.Add("AGENT_USE_NODE10", new VariableValue("false"));

                Mock<IExecutionContext> tec = CreateTestExecutionContext(thc, variables);
                NodeHandler nodeHandler = new NodeHandler();
                nodeHandler.Data = new Node10HandlerData();

                string nodeFolder;
                bool taskHasNode10Data, useNode10;
                
                (nodeFolder, taskHasNode10Data, useNode10) = nodeHandler.GetNodeFolder(tec.Object);

                // Node10 handler is unaffected by the 'AGENT_USE_NODE10' feature flag, so folder name should be 'node10'
                Assert.Equal("node10", nodeFolder);
                Assert.True(taskHasNode10Data);
                Assert.False(useNode10);
            }
        }

        private TestHostContext CreateTestHostContext([CallerMemberName] string testName = "")
        {
            return new TestHostContext(this, testName);
        }

        private Mock<IExecutionContext> CreateTestExecutionContext(TestHostContext tc,
            Dictionary<string, VariableValue> variables = null)
        {
            var trace = tc.GetTrace();
            var executionContext = new Mock<IExecutionContext>();
            List<string> warnings;
            variables = variables ?? new Dictionary<string, VariableValue>();

            executionContext
                .Setup(x => x.Variables)
                .Returns(new Variables(tc, copy: variables, warnings: out warnings));

            return executionContext;
        }

    }
}