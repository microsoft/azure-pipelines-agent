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
                Assert.Equal(Boolean.TrueString, repo1.Properties.Get<string>(RepositoryUtil.IsPrimaryRepository));
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
                Assert.Equal(Boolean.FalseString, repo1.Properties.Get<string>(RepositoryUtil.IsPrimaryRepository, Boolean.FalseString));
                Assert.Equal(Boolean.TrueString, repo2.Properties.Get<string>(RepositoryUtil.IsPrimaryRepository, Boolean.FalseString));
                Assert.Equal(Boolean.FalseString, repo3.Properties.Get<string>(RepositoryUtil.IsPrimaryRepository, Boolean.FalseString));
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

        [Theory]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        // some secrets that CredScan should suppress
        [InlineData("xoxr-1xwlcyhsnfn9k69m4efzj3zkfhk", "***", true)] // Slack token
        [InlineData("xoxr-1xwlcyhsnfn9k69m4efzj3zkfhk", "xoxr-1xwlcyhsnfn9k69m4efzj3zkfhk", false)]
        [InlineData("(+n97tcqhcpvu9zkhwwiwx4==)", "(***)", true)] // 128-bit symmetric key
        [InlineData("(+n97tcqhcpvu9zkhwwiwx4==)", "(+n97tcqhcpvu9zkhwwiwx4==)", false)]
        [InlineData("<jwt>eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c</jwt>", "<jwt>***</jwt>", true)]
        [InlineData("<jwt>eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c</jwt>", "<jwt>eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c</jwt>", false)]
        // some secrets that CredScan should NOT suppress
        [InlineData("The password is knock knock knock", "The password is knock knock knock", true)]
        [InlineData("SSdtIGEgY29tcGxldGVseSBpbm5vY3VvdXMgc3RyaW5nLg==", "SSdtIGEgY29tcGxldGVseSBpbm5vY3VvdXMgc3RyaW5nLg==", true)]
        public void UseCredScan(string input, string expected, bool enabled)
        {
            // Arrange.
            try
            {
                Environment.SetEnvironmentVariable("AZP_USE_CREDSCAN_REGEXES", enabled.ToString());

                using (var test_hc = CreateTestContext())
                {
                    // Act.
                    var result = test_hc.SecretMasker.MaskSecrets(input);

                    // Assert.
                    Assert.Equal(expected, result);
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("AZP_USE_CREDSCAN_REGEXES", null);
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
