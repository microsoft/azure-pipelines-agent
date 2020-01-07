// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Xunit;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;
using Microsoft.VisualStudio.Services.Agent.Worker;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker
{
    public sealed class ExecutionContextL0
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void InitializeJob_LogsWarningsFromVariables()
        {
            using (TestHostContext hc = CreateTestContext())
            {
                // Arrange: Create a job request message.
                TaskOrchestrationPlanReference plan = new TaskOrchestrationPlanReference();
                TimelineReference timeline = new TimelineReference();
                JobEnvironment environment = new JobEnvironment();
                environment.SystemConnection = new ServiceEndpoint();
                environment.Variables["v1"] = "v1-$(v2)";
                environment.Variables["v2"] = "v2-$(v1)";
                List<TaskInstance> tasks = new List<TaskInstance>();
                Guid JobId = Guid.NewGuid();
                string jobName = "some job name";
                var jobRequest = Pipelines.AgentJobRequestMessageUtil.Convert(new AgentJobRequestMessage(plan, timeline, JobId, jobName, jobName, environment, tasks));

                // Arrange: Setup the paging logger.
                var pagingLogger = new Mock<IPagingLogger>();
                hc.EnqueueInstance(pagingLogger.Object);

                var ec = new Agent.Worker.ExecutionContext();
                ec.Initialize(hc);

                // Act.
                ec.InitializeJob(jobRequest, CancellationToken.None);

                // Assert.
                pagingLogger.Verify(x => x.Write(It.Is<string>(y => y.IndexOf("##[warning]") >= 0)), Times.Exactly(2));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void AddIssue_CountWarningsErrors()
        {
            using (TestHostContext hc = CreateTestContext())
            {
                // Arrange: Create a job request message.
                TaskOrchestrationPlanReference plan = new TaskOrchestrationPlanReference();
                TimelineReference timeline = new TimelineReference();
                JobEnvironment environment = new JobEnvironment();
                environment.SystemConnection = new ServiceEndpoint();
                List<TaskInstance> tasks = new List<TaskInstance>();
                Guid JobId = Guid.NewGuid();
                string jobName = "some job name";
                var jobRequest = Pipelines.AgentJobRequestMessageUtil.Convert(new AgentJobRequestMessage(plan, timeline, JobId, jobName, jobName, environment, tasks));

                // Arrange: Setup the paging logger.
                var pagingLogger = new Mock<IPagingLogger>();
                var jobServerQueue = new Mock<IJobServerQueue>();
                jobServerQueue.Setup(x => x.QueueTimelineRecordUpdate(It.IsAny<Guid>(), It.IsAny<TimelineRecord>()));

                hc.EnqueueInstance(pagingLogger.Object);
                hc.SetSingleton(jobServerQueue.Object);

                var ec = new Agent.Worker.ExecutionContext();
                ec.Initialize(hc);

                // Act.
                ec.InitializeJob(jobRequest, CancellationToken.None);

                ec.AddIssue(new Issue() { Type = IssueType.Error, Message = "error" });
                ec.AddIssue(new Issue() { Type = IssueType.Error, Message = "error" });
                ec.AddIssue(new Issue() { Type = IssueType.Error, Message = "error" });
                ec.AddIssue(new Issue() { Type = IssueType.Error, Message = "error" });
                ec.AddIssue(new Issue() { Type = IssueType.Error, Message = "error" });
                ec.AddIssue(new Issue() { Type = IssueType.Error, Message = "error" });
                ec.AddIssue(new Issue() { Type = IssueType.Error, Message = "error" });
                ec.AddIssue(new Issue() { Type = IssueType.Error, Message = "error" });
                ec.AddIssue(new Issue() { Type = IssueType.Error, Message = "error" });
                ec.AddIssue(new Issue() { Type = IssueType.Error, Message = "error" });
                ec.AddIssue(new Issue() { Type = IssueType.Error, Message = "error" });
                ec.AddIssue(new Issue() { Type = IssueType.Error, Message = "error" });
                ec.AddIssue(new Issue() { Type = IssueType.Error, Message = "error" });
                ec.AddIssue(new Issue() { Type = IssueType.Error, Message = "error" });
                ec.AddIssue(new Issue() { Type = IssueType.Error, Message = "error" });

                ec.AddIssue(new Issue() { Type = IssueType.Warning, Message = "warning" });
                ec.AddIssue(new Issue() { Type = IssueType.Warning, Message = "warning" });
                ec.AddIssue(new Issue() { Type = IssueType.Warning, Message = "warning" });
                ec.AddIssue(new Issue() { Type = IssueType.Warning, Message = "warning" });
                ec.AddIssue(new Issue() { Type = IssueType.Warning, Message = "warning" });
                ec.AddIssue(new Issue() { Type = IssueType.Warning, Message = "warning" });
                ec.AddIssue(new Issue() { Type = IssueType.Warning, Message = "warning" });
                ec.AddIssue(new Issue() { Type = IssueType.Warning, Message = "warning" });
                ec.AddIssue(new Issue() { Type = IssueType.Warning, Message = "warning" });
                ec.AddIssue(new Issue() { Type = IssueType.Warning, Message = "warning" });
                ec.AddIssue(new Issue() { Type = IssueType.Warning, Message = "warning" });
                ec.AddIssue(new Issue() { Type = IssueType.Warning, Message = "warning" });
                ec.AddIssue(new Issue() { Type = IssueType.Warning, Message = "warning" });
                ec.AddIssue(new Issue() { Type = IssueType.Warning, Message = "warning" });

                ec.Complete();

                // Assert.
                jobServerQueue.Verify(x => x.QueueTimelineRecordUpdate(It.IsAny<Guid>(), It.Is<TimelineRecord>(t => t.ErrorCount == 15)), Times.AtLeastOnce);
                jobServerQueue.Verify(x => x.QueueTimelineRecordUpdate(It.IsAny<Guid>(), It.Is<TimelineRecord>(t => t.WarningCount == 14)), Times.AtLeastOnce);
                jobServerQueue.Verify(x => x.QueueTimelineRecordUpdate(It.IsAny<Guid>(), It.Is<TimelineRecord>(t => t.Issues.Where(i => i.Type == IssueType.Error).Count() == 10)), Times.AtLeastOnce);
                jobServerQueue.Verify(x => x.QueueTimelineRecordUpdate(It.IsAny<Guid>(), It.Is<TimelineRecord>(t => t.Issues.Where(i => i.Type == IssueType.Warning).Count() == 10)), Times.AtLeastOnce);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void StepTarget_VerifySet()
        {
            using (TestHostContext hc = CreateTestContext())
            {
                var ec = new Agent.Worker.ExecutionContext();
                ec.Initialize(hc);

                var pipeContainer = new Pipelines.ContainerResource {
                    Alias = "container"
                };
                pipeContainer.Properties.Set<string>("image", "someimage");
                // Arrange: Create a job request message.
                TaskOrchestrationPlanReference plan = new TaskOrchestrationPlanReference();
                TimelineReference timeline = new TimelineReference();
                JobEnvironment environment = new JobEnvironment();
                environment.SystemConnection = new ServiceEndpoint();
                List<Pipelines.JobStep> steps = new List<Pipelines.JobStep>();
                steps.Add(new Pipelines.TaskStep
                {
                    Target = new Pipelines.StepTarget
                    {
                        Target = "container"
                    },
                    Reference = new Pipelines.TaskStepDefinitionReference()
                });
                var resources = new Pipelines.JobResources();
                resources.Containers.Add(pipeContainer);
                Guid JobId = Guid.NewGuid();
                string jobName = "some job name";
                var jobRequest = new Pipelines.AgentJobRequestMessage(plan, timeline, JobId, jobName, jobName, null, new Dictionary<string, string>(),
                    new Dictionary<string, VariableValue>(), new List<MaskHint>(), resources, new Pipelines.WorkspaceOptions(), steps);

                // Arrange: Setup command manager
                var commandMock = new Mock<IWorkerCommandManager>();
                hc.SetSingleton(commandMock.Object);
                var pagingLogger = new Mock<IPagingLogger>();
                hc.EnqueueInstance(pagingLogger.Object);

                // Act.
                ec.InitializeJob(jobRequest, CancellationToken.None);
                ec.SetStepTarget(steps[0].Target);

                // Assert.
                Assert.IsType<ContainerInfo>(ec.StepTarget());
                commandMock.Verify(x => x.SetCommandRestrictionPolicy(It.IsAny<UnrestricedWorkerCommandRestrictionPolicy>()));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void StepTarget_RestrictedCommands_Host()
        {
            using (TestHostContext hc = CreateTestContext())
            {
                var ec = new Agent.Worker.ExecutionContext();
                ec.Initialize(hc);

                var pipeContainer = new Pipelines.ContainerResource {
                    Alias = "container"
                };
                pipeContainer.Properties.Set<string>("image", "someimage");
                // Arrange: Create a job request message.
                TaskOrchestrationPlanReference plan = new TaskOrchestrationPlanReference();
                TimelineReference timeline = new TimelineReference();
                JobEnvironment environment = new JobEnvironment();
                environment.SystemConnection = new ServiceEndpoint();
                List<Pipelines.JobStep> steps = new List<Pipelines.JobStep>();
                steps.Add(new Pipelines.TaskStep
                {
                    Target = new Pipelines.StepTarget
                    {
                        Target = "host",
                        Commands = "restricted"
                    },
                    Reference = new Pipelines.TaskStepDefinitionReference()
                });
                var resources = new Pipelines.JobResources();
                resources.Containers.Add(pipeContainer);
                Guid JobId = Guid.NewGuid();
                string jobName = "some job name";
                var jobRequest = new Pipelines.AgentJobRequestMessage(plan, timeline, JobId, jobName, jobName, null, new Dictionary<string, string>(),
                    new Dictionary<string, VariableValue>(), new List<MaskHint>(), resources, new Pipelines.WorkspaceOptions(), steps);

                // Arrange: Setup command manager
                var commandMock = new Mock<IWorkerCommandManager>();
                hc.SetSingleton(commandMock.Object);
                var pagingLogger = new Mock<IPagingLogger>();
                hc.EnqueueInstance(pagingLogger.Object);

                // Act.
                ec.InitializeJob(jobRequest, CancellationToken.None);
                ec.SetStepTarget(steps[0].Target);

                // Assert.
                Assert.IsType<HostInfo>(ec.StepTarget());
                commandMock.Verify(x => x.SetCommandRestrictionPolicy(It.IsAny<AttributeBasedWorkerCommandRestrictionPolicy>()));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void StepTarget_LoadStepContainersWithoutJobContainer()
        {
            using (TestHostContext hc = CreateTestContext())
            {
                var ec = new Agent.Worker.ExecutionContext();
                ec.Initialize(hc);

                var pipeContainer = new Pipelines.ContainerResource {
                    Alias = "container"
                };
                pipeContainer.Properties.Set<string>("image", "someimage");
                // Arrange: Create a job request message.
                TaskOrchestrationPlanReference plan = new TaskOrchestrationPlanReference();
                TimelineReference timeline = new TimelineReference();
                JobEnvironment environment = new JobEnvironment();
                environment.SystemConnection = new ServiceEndpoint();
                List<Pipelines.JobStep> steps = new List<Pipelines.JobStep>();
                steps.Add(new Pipelines.TaskStep
                {
                    Target = new Pipelines.StepTarget
                    {
                        Target = "container"
                    },
                    Reference = new Pipelines.TaskStepDefinitionReference()
                });
                var resources = new Pipelines.JobResources();
                resources.Containers.Add(pipeContainer);
                Guid JobId = Guid.NewGuid();
                string jobName = "some job name";
                var jobRequest = new Pipelines.AgentJobRequestMessage(plan, timeline, JobId, jobName, jobName, null, new Dictionary<string, string>(),
                    new Dictionary<string, VariableValue>(), new List<MaskHint>(), resources, new Pipelines.WorkspaceOptions(), steps);

                // Arrange: Setup command manager
                var pagingLogger = new Mock<IPagingLogger>();
                hc.EnqueueInstance(pagingLogger.Object);

                // Act.
                ec.InitializeJob(jobRequest, CancellationToken.None);

                // Assert.
                Assert.Equal(1, ec.Containers.Count());
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void SidecarContainers_VerifyNotJobContainers()
        {
            using (TestHostContext hc = CreateTestContext())
            {
                var ec = new Agent.Worker.ExecutionContext();
                ec.Initialize(hc);

                var pipeContainer = new Pipelines.ContainerResource {
                    Alias = "container"
                };
                var pipeContainerSidecar = new Pipelines.ContainerResource {
                    Alias = "sidecar"
                };
                var pipeContainerExtra = new Pipelines.ContainerResource {
                    Alias = "extra"
                };
                pipeContainer.Properties.Set<string>("image", "someimage");
                pipeContainerSidecar.Properties.Set<string>("image", "someimage");
                pipeContainerExtra.Properties.Set<string>("image", "someimage");
                // Arrange: Create a job request message.
                TaskOrchestrationPlanReference plan = new TaskOrchestrationPlanReference();
                TimelineReference timeline = new TimelineReference();
                JobEnvironment environment = new JobEnvironment();
                environment.SystemConnection = new ServiceEndpoint();
                List<Pipelines.JobStep> steps = new List<Pipelines.JobStep>();
                steps.Add(new Pipelines.TaskStep
                {
                    Reference = new Pipelines.TaskStepDefinitionReference()
                });
                var resources = new Pipelines.JobResources();
                resources.Containers.Add(pipeContainer);
                resources.Containers.Add(pipeContainerSidecar);
                resources.Containers.Add(pipeContainerExtra);
                Guid JobId = Guid.NewGuid();
                string jobName = "some job name";
                var sidecarContainers = new Dictionary<string, string>();
                sidecarContainers.Add("sidecar", "sidecar");
                var jobRequest = new Pipelines.AgentJobRequestMessage(plan, timeline, JobId, jobName, jobName, null, sidecarContainers,
                    new Dictionary<string, VariableValue>(), new List<MaskHint>(), resources, new Pipelines.WorkspaceOptions(), steps);

                // Arrange: Setup command manager
                var pagingLogger = new Mock<IPagingLogger>();
                hc.EnqueueInstance(pagingLogger.Object);

                // Act.
                ec.InitializeJob(jobRequest, CancellationToken.None);

                // Assert.
                Assert.Equal(2, ec.Containers.Count());
                Assert.Equal(1, ec.SidecarContainers.Count());
                Assert.False(ec.SidecarContainers.First().IsJobContainer);
            }
        }

        private TestHostContext CreateTestContext([CallerMemberName] String testName = "")
        {
            var hc = new TestHostContext(this, testName);

            // Arrange: Setup the configation store.
            var configurationStore = new Mock<IConfigurationStore>();
            configurationStore.Setup(x => x.GetSettings()).Returns(new AgentSettings());
            hc.SetSingleton(configurationStore.Object);

            // Arrange: Setup the proxy configation.
            var proxy = new Mock<IVstsAgentWebProxy>();
            hc.SetSingleton(proxy.Object);

            // Arrange: Setup the cert configation.
            var cert = new Mock<IAgentCertificateManager>();
            hc.SetSingleton(cert.Object);

            // Arrange: Create the execution context.
            hc.SetSingleton(new Mock<IJobServerQueue>().Object);
            return hc;
        }

        private JobRequestMessage CreateJobRequestMessage()
        {
            TaskOrchestrationPlanReference plan = new TaskOrchestrationPlanReference();
            TimelineReference timeline = new TimelineReference();
            JobEnvironment environment = new JobEnvironment();
            environment.SystemConnection = new ServiceEndpoint();
            environment.Variables["v1"] = "v1";
            List<TaskInstance> tasks = new List<TaskInstance>();
            Guid JobId = Guid.NewGuid();
            string jobName = "some job name";
            return new AgentJobRequestMessage(plan, timeline, JobId, jobName, jobName, environment, tasks);
        }
    }
}
