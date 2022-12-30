using System;
using Microsoft.VisualStudio.Services.Agent.Worker;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Xunit;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Agent.Plugins.PipelineArtifact;
using Agent.Plugins.PipelineCache;
using System.Collections.Generic;
using Moq;
using Agent.Sdk;
using System.Linq;


namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker
{
    public sealed class AgentPluginManagerL0
    {
        private class AgentPluginTaskTest
        {
            public string Name;
            public Guid TaskGuid;
            public List<string> ExpectedTaskPlugins;

            public void RunTest(AgentPluginManager manager)
            {
                var taskPlugins = manager.GetTaskPlugins(TaskGuid);
                if (ExpectedTaskPlugins == null)
                {
                    Assert.True(taskPlugins == null, $"{Name} returns null task plugins");
                }
                else
                {
                    Assert.True(taskPlugins.Count == ExpectedTaskPlugins.Count, $"{Name} has {ExpectedTaskPlugins.Count} Task Plugin(s)");
                    foreach (var s in ExpectedTaskPlugins)
                    {
                        Assert.True(taskPlugins.Contains(s), $"{Name} contains '{s}'");
                    }
                }
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void GetTaskPluginsTests()
        {
            using (TestHostContext tc = CreateTestContext())
            {
                Tracing trace = tc.GetTrace();
                var agentPluginManager = new AgentPluginManager();
                agentPluginManager.Initialize(tc);

                List<AgentPluginTaskTest> tests = new List<AgentPluginTaskTest>
                {
                    new AgentPluginTaskTest()
                    {
                        Name = "Checkout Task",
                        TaskGuid = Pipelines.PipelineConstants.CheckoutTask.Id,
                        ExpectedTaskPlugins = new List<string>
                        {
                            "Agent.Plugins.Repository.CheckoutTask, Agent.Plugins",
                            "Agent.Plugins.Repository.CleanupTask, Agent.Plugins",
                        }
                    },
                    new AgentPluginTaskTest()
                    {
                        Name = "Download Pipline Artifact Task",
                        TaskGuid = PipelineArtifactPluginConstants.DownloadPipelineArtifactTaskId,
                        ExpectedTaskPlugins = new List<string>
                        {
                            "Agent.Plugins.PipelineArtifact.DownloadPipelineArtifactTask, Agent.Plugins",
                            "Agent.Plugins.PipelineArtifact.DownloadPipelineArtifactTaskV1, Agent.Plugins",
                            "Agent.Plugins.PipelineArtifact.DownloadPipelineArtifactTaskV1_1_0, Agent.Plugins",
                            "Agent.Plugins.PipelineArtifact.DownloadPipelineArtifactTaskV1_1_1, Agent.Plugins",
                            "Agent.Plugins.PipelineArtifact.DownloadPipelineArtifactTaskV1_1_2, Agent.Plugins",
                            "Agent.Plugins.PipelineArtifact.DownloadPipelineArtifactTaskV1_1_3, Agent.Plugins",
                            "Agent.Plugins.PipelineArtifact.DownloadPipelineArtifactTaskV2_0_0, Agent.Plugins",
                        }
                    },
                    new AgentPluginTaskTest()
                    {
                        Name = "Publish Pipeline Artifact Task",
                        TaskGuid = PipelineArtifactPluginConstants.PublishPipelineArtifactTaskId,
                        ExpectedTaskPlugins = new List<string>
                        {
                            "Agent.Plugins.PipelineArtifact.PublishPipelineArtifactTask, Agent.Plugins",
                            "Agent.Plugins.PipelineArtifact.PublishPipelineArtifactTaskV1, Agent.Plugins",
                            "Agent.Plugins.PipelineArtifact.PublishPipelineArtifactTaskV0_140_0, Agent.Plugins"
                        }
                    },
                    new AgentPluginTaskTest()
                    {
                        Name = "Pipeline Cache Task",
                        TaskGuid = PipelineCachePluginConstants.CacheTaskId,
                        ExpectedTaskPlugins = new List<string>
                        {
                            "Agent.Plugins.PipelineCache.SavePipelineCacheV0, Agent.Plugins",
                            "Agent.Plugins.PipelineCache.RestorePipelineCacheV0, Agent.Plugins",
                        }
                    },
                    new AgentPluginTaskTest()
                    {
                        Name = "Empty Guid Tasks",
                        TaskGuid = Guid.Empty,
                        ExpectedTaskPlugins = null
                    },
                };

                foreach (var test in tests)
                {
                    test.RunTest(agentPluginManager);
                }
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public Task RunPluginTaskAsyncThrowsNotsupported()
        {
            using (TestHostContext tc = CreateTestContext())
            {
                Tracing trace = tc.GetTrace();
                var agentPluginManager = new AgentPluginManager();
                agentPluginManager.Initialize(tc);
                var executionContext = CreateTestExecutionContext(tc);
                return Assert.ThrowsAsync<NotSupportedException>(() =>
                    agentPluginManager.RunPluginTaskAsync(executionContext, "invalid.plugin", new Dictionary<string, string>(), new Dictionary<string, string>(), null, null)
                );
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void GeneratePluginExecutionContextHostInfoTest()
        {

            using (TestHostContext tc = CreateTestContext())
            {
                Tracing trace = tc.GetTrace();
                var agentPluginManager = new AgentPluginManager();
                agentPluginManager.Initialize(tc);

                var inputs = new Dictionary<string, string>(){
                    { "input1", "foo" },
                    { "input2", tc.GetDirectory(WellKnownDirectory.Work)},
                };

                var variables = new Dictionary<string, VariableValue>(){
                    { "variable1", "foo" },
                    { "variable2", tc.GetDirectory(WellKnownDirectory.Work)},
                };

                var taskVariables = new Dictionary<string, VariableValue>(){
                    { "taskVariable1", "foo" },
                    { "taskVariable2", tc.GetDirectory(WellKnownDirectory.Work)},
                };
                var executionContext = CreateTestExecutionContext(tc, variables: variables, taskVariables: taskVariables);

                var pluginContext = agentPluginManager.GeneratePluginExecutionContext(executionContext, inputs, executionContext.Variables);
                Assert.True(pluginContext != null, "PluginContext for Host Step Target is not null");
                // inputs should match exactly for Host Step Targets
                Assert.True(inputs.All(e => pluginContext.Inputs.Contains(e)));
                // variables should match exactly for Host Step Targets
                Assert.True(variables.All(e => pluginContext.Variables.Contains(e)));
                // task variables should match exactly for Host Step Targets
                Assert.True(taskVariables.All(e => pluginContext.TaskVariables.Contains(e)));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void GeneratePluginExecutionContextContainerInfoTest()
        {
            var dockerContainer = new Pipelines.ContainerResource()
            {
                Alias = "vsts_container_preview",
                Image = "foo"
            };

            using (TestHostContext tc = CreateTestContext())
            {
                Tracing trace = tc.GetTrace();
                var agentPluginManager = new AgentPluginManager();
                agentPluginManager.Initialize(tc);
                var containerInfo = tc.CreateContainerInfo(dockerContainer, isJobContainer: false);
                var containerWorkPath = "/__w";
                if (TestUtil.IsWindows())
                {
                    containerWorkPath = "C:\\__w";
                }
                var inputs = new Dictionary<string, string>(){
                    { "input1", "foo" },
                    { "input2", containerWorkPath},
                    { "input3", tc.GetDirectory(WellKnownDirectory.Work)},
                };

                var expectedInputs = new Dictionary<string, string>(){
                    { "input1", "foo" },
                    { "input2", tc.GetDirectory(WellKnownDirectory.Work)},
                    { "input3", tc.GetDirectory(WellKnownDirectory.Work)},
                };

                var variables = new Dictionary<string, VariableValue>(){
                    { "variable1", "foo" },
                    { "variable2", containerWorkPath},
                    { "variable3", tc.GetDirectory(WellKnownDirectory.Work)},
                };

                var expectedVariables = new Dictionary<string, VariableValue>(){
                    { "variable1", "foo" },
                    { "variable2", tc.GetDirectory(WellKnownDirectory.Work)},
                    { "variable3", tc.GetDirectory(WellKnownDirectory.Work)},
                };

                var taskVariables = new Dictionary<string, VariableValue>(){
                    { "taskVariable1", "foo" },
                    { "taskVariable2", containerWorkPath},
                    { "taskVariable3", tc.GetDirectory(WellKnownDirectory.Work)},
                };

                var expectedTaskVariables = new Dictionary<string, VariableValue>(){
                    { "taskVariable1", "foo" },
                    { "taskVariable2", tc.GetDirectory(WellKnownDirectory.Work)},
                    { "taskVariable3", tc.GetDirectory(WellKnownDirectory.Work)},
                };

                var executionContext = CreateTestExecutionContext(tc, stepTarget: containerInfo, variables: variables, taskVariables: taskVariables);

                var pluginContext = agentPluginManager.GeneratePluginExecutionContext(executionContext, inputs, executionContext.Variables);
                Assert.True(pluginContext != null, "PluginContext for Container Step Target is not null");
                Assert.True(expectedInputs.All(e => pluginContext.Inputs.Contains(e)));
                Assert.True(expectedVariables.All(e => pluginContext.Variables.Contains(e)));
                Assert.True(expectedTaskVariables.All(e => pluginContext.TaskVariables.Contains(e)));
            }
        }

        private TestHostContext CreateTestContext([CallerMemberName] String testName = "")
        {
            TestHostContext tc = new TestHostContext(this, testName);
            return tc;
        }

        private IExecutionContext CreateTestExecutionContext(TestHostContext tc,
            ExecutionTargetInfo stepTarget = null,
            Dictionary<string, VariableValue> variables = null,
            Dictionary<string, VariableValue> taskVariables = null)
        {
            var trace = tc.GetTrace();
            var executionContext = new Mock<IExecutionContext>();
            List<string> warnings;
            variables = variables ?? new Dictionary<string, VariableValue>();
            taskVariables = taskVariables ?? new Dictionary<string, VariableValue>();

            executionContext
                .Setup(x => x.Variables)
                .Returns(new Variables(tc, copy: variables, warnings: out warnings));
            executionContext
                .Setup(x => x.TaskVariables)
                .Returns(new Variables(tc, copy: taskVariables, warnings: out warnings));
            if (stepTarget == null)
            {
                executionContext
                    .Setup(x => x.StepTarget())
                    .Returns(new HostInfo());
            }
            else
            {
                executionContext
                    .Setup(x => x.StepTarget())
                    .Returns(stepTarget);
            }

            return executionContext.Object;
        }
    }
}
