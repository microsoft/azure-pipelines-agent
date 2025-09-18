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
using Microsoft.VisualStudio.Services.Agent.Util;

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
            using (var ec = new Agent.Worker.ExecutionContext())
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
            using (var ec = new Agent.Worker.ExecutionContext())
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
            using (var ec = new Agent.Worker.ExecutionContext())
            {
                ec.Initialize(hc);

                var pipeContainer = new Pipelines.ContainerResource
                {
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

                // Arrange
                var pagingLogger = new Mock<IPagingLogger>();
                hc.EnqueueInstance(pagingLogger.Object);

                // Act.
                ec.InitializeJob(jobRequest, CancellationToken.None);
                ec.SetStepTarget(steps[0].Target);

                // Assert.
                Assert.IsType<ContainerInfo>(ec.StepTarget());
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void StepTarget_RestrictedCommands_Host()
        {
            using (TestHostContext hc = CreateTestContext())
            using (var ec = new Agent.Worker.ExecutionContext())
            {
                ec.Initialize(hc);

                var pipeContainer = new Pipelines.ContainerResource
                {
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

                // Arrange
                var pagingLogger = new Mock<IPagingLogger>();
                hc.EnqueueInstance(pagingLogger.Object);

                // Act.
                ec.InitializeJob(jobRequest, CancellationToken.None);
                ec.SetStepTarget(steps[0].Target);

                // Assert.
                Assert.IsType<HostInfo>(ec.StepTarget());
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void StepTarget_LoadStepContainersWithoutJobContainer()
        {
            using (TestHostContext hc = CreateTestContext())
            using (var ec = new Agent.Worker.ExecutionContext())
            {
                ec.Initialize(hc);

                var pipeContainer = new Pipelines.ContainerResource
                {
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
            using (var ec = new Agent.Worker.ExecutionContext())
            {
                ec.Initialize(hc);

                var pipeContainer = new Pipelines.ContainerResource
                {
                    Alias = "container"
                };
                var pipeContainerSidecar = new Pipelines.ContainerResource
                {
                    Alias = "sidecar"
                };
                var pipeContainerExtra = new Pipelines.ContainerResource
                {
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

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void InitializeJob_should_set_JobSettings()
        {
            using (TestHostContext hc = CreateTestContext())
            using (var ec = new Agent.Worker.ExecutionContext())
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
                hc.EnqueueInstance(pagingLogger.Object);

                ec.Initialize(hc);

                // Act.
                ec.InitializeJob(jobRequest, CancellationToken.None);

                // Assert.
                Assert.NotNull(ec.JobSettings);
                Assert.Equal(Boolean.FalseString, ec.JobSettings[WellKnownJobSettings.HasMultipleCheckouts]);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void InitializeJob_should_set_JobSettings_multicheckout()
        {
            using (TestHostContext hc = CreateTestContext())
            using (var ec = new Agent.Worker.ExecutionContext())
            {
                // Arrange: Create a job request message.
                TaskOrchestrationPlanReference plan = new TaskOrchestrationPlanReference();
                TimelineReference timeline = new TimelineReference();
                JobEnvironment environment = new JobEnvironment();
                environment.SystemConnection = new ServiceEndpoint();
                List<TaskInstance> tasks = new List<TaskInstance>();
                tasks.Add(new TaskInstance() { Id = Pipelines.PipelineConstants.CheckoutTask.Id, Version = Pipelines.PipelineConstants.CheckoutTask.Version });
                tasks.Add(new TaskInstance() { Id = Pipelines.PipelineConstants.CheckoutTask.Id, Version = Pipelines.PipelineConstants.CheckoutTask.Version });
                Guid JobId = Guid.NewGuid();
                string jobName = "some job name";
                var jobRequest = Pipelines.AgentJobRequestMessageUtil.Convert(new AgentJobRequestMessage(plan, timeline, JobId, jobName, jobName, environment, tasks));

                // Arrange: Setup the paging logger.
                var pagingLogger = new Mock<IPagingLogger>();
                hc.EnqueueInstance(pagingLogger.Object);

                ec.Initialize(hc);

                // Act.
                ec.InitializeJob(jobRequest, CancellationToken.None);

                // Assert.
                Assert.NotNull(ec.JobSettings);
                Assert.Equal(Boolean.TrueString, ec.JobSettings[WellKnownJobSettings.HasMultipleCheckouts]);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void InitializeJob_should_mark_primary_repository()
        {
            // Note: the primary repository is defined as the first repository that is checked out in the job
            using (TestHostContext hc = CreateTestContext())
            using (var ec = new Agent.Worker.ExecutionContext())
            {
                // Arrange: Create a job request message.
                TaskOrchestrationPlanReference plan = new TaskOrchestrationPlanReference();
                TimelineReference timeline = new TimelineReference();
                JobEnvironment environment = new JobEnvironment();
                environment.SystemConnection = new ServiceEndpoint();
                List<TaskInstance> tasks = new List<TaskInstance>();
                tasks.Add(new TaskInstance() { Id = Pipelines.PipelineConstants.CheckoutTask.Id, Version = Pipelines.PipelineConstants.CheckoutTask.Version, Inputs = { { Pipelines.PipelineConstants.CheckoutTaskInputs.Repository, "repo1" } } });
                Guid JobId = Guid.NewGuid();
                string jobName = "some job name";
                var jobRequest = Pipelines.AgentJobRequestMessageUtil.Convert(new AgentJobRequestMessage(plan, timeline, JobId, jobName, jobName, environment, tasks));
                var repo1 = new Pipelines.RepositoryResource() { Alias = "repo1" };
                jobRequest.Resources.Repositories.Add(repo1);

                // Arrange: Setup the paging logger.
                var pagingLogger = new Mock<IPagingLogger>();
                hc.EnqueueInstance(pagingLogger.Object);

                ec.Initialize(hc);

                // Act.
                ec.InitializeJob(jobRequest, CancellationToken.None);

                // Assert.
                Assert.NotNull(ec.JobSettings);
                Assert.Equal(Boolean.FalseString, ec.JobSettings[WellKnownJobSettings.HasMultipleCheckouts]);
                Assert.Equal("repo1", ec.JobSettings[WellKnownJobSettings.FirstRepositoryCheckedOut]);
                Assert.False(ec.JobSettings.ContainsKey(WellKnownJobSettings.DefaultWorkingDirectoryRepository));
                Assert.Equal(Boolean.TrueString, repo1.Properties.Get<string>(RepositoryUtil.IsPrimaryRepository));
                Assert.Equal(Boolean.FalseString, repo1.Properties.Get<string>(RepositoryUtil.IsDefaultWorkingDirectoryRepository, Boolean.FalseString));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void InitializeJob_should_mark_default_workdirectory_repository()
        {
            // Note: the primary repository is defined as the first repository that is checked out in the job
            using (TestHostContext hc = CreateTestContext())
            using (var ec = new Agent.Worker.ExecutionContext())
            {
                // Arrange: Create a job request message.
                TaskOrchestrationPlanReference plan = new TaskOrchestrationPlanReference();
                TimelineReference timeline = new TimelineReference();
                JobEnvironment environment = new JobEnvironment();
                environment.SystemConnection = new ServiceEndpoint();
                List<TaskInstance> tasks = new List<TaskInstance>();
                tasks.Add(new TaskInstance() { Id = Pipelines.PipelineConstants.CheckoutTask.Id, Version = Pipelines.PipelineConstants.CheckoutTask.Version, Inputs = { { Pipelines.PipelineConstants.CheckoutTaskInputs.Repository, "repo1" }, { Pipelines.PipelineConstants.CheckoutTaskInputs.WorkspaceRepo, "true" } } });
                Guid JobId = Guid.NewGuid();
                string jobName = "some job name";
                var jobRequest = Pipelines.AgentJobRequestMessageUtil.Convert(new AgentJobRequestMessage(plan, timeline, JobId, jobName, jobName, environment, tasks));
                var repo1 = new Pipelines.RepositoryResource() { Alias = "repo1" };
                jobRequest.Resources.Repositories.Add(repo1);

                // Arrange: Setup the paging logger.
                var pagingLogger = new Mock<IPagingLogger>();
                hc.EnqueueInstance(pagingLogger.Object);

                ec.Initialize(hc);

                // Act.
                ec.InitializeJob(jobRequest, CancellationToken.None);

                // Assert.
                Assert.NotNull(ec.JobSettings);
                Assert.Equal(Boolean.FalseString, ec.JobSettings[WellKnownJobSettings.HasMultipleCheckouts]);
                Assert.Equal("repo1", ec.JobSettings[WellKnownJobSettings.DefaultWorkingDirectoryRepository]);
                Assert.Equal(Boolean.TrueString, repo1.Properties.Get<string>(RepositoryUtil.IsPrimaryRepository));
                Assert.Equal(Boolean.TrueString, repo1.Properties.Get<string>(RepositoryUtil.IsDefaultWorkingDirectoryRepository, Boolean.FalseString));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void InitializeJob_should_mark_primary_repository_in_multicheckout()
        {
            // Note: the primary repository is defined as the first repository that is checked out in the job
            using (TestHostContext hc = CreateTestContext())
            using (var ec = new Agent.Worker.ExecutionContext())
            {
                // Arrange: Create a job request message.
                TaskOrchestrationPlanReference plan = new TaskOrchestrationPlanReference();
                TimelineReference timeline = new TimelineReference();
                JobEnvironment environment = new JobEnvironment();
                environment.SystemConnection = new ServiceEndpoint();
                List<TaskInstance> tasks = new List<TaskInstance>();
                tasks.Add(new TaskInstance() { Id = Pipelines.PipelineConstants.CheckoutTask.Id, Version = Pipelines.PipelineConstants.CheckoutTask.Version, Inputs = { { Pipelines.PipelineConstants.CheckoutTaskInputs.Repository, "repo2" } } });
                tasks.Add(new TaskInstance() { Id = Pipelines.PipelineConstants.CheckoutTask.Id, Version = Pipelines.PipelineConstants.CheckoutTask.Version, Inputs = { { Pipelines.PipelineConstants.CheckoutTaskInputs.Repository, "repo3" } } });
                Guid JobId = Guid.NewGuid();
                string jobName = "some job name";
                var jobRequest = Pipelines.AgentJobRequestMessageUtil.Convert(new AgentJobRequestMessage(plan, timeline, JobId, jobName, jobName, environment, tasks));
                var repo1 = new Pipelines.RepositoryResource() { Alias = "self" };
                var repo2 = new Pipelines.RepositoryResource() { Alias = "repo2" };
                var repo3 = new Pipelines.RepositoryResource() { Alias = "repo3" };
                jobRequest.Resources.Repositories.Add(repo1);
                jobRequest.Resources.Repositories.Add(repo2);
                jobRequest.Resources.Repositories.Add(repo3);

                // Arrange: Setup the paging logger.
                var pagingLogger = new Mock<IPagingLogger>();
                hc.EnqueueInstance(pagingLogger.Object);


                ec.Initialize(hc);

                // Act.
                ec.InitializeJob(jobRequest, CancellationToken.None);

                // Assert.
                Assert.NotNull(ec.JobSettings);
                Assert.Equal(Boolean.TrueString, ec.JobSettings[WellKnownJobSettings.HasMultipleCheckouts]);
                Assert.Equal("repo2", ec.JobSettings[WellKnownJobSettings.FirstRepositoryCheckedOut]);
                Assert.False(ec.JobSettings.ContainsKey(WellKnownJobSettings.DefaultWorkingDirectoryRepository));
                Assert.Equal(Boolean.FalseString, repo1.Properties.Get<string>(RepositoryUtil.IsPrimaryRepository, Boolean.FalseString));
                Assert.Equal(Boolean.TrueString, repo2.Properties.Get<string>(RepositoryUtil.IsPrimaryRepository, Boolean.FalseString));
                Assert.Equal(Boolean.FalseString, repo3.Properties.Get<string>(RepositoryUtil.IsPrimaryRepository, Boolean.FalseString));
                Assert.Equal(Boolean.FalseString, repo1.Properties.Get<string>(RepositoryUtil.IsDefaultWorkingDirectoryRepository, Boolean.FalseString));
                Assert.Equal(Boolean.FalseString, repo2.Properties.Get<string>(RepositoryUtil.IsDefaultWorkingDirectoryRepository, Boolean.FalseString));
                Assert.Equal(Boolean.FalseString, repo3.Properties.Get<string>(RepositoryUtil.IsDefaultWorkingDirectoryRepository, Boolean.FalseString));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void InitializeJob_should_mark_default_workdirectory_repository_in_multicheckout()
        {
            // Note: the primary repository is defined as the first repository that is checked out in the job
            using (TestHostContext hc = CreateTestContext())
            using (var ec = new Agent.Worker.ExecutionContext())
            {
                // Arrange: Create a job request message.
                TaskOrchestrationPlanReference plan = new TaskOrchestrationPlanReference();
                TimelineReference timeline = new TimelineReference();
                JobEnvironment environment = new JobEnvironment();
                environment.SystemConnection = new ServiceEndpoint();
                List<TaskInstance> tasks = new List<TaskInstance>();
                tasks.Add(new TaskInstance() { Id = Pipelines.PipelineConstants.CheckoutTask.Id, Version = Pipelines.PipelineConstants.CheckoutTask.Version, Inputs = { { Pipelines.PipelineConstants.CheckoutTaskInputs.Repository, "self" } } });
                tasks.Add(new TaskInstance() { Id = Pipelines.PipelineConstants.CheckoutTask.Id, Version = Pipelines.PipelineConstants.CheckoutTask.Version, Inputs = { { Pipelines.PipelineConstants.CheckoutTaskInputs.Repository, "repo2" }, { Pipelines.PipelineConstants.CheckoutTaskInputs.WorkspaceRepo, "true" } } });
                tasks.Add(new TaskInstance() { Id = Pipelines.PipelineConstants.CheckoutTask.Id, Version = Pipelines.PipelineConstants.CheckoutTask.Version, Inputs = { { Pipelines.PipelineConstants.CheckoutTaskInputs.Repository, "repo3" } } });
                Guid JobId = Guid.NewGuid();
                string jobName = "some job name";
                var jobRequest = Pipelines.AgentJobRequestMessageUtil.Convert(new AgentJobRequestMessage(plan, timeline, JobId, jobName, jobName, environment, tasks));
                var repo1 = new Pipelines.RepositoryResource() { Alias = "self" };
                var repo2 = new Pipelines.RepositoryResource() { Alias = "repo2" };
                var repo3 = new Pipelines.RepositoryResource() { Alias = "repo3" };
                jobRequest.Resources.Repositories.Add(repo1);
                jobRequest.Resources.Repositories.Add(repo2);
                jobRequest.Resources.Repositories.Add(repo3);

                // Arrange: Setup the paging logger.
                var pagingLogger = new Mock<IPagingLogger>();
                hc.EnqueueInstance(pagingLogger.Object);


                ec.Initialize(hc);

                // Act.
                ec.InitializeJob(jobRequest, CancellationToken.None);

                // Assert.
                Assert.NotNull(ec.JobSettings);
                Assert.Equal(Boolean.TrueString, ec.JobSettings[WellKnownJobSettings.HasMultipleCheckouts]);
                Assert.Equal("self", ec.JobSettings[WellKnownJobSettings.FirstRepositoryCheckedOut]);
                Assert.Equal("repo2", ec.JobSettings[WellKnownJobSettings.DefaultWorkingDirectoryRepository]);
                Assert.Equal(Boolean.TrueString, repo1.Properties.Get<string>(RepositoryUtil.IsPrimaryRepository, Boolean.FalseString));
                Assert.Equal(Boolean.FalseString, repo2.Properties.Get<string>(RepositoryUtil.IsPrimaryRepository, Boolean.FalseString));
                Assert.Equal(Boolean.FalseString, repo3.Properties.Get<string>(RepositoryUtil.IsPrimaryRepository, Boolean.FalseString));
                Assert.Equal(Boolean.FalseString, repo1.Properties.Get<string>(RepositoryUtil.IsDefaultWorkingDirectoryRepository, Boolean.FalseString));
                Assert.Equal(Boolean.TrueString, repo2.Properties.Get<string>(RepositoryUtil.IsDefaultWorkingDirectoryRepository, Boolean.FalseString));
                Assert.Equal(Boolean.FalseString, repo3.Properties.Get<string>(RepositoryUtil.IsDefaultWorkingDirectoryRepository, Boolean.FalseString));
            }
        }

        [Theory]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        [InlineData(true, null, null)]
        [InlineData(true, null, "host")]
        [InlineData(true, null, "container")]
        [InlineData(true, "container", null)]
        [InlineData(true, "container", "host")]
        [InlineData(true, "container", "container")]
        [InlineData(false, null, null)]
        [InlineData(false, null, "host")]
        [InlineData(false, null, "container")]
        [InlineData(false, "container", null)]
        [InlineData(false, "container", "host")]
        [InlineData(false, "container", "container")]
        public void TranslatePathForStepTarget_should_convert_path_only_for_containers(bool isCheckout, string jobTarget, string stepTarget)
        {
            // Note: the primary repository is defined as the first repository that is checked out in the job
            using (TestHostContext hc = CreateTestContext())
            using (var ec = new Agent.Worker.ExecutionContext())
            {
                ec.Initialize(hc);

                // Arrange: Create a container.
                var pipeContainer = new Pipelines.ContainerResource
                {
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
                        Target = stepTarget
                    },
                    Reference = new Pipelines.TaskStepDefinitionReference()
                });
                var resources = new Pipelines.JobResources();
                resources.Containers.Add(pipeContainer);
                Guid JobId = Guid.NewGuid();
                string jobName = "some job name";
                var jobRequest = new Pipelines.AgentJobRequestMessage(plan, timeline, JobId, jobName, jobName, jobTarget, new Dictionary<string, string>(),
                    new Dictionary<string, VariableValue>(), new List<MaskHint>(), resources, new Pipelines.WorkspaceOptions(), steps);

                // Arrange
                var pagingLogger = new Mock<IPagingLogger>();
                hc.EnqueueInstance(pagingLogger.Object);

                // Act.
                ec.InitializeJob(jobRequest, CancellationToken.None);
                ec.SetStepTarget(steps[0].Target);
                ec.Variables.Set(Constants.Variables.Task.SkipTranslatorForCheckout, isCheckout.ToString());

                string stringBeforeTranslation = hc.GetDirectory(WellKnownDirectory.Work);
                string stringAfterTranslation = ec.TranslatePathForStepTarget(stringBeforeTranslation);

                // Assert.
                if ((stepTarget == "container") || (isCheckout is false && jobTarget == "container" && stepTarget == null))
                {
                    string stringContainer = "C:\\__w";
                    if (ec.StepTarget().ExecutionOS != PlatformUtil.OS.Windows)
                    {
                        stringContainer = "/__w";
                    }
                    Assert.Equal(stringContainer, stringAfterTranslation);
                }
                else
                {
                    Assert.Equal(stringBeforeTranslation, stringAfterTranslation);
                }
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

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void BuildCorrelationId_WithStepOnly_ReturnsShortenedStepId()
        {
            using (TestHostContext hc = CreateTestContext())
            using (var ec = new Agent.Worker.ExecutionContext())
            {
                // Arrange
                ec.Initialize(hc);
                string stepId = "60cf5508-70a7-5ba0-b727-5dd7f6763eb4";
                
                // Act
                ec.SetCorrelationStep(stepId);
                var correlationId = ec.BuildCorrelationId();

                // Debug: Print actual values
                System.Console.WriteLine($"Actual correlation ID: '{correlationId}'");
                System.Console.WriteLine($"Actual length: {correlationId.Length}");

                // Assert
                Assert.Equal("STEP-60cf550870a7", correlationId);
                Assert.Equal(17, correlationId.Length); // "STEP-" (5) + 12 characters = 17
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void BuildCorrelationId_WithTaskOnly_ReturnsShortenedTaskId()
        {
            using (TestHostContext hc = CreateTestContext())
            using (var ec = new Agent.Worker.ExecutionContext())
            {
                // Arrange
                ec.Initialize(hc);
                string taskId = "6d15af64-176c-496d-b583-fd2ae21d4df4";
                
                // Act
                ec.SetCorrelationTask(taskId);
                var correlationId = ec.BuildCorrelationId();

                // Assert
                Assert.Equal("TASK-6d15af64176c", correlationId);
                Assert.Equal(17, correlationId.Length); // "TASK-" (5) + 12 characters = 17
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void BuildCorrelationId_WithStepAndTask_ReturnsCombinedShortenedIds()
        {
            using (TestHostContext hc = CreateTestContext())
            using (var ec = new Agent.Worker.ExecutionContext())
            {
                // Arrange
                ec.Initialize(hc);
                string stepId = "60cf5508-70a7-5ba0-b727-5dd7f6763eb4";
                string taskId = "6d15af64-176c-496d-b583-fd2ae21d4df4";
                
                // Act
                ec.SetCorrelationStep(stepId);
                ec.SetCorrelationTask(taskId);
                var correlationId = ec.BuildCorrelationId();

                // Assert
                Assert.Equal("STEP-60cf550870a7|TASK-6d15af64176c", correlationId);
                Assert.Equal(35, correlationId.Length); // "STEP-" (5) + 12 + "|TASK-" (6) + 12 = 35
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void BuildCorrelationId_WithNoCorrelation_ReturnsEmpty()
        {
            using (TestHostContext hc = CreateTestContext())
            using (var ec = new Agent.Worker.ExecutionContext())
            {
                // Arrange
                ec.Initialize(hc);
                
                // Act
                var correlationId = ec.BuildCorrelationId();

                // Assert
                Assert.Equal(string.Empty, correlationId);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void BuildCorrelationId_WithShortGuid_ReturnsFullString()
        {
            using (TestHostContext hc = CreateTestContext())
            using (var ec = new Agent.Worker.ExecutionContext())
            {
                // Arrange
                ec.Initialize(hc);
                string shortStepId = "abc123def";
                
                // Act
                ec.SetCorrelationStep(shortStepId);
                var correlationId = ec.BuildCorrelationId();

                // Assert
                Assert.Equal("STEP-abc123def", correlationId);
                Assert.Equal(14, correlationId.Length); // "STEP-" + 9 characters
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void BuildCorrelationId_WithHyphenatedGuid_RemovesHyphensAndShortens()
        {
            using (TestHostContext hc = CreateTestContext())
            using (var ec = new Agent.Worker.ExecutionContext())
            {
                // Arrange
                ec.Initialize(hc);
                string stepId = "550e8400-e29b-41d4-a716-446655440000";
                
                // Act
                ec.SetCorrelationStep(stepId);
                var correlationId = ec.BuildCorrelationId();

                // Assert
                Assert.Equal("STEP-550e8400e29b", correlationId);
                Assert.True(correlationId.StartsWith("STEP-"));
                Assert.DoesNotContain("-", correlationId.Substring(5)); // No hyphens in the GUID part
                Assert.Equal(17, correlationId.Length); // "STEP-" (5) + 12 = 17
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void CorrelationContext_ClearMethods_ResetCorrectly()
        {
            using (TestHostContext hc = CreateTestContext())
            using (var ec = new Agent.Worker.ExecutionContext())
            {
                // Arrange
                ec.Initialize(hc);
                ec.SetCorrelationStep("step123");
                ec.SetCorrelationTask("task456");
                
                // Act & Assert - Clear step only
                ec.ClearCorrelationStep();
                var correlationWithTaskOnly = ec.BuildCorrelationId();
                Assert.Equal("TASK-task456", correlationWithTaskOnly);

                // Act & Assert - Clear task
                ec.ClearCorrelationTask();
                var correlationEmpty = ec.BuildCorrelationId();
                Assert.Equal(string.Empty, correlationEmpty);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void BuildCorrelationId_WithNullValues_HandlesGracefully()
        {
            using (TestHostContext hc = CreateTestContext())
            using (var ec = new Agent.Worker.ExecutionContext())
            {
                // Arrange
                ec.Initialize(hc);
                
                // Act - Set null values (should be handled by the method)
                ec.SetCorrelationStep(null);
                ec.SetCorrelationTask(null);
                var correlationId = ec.BuildCorrelationId();

                // Assert
                Assert.Equal(string.Empty, correlationId);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void BuildCorrelationId_WithEmptyStrings_HandlesGracefully()
        {
            using (TestHostContext hc = CreateTestContext())
            using (var ec = new Agent.Worker.ExecutionContext())
            {
                // Arrange
                ec.Initialize(hc);
                
                // Act - Set empty strings
                ec.SetCorrelationStep(string.Empty);
                ec.SetCorrelationTask(string.Empty);
                var correlationId = ec.BuildCorrelationId();

                // Assert
                Assert.Equal(string.Empty, correlationId);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void BuildCorrelationId_WithWhitespaceStrings_HandlesGracefully()
        {
            using (TestHostContext hc = CreateTestContext())
            using (var ec = new Agent.Worker.ExecutionContext())
            {
                // Arrange
                ec.Initialize(hc);
                
                // Act - Set whitespace strings
                ec.SetCorrelationStep("   ");
                ec.SetCorrelationTask("\t\n");
                var correlationId = ec.BuildCorrelationId();

                // Assert - Whitespace should be preserved in this implementation
                Assert.Equal("STEP-   |TASK-\t\n", correlationId);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void BuildCorrelationId_WithMixedCaseGuid_NormalizesProperly()
        {
            using (TestHostContext hc = CreateTestContext())
            using (var ec = new Agent.Worker.ExecutionContext())
            {
                // Arrange
                ec.Initialize(hc);
                string mixedCaseGuid = "60CF5508-70a7-5BA0-b727-5dd7f6763eb4";
                
                // Act
                ec.SetCorrelationStep(mixedCaseGuid);
                var correlationId = ec.BuildCorrelationId();

                // Assert - Should handle mixed case and remove hyphens
                Assert.Equal("STEP-60CF550870a7", correlationId);
                Assert.Equal(17, correlationId.Length); // "STEP-" (5) + 12 = 17
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void BuildCorrelationId_WithVariousFormats_ShorteningBehavior()
        {
            using (TestHostContext hc = CreateTestContext())
            using (var ec = new Agent.Worker.ExecutionContext())
            {
                // Arrange
                ec.Initialize(hc);
                
                // Test different input formats
                var testCases = new[]
                {
                    ("60cf5508-70a7-5ba0-b727-5dd7f6763eb4", "STEP-60cf550870a7"), // Standard GUID with hyphens
                    ("60cf550870a75ba0b7275dd7f6763eb4", "STEP-60cf550870a7"),     // GUID without hyphens
                    ("60CF5508-70A7", "STEP-60CF550870A7"),                      // Short string, no shortening
                    ("abc", "STEP-abc"),                                           // Very short string
                    ("1234567890abcdef1234567890abcdef", "STEP-1234567890ab"),    // 32-char hex string
                };

                foreach (var (input, expected) in testCases)
                {
                    // Act
                    ec.SetCorrelationStep(input);
                    var result = ec.BuildCorrelationId();

                    // Assert
                    Assert.Equal(expected, result);
                }
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void BuildCorrelationId_UniquenessProperty_DifferentInputsProduceDifferentOutputs()
        {
            using (TestHostContext hc = CreateTestContext())
            using (var ec = new Agent.Worker.ExecutionContext())
            {
                // Arrange
                ec.Initialize(hc);
                var uniqueGuids = new[]
                {
                    "60cf5508-70a7-5ba0-b727-5dd7f6763eb4",
                    "70cf5508-70a7-5ba0-b727-5dd7f6763eb4", // First char different
                    "6ba7b810-9dad-11d1-80b4-00c04fd430c8", // Completely different first 12 chars
                    "12345678-1234-5678-9abc-123456789abc", // Different pattern
                };

                var resultSet = new HashSet<string>();

                // Act & Assert
                foreach (var guid in uniqueGuids)
                {
                    ec.SetCorrelationStep(guid);
                    var result = ec.BuildCorrelationId();
                    
                    // Each result should be unique (note: GUIDs that differ only after char 12 will have same shortened result)
                    Assert.True(resultSet.Add(result), $"Duplicate result for GUID {guid}: {result}. This is expected if GUIDs differ only after position 12.");
                    
                    // Result should be properly formatted
                    Assert.StartsWith("STEP-", result);
                    Assert.Equal(17, result.Length); // "STEP-" (5) + 12 chars = 17
                }

                // All results should be different (for our carefully chosen test data)
                Assert.Equal(uniqueGuids.Length, resultSet.Count);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void BuildCorrelationId_ThreadSafety_AsyncLocalIsolation()
        {
            using (TestHostContext hc = CreateTestContext())
            {
                // This test verifies that different ExecutionContext instances
                // don't interfere with each other's correlation values
                
                using (var ec1 = new Agent.Worker.ExecutionContext())
                using (var ec2 = new Agent.Worker.ExecutionContext())
                {
                    // Arrange
                    ec1.Initialize(hc);
                    ec2.Initialize(hc);
                    
                    // Act
                    ec1.SetCorrelationStep("step1");
                    ec2.SetCorrelationStep("step2");
                    
                    var result1 = ec1.BuildCorrelationId();
                    var result2 = ec2.BuildCorrelationId();

                    // Assert
                    Assert.Equal("STEP-step1", result1);
                    Assert.Equal("STEP-step2", result2);
                    Assert.NotEqual(result1, result2);
                }
            }
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
