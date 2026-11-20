// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Agent.Listener.Configuration;
using Agent.Sdk;
using Microsoft.TeamFoundation.Framework.Common;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Listener;
using Microsoft.VisualStudio.Services.Agent.Listener.Telemetry;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.WebApi;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Listener
{
    public sealed class JobStartupFailureL0
    {
        [Theory]
        [InlineData("FeatureFlags", false)]
        [InlineData("StartWorker", true)]
        [Trait("Level", "L0")]
        [Trait("Category", "Agent")]
        public void StartupFailureMessageIncludesDetailsWithoutChangingGlobalLocalization(string stage, bool processFault)
        {
            string localizationBefore = StringUtil.Loc("MissingNodePath");
            using (var hc = new TestHostContext(this))
            {
                const string secret = "startup-test-secret";
                hc.SecretMasker.AddValue(secret, "JobStartupFailure");
                var exception = processFault
                    ? (Exception)new Win32Exception("Unable to launch worker " + secret)
                    : new InvalidOperationException("Startup failed " + secret);
                var jobId = Guid.NewGuid();
                const long requestId = 123;
                var resources = IOUtil.LoadObject<Dictionary<string, object>>(
                    Path.Combine(TestUtil.GetSrcPath(), "Misc", "layoutbin", "en-US", "strings.json"));
                string format = Assert.IsType<string>(resources["AgentWorkerStartupFailed"]);
                string message = hc.SecretMasker.MaskSecrets(StringUtil.Format(format, stage, jobId, requestId, exception.ToString()));

                Assert.Contains("startup", message);
                Assert.Contains(stage, message);
                Assert.Contains(jobId.ToString(), message);
                Assert.Contains(requestId.ToString(), message);
                Assert.Contains(exception.GetType().FullName, message);
                Assert.Contains(hc.SecretMasker.MaskSecrets(exception.Message), message);
                Assert.DoesNotContain(secret, message);
                Assert.Equal(localizationBefore, StringUtil.Loc("MissingNodePath"));
            }
        }

        [Theory]
        [InlineData("FeatureFlags", false, false, false, false)]
        [InlineData("StartWorker", false, false, false, false)]
        [InlineData("SendJobRequest", false, false, false, false)]
        [InlineData("StartWorker", true, false, false, false)]
        [InlineData("StartWorkerAfterSend", true, false, false, false)]
        [InlineData("FeatureFlags", false, true, false, false)]
        [InlineData("StartWorker", true, false, true, false)]
        [InlineData("FeatureFlags", false, false, false, true)]
        [InlineData("CreateProcessServices", false, false, false, false)]
        [Trait("Level", "L0")]
        [Trait("Category", "Agent")]
        public async Task ReportsStartupFailureWithoutCompletingJob(string stage, bool processFault, bool reportingFails, bool missingRecord, bool telemetryFails)
        {
            string localizationBefore = StringUtil.Loc("MissingNodePath");
            using (var hc = new TestHostContext(this))
            {
                hc.UseRealDelays = true;
                const string secret = "startup-test-secret";
                var exception = processFault
                    ? (Exception)new Win32Exception("Unable to launch worker " + secret)
                    : new InvalidOperationException("Startup failed " + secret);
                bool delayedProcessFault = stage == "StartWorkerAfterSend";
                var processCompletion = new TaskCompletionSource<int>();
                var environment = new JobEnvironment();
                environment.Endpoints.Add(new ServiceEndpoint
                {
                    Name = WellKnownServiceEndpointNames.SystemVssConnection,
                    Url = new Uri("https://dev.azure.com/example"),
                    Authorization = new EndpointAuthorization
                    {
                        Scheme = EndpointAuthorizationSchemes.OAuth,
                        Parameters = { [EndpointAuthorizationParameters.AccessToken] = secret }
                    }
                });
                var message = Pipelines.AgentJobRequestMessageUtil.Convert(new AgentJobRequestMessage(
                    new TaskOrchestrationPlanReference { PlanId = Guid.NewGuid(), PlanType = "Build" },
                    new TimelineReference { Id = Guid.NewGuid() },
                    Guid.NewGuid(), "job", "job", environment, new List<TaskInstance>()));
                message.Variables[Constants.Variables.System.ServerType] = new VariableValue("Hosted");
                var config = new Mock<IConfigurationStore>();
                config.Setup(x => x.GetSettings()).Returns(new AgentSettings { PoolId = 42, AgentId = 7 });
                hc.SetSingleton<IConfigurationStore>(config.Object);
                var agentServer = new Mock<IAgentServer>();
                hc.SetSingleton<IAgentServer>(agentServer.Object);
                hc.SetSingleton<IJobNotification>(new Mock<IJobNotification>().Object);
                var request = new TaskAgentJobRequest();
                typeof(TaskAgentJobRequest).GetProperty("LockedUntil", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    .SetValue(request, DateTime.UtcNow.AddMinutes(5));
                CancellationToken renewalToken = default;
                agentServer.Setup(x => x.RenewAgentRequestAsync(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                    .Returns((int poolId, long requestId, Guid lockToken, CancellationToken token) =>
                    {
                        renewalToken = token;
                        return Task.FromResult(request);
                    });
                var flags = new Mock<IFeatureFlagProvider>();
                hc.SetSingleton<IFeatureFlagProvider>(flags.Object);
                if (stage == "FeatureFlags")
                {
                    flags.Setup(x => x.GetFeatureFlagAsync(hc, It.IsAny<string>(), It.IsAny<ITraceWriter>(), It.IsAny<CancellationToken>()))
                        .ThrowsAsync(exception);
                }
                var processChannel = new Mock<IProcessChannel>();
                var processInvoker = new Mock<IProcessInvoker>();
                hc.EnqueueInstance<IProcessChannel>(processChannel.Object);
                hc.EnqueueInstance<IProcessInvoker>(processInvoker.Object);
                if (stage == "CreateProcessServices")
                {
                    processInvoker.Setup(x => x.Initialize(hc)).Throws(exception);
                }
                if (stage == "StartWorker" && !processFault)
                {
                    processChannel.Setup(x => x.StartServer(It.IsAny<StartProcessDelegate>(), It.IsAny<bool>())).Throws(exception);
                }
                else
                {
                    processChannel.Setup(x => x.StartServer(It.IsAny<StartProcessDelegate>(), It.IsAny<bool>()))
                        .Callback((StartProcessDelegate start, bool dispose) => start("1", "2"));
                    processInvoker.Setup(x => x.ExecuteAsync(
                        It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(),
                        It.IsAny<bool>(), It.IsAny<Encoding>(), It.IsAny<bool>(), It.IsAny<InputQueue<string>>(),
                        It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                        .Returns(delayedProcessFault ? processCompletion.Task : processFault ? Task.FromException<int>(exception) : Task.FromResult(0));
                    var send = processChannel.Setup(x => x.SendAsync(MessageType.NewJobRequest, It.IsAny<string>(), It.IsAny<CancellationToken>()));
                    if (delayedProcessFault)
                    {
                        send.Callback(() => processCompletion.SetException(exception));
                    }
                    send
                        .ThrowsAsync(processFault ? new OperationCanceledException("Pipe send timed out") : exception);
                }

                var jobServer = new Mock<IJobServer>();
                hc.SetSingleton<IJobServer>(jobServer.Object);
                var timeline = new Timeline(message.Timeline.Id);
                if (!missingRecord)
                {
                    timeline.Records.Add(new TimelineRecord { Id = message.JobId, RecordType = "Job", ErrorCount = 0, State = TimelineRecordState.InProgress });
                }
                TimelineRecord reportedRecord = null;
                jobServer.Setup(x => x.GetTimelineAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(timeline);
                jobServer.Setup(x => x.UpdateTimelineRecordsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
                    It.IsAny<IEnumerable<TimelineRecord>>(), It.IsAny<CancellationToken>()))
                    .Callback((Guid scope, string hub, Guid plan, Guid id, IEnumerable<TimelineRecord> records, CancellationToken token) =>
                    {
                        Assert.True(renewalToken.IsCancellationRequested);
                        Assert.True(token.CanBeCanceled);
                        reportedRecord = records.Single();
                    })
                    .ReturnsAsync(new List<TimelineRecord>());
                if (reportingFails)
                {
                    jobServer.Setup(x => x.GetTimelineAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                        .ThrowsAsync(new IOException("Timeline unavailable"));
                }

                Command telemetry = null;
                var publisher = new Mock<IAgenetListenerTelemetryPublisher>();
                publisher.Setup(x => x.PublishEvent(hc, It.IsAny<Command>(), It.IsAny<CancellationToken>()))
                    .Callback((IHostContext context, Command command, CancellationToken token) => telemetry = command)
                    .Returns(telemetryFails ? Task.FromException(new IOException("Telemetry unavailable")) : Task.CompletedTask);
                hc.SetSingleton<IAgenetListenerTelemetryPublisher>(publisher.Object);
                var dispatcher = new JobDispatcher();
                dispatcher.Initialize(hc);
                dispatcher.Run(message);
                var jobs = (System.Collections.IDictionary)typeof(JobDispatcher)
                    .GetField("_jobInfos", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dispatcher);
                var workerDispatch = jobs[message.JobId];
                var dispatchTask = (Task)workerDispatch.GetType().GetProperty("WorkerDispatch").GetValue(workerDispatch);
                var observedException = await Record.ExceptionAsync(() => dispatchTask.WaitAsync(TimeSpan.FromSeconds(10)));
                Assert.Same(exception, observedException);
                await dispatcher.WaitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

                Assert.True(renewalToken.IsCancellationRequested);
                Assert.NotNull(telemetry);
                Assert.Equal("AzurePipelinesAgent", telemetry.Properties["area"]);
                Assert.Equal("WorkerStartupFailure", telemetry.Properties["feature"]);
                var data = JObject.Parse(telemetry.Data);
                Assert.Equal(message.JobId.ToString(), data.Value<string>("JobId"));
                string expectedStage = delayedProcessFault ? "StartWorker" : stage;
                Assert.Equal(expectedStage, data.Value<string>("Stage"));
                Assert.Equal(exception.GetType().FullName, data.Value<string>("ExceptionType"));
                Assert.DoesNotContain(secret, telemetry.Data);
                if (!reportingFails)
                {
                    Assert.NotNull(reportedRecord);
                    var issue = Assert.Single(reportedRecord.Issues);
                    Assert.Equal(IssueType.Error, issue.Type);
                    Assert.Equal("AgentStartupFailure", issue.Category);
                    string expectedMessage = hc.SecretMasker.MaskSecrets(StringUtil.Loc("AgentWorkerStartupFailed",
                        expectedStage, message.JobId, message.RequestId, exception.ToString()));
                    Assert.Equal(expectedMessage, issue.Message);
                    Assert.DoesNotContain(secret, issue.Message);
                    Assert.Equal(1, reportedRecord.ErrorCount);
                    Assert.Null(reportedRecord.Result);
                    Assert.Equal(missingRecord ? (TimelineRecordState?)null : TimelineRecordState.InProgress, reportedRecord.State);
                }
                jobServer.Verify(x => x.ConnectAsync(It.IsAny<VssConnection>(), It.Is<CancellationToken>(token => token.CanBeCanceled)), Times.Once);
                jobServer.Verify(x => x.RaisePlanEventAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(),
                    It.IsAny<JobCompletedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
                agentServer.Verify(x => x.FinishAgentRequestAsync(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<Guid>(),
                    It.IsAny<DateTime>(), It.IsAny<TaskResult>(), It.IsAny<CancellationToken>()), Times.Never);
                Assert.Equal(localizationBefore, StringUtil.Loc("MissingNodePath"));
            }
        }
    }
}
