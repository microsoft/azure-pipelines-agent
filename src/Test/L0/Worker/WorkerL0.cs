// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Microsoft.VisualStudio.Services.WebApi;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker
{
    public sealed class WorkerL0
    {
        private Mock<IProcessChannel> _processChannel;
        private Mock<IJobRunner> _jobRunner;
        private Mock<IVstsAgentWebProxy> _proxy;
        private Mock<IAgentCertificateManager> _cert;

        public WorkerL0()
        {
            _processChannel = new Mock<IProcessChannel>();
            _jobRunner = new Mock<IJobRunner>();
            _proxy = new Mock<IVstsAgentWebProxy>();
            _cert = new Mock<IAgentCertificateManager>();
        }

        private Pipelines.AgentJobRequestMessage CreateJobRequestMessage(string jobName)
        {
            TaskOrchestrationPlanReference plan = new TaskOrchestrationPlanReference() { PlanId = Guid.NewGuid() };
            TimelineReference timeline = null;
            Dictionary<string, VariableValue> variables = new Dictionary<string, VariableValue>(StringComparer.OrdinalIgnoreCase);
            variables[Constants.Variables.System.Culture] = "en-US";
            Pipelines.JobResources resources = new Pipelines.JobResources();
            var serviceEndpoint = new ServiceEndpoint();
            serviceEndpoint.Authorization = new EndpointAuthorization();
            serviceEndpoint.Authorization.Parameters.Add("nullValue", null);
            resources.Endpoints.Add(serviceEndpoint);

            List<Pipelines.JobStep> tasks = new List<Pipelines.JobStep>();
            tasks.Add(new Pipelines.TaskStep()
            {
                Id = Guid.NewGuid(),
                Reference = new Pipelines.TaskStepDefinitionReference()
                {
                    Id = Guid.NewGuid(),
                    Name = "TestTask",
                    Version = "1.0.0"
                }
            });
            Guid JobId = Guid.NewGuid();
            var sidecarContainers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["nginx"] = "nginx"
            };
            var jobRequest = new Pipelines.AgentJobRequestMessage(plan, timeline, JobId, jobName, jobName, "ubuntu", sidecarContainers, variables, new List<MaskHint>(), resources, null, tasks);
            return jobRequest;
        }

        private JobCancelMessage CreateJobCancelMessage(Guid jobId)
        {
            return new JobCancelMessage(jobId, TimeSpan.FromSeconds(0));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async void DispatchRunNewJob()
        {
            //Arrange
            using (var hc = new TestHostContext(this))
            using (var tokenSource = new CancellationTokenSource())
            {
                var worker = new Microsoft.VisualStudio.Services.Agent.Worker.Worker();
                hc.EnqueueInstance<IProcessChannel>(_processChannel.Object);
                hc.EnqueueInstance<IJobRunner>(_jobRunner.Object);
                hc.SetSingleton<IVstsAgentWebProxy>(_proxy.Object);
                hc.SetSingleton<IAgentCertificateManager>(_cert.Object);
                worker.Initialize(hc);
                var jobMessage = CreateJobRequestMessage("job1");
                var arWorkerMessages = new WorkerMessage[]
                    {
                        new WorkerMessage
                        {
                            Body = JsonUtility.ToString(jobMessage),
                            MessageType = MessageType.NewJobRequest
                        }
                    };
                var workerMessages = new Queue<WorkerMessage>(arWorkerMessages);

                _processChannel
                    .Setup(x => x.ReceiveAsync(It.IsAny<CancellationToken>()))
                    .Returns(async () =>
                    {
                        // Return the job message.
                        if (workerMessages.Count > 0)
                        {
                            return workerMessages.Dequeue();
                        }

                        // Wait for the text to run
                        await Task.Delay(-1, tokenSource.Token);
                        return default(WorkerMessage);
                    });
                _jobRunner.Setup(x => x.RunAsync(It.IsAny<Pipelines.AgentJobRequestMessage>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.FromResult<TaskResult>(TaskResult.Succeeded));

                //Act
                await worker.RunAsync(pipeIn: "1", pipeOut: "2");

                //Assert
                _processChannel.Verify(x => x.StartClient("1", "2"), Times.Once());
                _jobRunner.Verify(x => x.RunAsync(
                    It.Is<Pipelines.AgentJobRequestMessage>(y => IsMessageIdentical(y, jobMessage)), It.IsAny<CancellationToken>()));
                tokenSource.Cancel();
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async void DispatchCancellation()
        {
            //Arrange
            using (var hc = new TestHostContext(this))
            {
                var worker = new Microsoft.VisualStudio.Services.Agent.Worker.Worker();
                hc.EnqueueInstance<IProcessChannel>(_processChannel.Object);
                hc.EnqueueInstance<IJobRunner>(_jobRunner.Object);
                hc.SetSingleton<IVstsAgentWebProxy>(_proxy.Object);
                hc.SetSingleton<IAgentCertificateManager>(_cert.Object);
                worker.Initialize(hc);
                var jobMessage = CreateJobRequestMessage("job1");
                var cancelMessage = CreateJobCancelMessage(jobMessage.JobId);
                var arWorkerMessages = new WorkerMessage[]
                    {
                        new WorkerMessage
                        {
                            Body = JsonUtility.ToString(jobMessage),
                            MessageType = MessageType.NewJobRequest
                        },
                        new WorkerMessage
                        {
                            Body = JsonUtility.ToString(cancelMessage),
                            MessageType = MessageType.CancelRequest
                        }

                    };
                var workerMessages = new Queue<WorkerMessage>(arWorkerMessages);

                _processChannel.Setup(x => x.ReceiveAsync(It.IsAny<CancellationToken>()))
                    .Returns(() => Task.FromResult(workerMessages.Dequeue()));
                _jobRunner.Setup(x => x.RunAsync(It.IsAny<Pipelines.AgentJobRequestMessage>(), It.IsAny<CancellationToken>()))
                    .Returns(
                    async (Pipelines.AgentJobRequestMessage jm, CancellationToken ct) =>
                    {
                        await Task.Delay(-1, ct);
                        return TaskResult.Canceled;
                    });

                //Act
                await Assert.ThrowsAsync<TaskCanceledException>(
                    async () => await worker.RunAsync("1", "2"));

                //Assert
                _processChannel.Verify(x => x.StartClient("1", "2"), Times.Once());
                _jobRunner.Verify(x => x.RunAsync(
                    It.Is<Pipelines.AgentJobRequestMessage>(y => IsMessageIdentical(y, jobMessage)), It.IsAny<CancellationToken>()));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void VerifyJobRequestMessagePiiDataIsScrubbed()
        {
            // Arrange
            Pipelines.AgentJobRequestMessage message = CreateJobRequestMessage("jobwithpiidata");

            // Populate PII variables
            foreach (string piiVariable in Variables.PiiVariables)
            {
                message.Variables.Add(piiVariable, "MyPiiVariable");
            }

            foreach (string piiVariableSuffix in Variables.PiiArtifactVariableSuffixes)
            {
                message.Variables.Add($"{Variables.PiiArtifactVariablePrefix}.MyArtifact.{piiVariableSuffix}", "MyPiiVariable");
            }

            // Populate the repository PII data
            Pipelines.RepositoryResource repository = new Pipelines.RepositoryResource();

            repository.Properties.Set(
                Pipelines.RepositoryPropertyNames.VersionInfo,
                new Pipelines.VersionInfo()
                {
                    Author = "MyAuthor"
                });

            message.Resources.Repositories.Add(repository);

            // Act
            Pipelines.AgentJobRequestMessage scrubbedMessage = WorkerUtilities.ScrubPiiData(message);

            // Assert
            foreach (string piiVariable in Variables.PiiVariables)
            {
                scrubbedMessage.Variables.TryGetValue(piiVariable, out VariableValue value);

                Assert.Equal("[PII]", value.Value);
            }

            foreach (string piiVariableSuffix in Variables.PiiArtifactVariableSuffixes)
            {
                scrubbedMessage.Variables.TryGetValue($"{Variables.PiiArtifactVariablePrefix}.MyArtifact.{piiVariableSuffix}", out VariableValue value);

                Assert.Equal("[PII]", value.Value);
            }

            Pipelines.RepositoryResource scrubbedRepo = scrubbedMessage.Resources.Repositories[0];
            Pipelines.VersionInfo scrubbedInfo = scrubbedRepo.Properties.Get<Pipelines.VersionInfo>(Pipelines.RepositoryPropertyNames.VersionInfo);

            Assert.Equal("[PII]", scrubbedInfo.Author);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void VerifyJobRequestMessageVsoCommandsDeactivated()
        {
            Pipelines.AgentJobRequestMessage message = CreateJobRequestMessage("jobWithVsoCommands");

            message.Variables[Constants.Variables.Build.SourceVersionMessage] = "##vso[setVariable]etc1";
            message.Variables[Constants.Variables.System.SourceVersionMessage] = "##vso[setVariable]etc2";
            message.Variables[Constants.Variables.Build.DefinitionName] = "##vso[setVariable]etc3";
            message.Variables[Constants.Variables.System.DefinitionName] = "##vso[setVariable]etc4";
            message.Variables[Constants.Variables.Release.ReleaseDefinitionName] = "##vso[setVariable]etc5";
            message.Variables[Constants.Variables.Release.ReleaseEnvironmentName] = "##vso[setVariable]etc6";
            message.Variables[Constants.Variables.Build.SourceVersionAuthor] = "##vso[setVariable]etc7";

            var scrubbedMessage = WorkerUtilities.DeactivateVsoCommandsFromJobMessageVariables(message);

            Assert.Equal("**vso[setVariable]etc1", scrubbedMessage.Variables[Constants.Variables.Build.SourceVersionMessage]);
            Assert.Equal("**vso[setVariable]etc2", scrubbedMessage.Variables[Constants.Variables.System.SourceVersionMessage]);
            Assert.Equal("**vso[setVariable]etc3", scrubbedMessage.Variables[Constants.Variables.Build.DefinitionName]);
            Assert.Equal("**vso[setVariable]etc4", scrubbedMessage.Variables[Constants.Variables.System.DefinitionName]);
            Assert.Equal("**vso[setVariable]etc5", scrubbedMessage.Variables[Constants.Variables.Release.ReleaseDefinitionName]);
            Assert.Equal("**vso[setVariable]etc6", scrubbedMessage.Variables[Constants.Variables.Release.ReleaseEnvironmentName]);
            Assert.Equal("**vso[setVariable]etc7", scrubbedMessage.Variables[Constants.Variables.Build.SourceVersionAuthor]);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void VerifyIfOtherVariablesNotDeactivatesVsoCommands()
        {
            Pipelines.AgentJobRequestMessage message = CreateJobRequestMessage("jobWithVsoCommands");

            message.Variables[Constants.Variables.Build.RepoName] = "##vso[setVariable]etc1";
            message.Variables[Constants.Variables.System.JobId] = "##vso[setVariable]etc2";

            var scrubbedMessage = WorkerUtilities.DeactivateVsoCommandsFromJobMessageVariables(message);

            Assert.Equal("##vso[setVariable]etc1", scrubbedMessage.Variables[Constants.Variables.Build.RepoName]);
            Assert.Equal("##vso[setVariable]etc2", scrubbedMessage.Variables[Constants.Variables.System.JobId]);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void VerifyJobRequestMessageVsoCommandsDeactivatedIfVariableCasesNotMatch()
        {
            Pipelines.AgentJobRequestMessage message = CreateJobRequestMessage("jobWithVsoCommands");

            message.Variables[Constants.Variables.Build.SourceVersionMessage.ToUpper()] = "##vso[setVariable]etc1";
            message.Variables[Constants.Variables.System.SourceVersionMessage.ToLower()] = "##vso[setVariable]etc2";

            var scrubbedMessage = WorkerUtilities.DeactivateVsoCommandsFromJobMessageVariables(message);

            Assert.Equal("**vso[setVariable]etc1", scrubbedMessage.Variables[Constants.Variables.Build.SourceVersionMessage]);
            Assert.Equal("**vso[setVariable]etc2", scrubbedMessage.Variables[Constants.Variables.System.SourceVersionMessage]);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void VerifyJobRequestMessageVsoCommandsDeactivatedIfVariableCasesHandlesNullValues()
        {
            Pipelines.AgentJobRequestMessage message = CreateJobRequestMessage("jobWithVsoCommands");

            message.Variables[Constants.Variables.Build.SourceVersionMessage] = "";
            message.Variables[Constants.Variables.System.SourceVersionMessage] = null;
            message.Variables[Constants.Variables.Build.DefinitionName] = " ";

            var scrubbedMessage = WorkerUtilities.DeactivateVsoCommandsFromJobMessageVariables(message);

            Assert.Equal("", scrubbedMessage.Variables[Constants.Variables.Build.SourceVersionMessage]);
            Assert.Equal("", scrubbedMessage.Variables[Constants.Variables.System.SourceVersionMessage]);
            Assert.Equal(" ", scrubbedMessage.Variables[Constants.Variables.Build.DefinitionName]);
        }

        private bool IsMessageIdentical(Pipelines.AgentJobRequestMessage source, Pipelines.AgentJobRequestMessage target)
        {
            if (source == null && target == null)
            {
                return true;
            }
            if (source != null && target == null)
            {
                return false;
            }
            if (source == null && target != null)
            {
                return false;
            }
            if (source.JobContainer != target.JobContainer)
            {
                return false;
            }
            if (source.JobDisplayName != target.JobDisplayName)
            {
                return false;
            }
            if (source.JobId != target.JobId)
            {
                return false;
            }
            if (source.JobName != target.JobName)
            {
                return false;
            }
            if (source.MaskHints.Count != target.MaskHints.Count)
            {
                return false;
            }
            if (source.MessageType != target.MessageType)
            {
                return false;
            }
            if (source.Plan.PlanId != target.Plan.PlanId)
            {
                return false;
            }
            if (source.RequestId != target.RequestId)
            {
                return false;
            }
            if (source.Resources.Endpoints.Count != target.Resources.Endpoints.Count)
            {
                return false;
            }
            if (source.Steps.Count != target.Steps.Count)
            {
                return false;
            }
            if (source.Variables.Count != target.Variables.Count)
            {
                return false;
            }

            return true;
        }
    }
}
