using System;
using Microsoft.VisualStudio.Services.Agent.Worker;
using System.Runtime.CompilerServices;
using Xunit;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;
using Agent.Plugins.PipelineArtifact;
using Agent.Plugins.PipelineCache;
using System.Collections.Generic;


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
                Assert.True(taskPlugins.Count == ExpectedTaskPlugins.Count, $"{Name} has {ExpectedTaskPlugins.Count} Task Plugin(s)");
                foreach (var s in ExpectedTaskPlugins)
                {
                    Assert.True(taskPlugins.Contains(s), $"{Name} contains '{s}'");
                }
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void SimpleTests()
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
                };

                foreach (var test in tests)
                {
                    test.RunTest(agentPluginManager);
                }
            }
        }

        private TestHostContext CreateTestContext([CallerMemberName] String testName = "")
        {
            TestHostContext tc = new TestHostContext(this, testName);
            return tc;
        }
    }
}
