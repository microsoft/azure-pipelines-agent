// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.Telemetry;
using Microsoft.VisualStudio.Services.WebPlatform;
using Moq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker.Telemetry
{
    public class TelemetryCommandExtensionTests
    {
        private Mock<IExecutionContext> _ec;
        private List<string> _warnings = new List<string>();
        private List<string> _errors = new List<string>();
        private Mock<ICustomerIntelligenceServer> _mockCiService;
        private Mock<IAsyncCommandContext> _mockCommandContext;

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Telemetry")]
        public void PublishTelemetryCommandWithCiProps()
        {
            using (var _hc = SetupMocks())
            {
                var publishTelemetryCmd = new TelemetryCommandExtension();
                publishTelemetryCmd.Initialize(_hc);

                var data = new Dictionary<string, object>()
                {
                    {"key1", "valu\\e1"},
                    {"key2", "value2"},
                    {"key3", Int64.Parse("4") }
                };

                var json = JsonConvert.SerializeObject(data, Formatting.None);
                var cmd = new Command("telemetry", "publish");
                cmd.Data = json;
                cmd.Properties.Add("area", "Test");
                cmd.Properties.Add("feature", "Task");

                publishTelemetryCmd.ProcessCommand(_ec.Object, cmd);
                _mockCiService.Verify(s => s.PublishEventsAsync(It.Is<CustomerIntelligenceEvent[]>(e => VerifyEvent(e, data))), Times.Once());
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Telemetry")]
        public void PublishTelemetryCommandWithSpecialCiProps()
        {
            using (var _hc = SetupMocks())
            {
                var publishTelemetryCmd = new TelemetryCommandExtension();
                publishTelemetryCmd.Initialize(_hc);

                var data = new Dictionary<string, object>()
                {
                    {"key1", "va@lu;çe1"}
                };

                var json = JsonConvert.SerializeObject(data, Formatting.None);
                var cmd = new Command("telemetry", "publish");
                cmd.Data = json;
                cmd.Properties.Add("area", "Test");
                cmd.Properties.Add("feature", "Task");

                publishTelemetryCmd.ProcessCommand(_ec.Object, cmd);
                _mockCiService.Verify(s => s.PublishEventsAsync(It.Is<CustomerIntelligenceEvent[]>(e => VerifyEvent(e, data))), Times.Once());
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Telemetry")]
        public void PublishTelemetryWithoutArea()
        {
            using (var _hc = SetupMocks())
            {
                var publishTelemetryCmd = new TelemetryCommandExtension();
                publishTelemetryCmd.Initialize(_hc);

                var cmd = new Command("telemetry", "publish");
                cmd.Data = "key1=value1;key2=value2";
                cmd.Properties.Add("feature", "Task");

                Assert.Throws<ArgumentException>(() => publishTelemetryCmd.ProcessCommand(_ec.Object, cmd));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Telemetry")]
        public void PublishTelemetryWithoutFeature()
        {
            using (var _hc = SetupMocks())
            {
                var publishTelemetryCmd = new TelemetryCommandExtension();
                publishTelemetryCmd.Initialize(_hc);

                var cmd = new Command("telemetry", "publish");
                cmd.Data = "key1=value1;key2=value2";
                cmd.Properties.Add("area", "Test");

                Assert.Throws<ArgumentException>(() => publishTelemetryCmd.ProcessCommand(_ec.Object, cmd));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Telemetry")]
        public void PublishTelemetryWithoutCiData()
        {
            using (var _hc = SetupMocks())
            {
                var publishTelemetryCmd = new TelemetryCommandExtension();
                publishTelemetryCmd.Initialize(_hc);

                var cmd = new Command("telemetry", "publish");
                cmd.Properties.Add("area", "Test");
                cmd.Properties.Add("feature", "Task");

                Assert.Throws<ArgumentException>(() => publishTelemetryCmd.ProcessCommand(_ec.Object, cmd));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Telemetry")]
        public void PublishTelemetryWithoutCommandEvent()
        {
            using (var _hc = SetupMocks())
            {
                var publishTelemetryCmd = new TelemetryCommandExtension();
                publishTelemetryCmd.Initialize(_hc);

                var cmd = new Command("telemetry", "abcxyz");
                cmd.Properties.Add("area", "Test");
                cmd.Properties.Add("feature", "Task");

                var ex = Assert.Throws<Exception>(() => publishTelemetryCmd.ProcessCommand(_ec.Object, cmd));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Telemetry")]
        public void PublishTelemetryCommandWithExceptionFromServer()
        {
            using (var _hc = SetupMocks())
            {
                _mockCiService.Setup(x => x.PublishEventsAsync(It.IsAny<CustomerIntelligenceEvent[]>())).Throws<Exception>();

                var publishTelemetryCmd = new TelemetryCommandExtension();
                publishTelemetryCmd.Initialize(_hc);

                var data = new Dictionary<string, object>()
                {
                    {"key1", "valu\\e1"},
                    {"key2", "value2"},
                    {"key3", Int64.Parse("4") }
                };

                var json = JsonConvert.SerializeObject(data, Formatting.None);
                var cmd = new Command("telemetry", "publish");
                cmd.Data = json;
                cmd.Properties.Add("area", "Test");
                cmd.Properties.Add("feature", "Task");

                publishTelemetryCmd.ProcessCommand(_ec.Object, cmd);
                _mockCiService.Verify(s => s.PublishEventsAsync(It.Is<CustomerIntelligenceEvent[]>(e => VerifyEvent(e, data))), Times.Once());
                Assert.True(_warnings.Count > 0);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Telemetry")]
        public void PublishTelemetryCommandForInBoxTask()
        {
            using (var _hc = SetupMocks())
            {
                var ex_context = _ec.Object;
                ex_context.Variables.Set(Constants.Variables.Task.PublishTelemetry, true.ToString());

                var publishTelemetryCmd = new TelemetryCommandExtension();
                publishTelemetryCmd.Initialize(_hc);

                var data = new Dictionary<string, object>()
                {
                    {"key1", "valu\\e1"},
                    {"key2", "value2"},
                    {"key3", Int64.Parse("4") }
                };

                var json = JsonConvert.SerializeObject(data, Formatting.None);
                var cmd = new Command("telemetry", "publish");
                cmd.Data = json;
                cmd.Properties.Add("area", "Test");
                cmd.Properties.Add("feature", "Task");

                publishTelemetryCmd.ProcessCommand(ex_context, cmd);
                _mockCiService.Verify(s => s.PublishEventsAsync(It.Is<CustomerIntelligenceEvent[]>(e => VerifyEvent(e, data))), Times.Once);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Telemetry")]
        public void PublishTelemetryCommandForCustomerTask()
        {
            using (var _hc = SetupMocks())
            {
                var ex_context = _ec.Object;
                ex_context.Variables.Set(Constants.Variables.Task.PublishTelemetry, false.ToString());

                var publishTelemetryCmd = new TelemetryCommandExtension();
                publishTelemetryCmd.Initialize(_hc);

                var data = new Dictionary<string, object>()
                {
                    {"key1", "valu\\e1"},
                    {"key2", "value2"},
                    {"key3", Int64.Parse("4") }
                };

                var json = JsonConvert.SerializeObject(data, Formatting.None);
                var cmd = new Command("telemetry", "publish");
                cmd.Data = json;
                cmd.Properties.Add("area", "Test");
                cmd.Properties.Add("feature", "Task");

                publishTelemetryCmd.ProcessCommand(ex_context, cmd);
                _mockCiService.Verify(s => s.PublishEventsAsync(It.Is<CustomerIntelligenceEvent[]>(e => VerifyEvent(e, data))), Times.Never);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Telemetry")]
        public void PublishTelemetryCommand_WhenEnforceContainerVsoPathValidationOff_DoesNotAwaitPublish()
        {
            // Flag-off regression guard: must preserve the original fire-and-forget behavior —
            // the publish task is still kicked off (ciService.PublishEventsAsync is still called),
            // but it is NOT registered in AsyncCommands, exactly as before the reliability fix.
            using (var _hc = SetupMocks())
            {
                var ex_context = _ec.Object;
                ex_context.Variables.Set("DistributedTask.Agent.EnforceContainerVsoPathValidation", false.ToString());
                _ec.Setup(x => x.GetVariableValueOrDefault(It.IsAny<string>()))
                    .Returns<string>(name => ex_context.Variables.Get(name));

                var publishTelemetryCmd = new TelemetryCommandExtension();
                publishTelemetryCmd.Initialize(_hc);

                var data = new Dictionary<string, object>() { { "key1", "value1" } };
                var cmd = new Command("telemetry", "publish");
                cmd.Data = JsonConvert.SerializeObject(data, Formatting.None);
                cmd.Properties.Add("area", "Test");
                cmd.Properties.Add("feature", "Task");

                publishTelemetryCmd.ProcessCommand(ex_context, cmd);

                Assert.Empty(ex_context.AsyncCommands);
                _mockCiService.Verify(s => s.PublishEventsAsync(It.Is<CustomerIntelligenceEvent[]>(e => VerifyEvent(e, data))), Times.Once);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Telemetry")]
        public void PublishTelemetryCommand_WhenEnforceContainerVsoPathValidationOn_RegistersAsyncCommandSoPublishIsAwaited()
        {
            // Flag-on: the publish task must be registered in AsyncCommands so the job waits for
            // it (including BlockedCount/BlockedSamples) to complete before the worker exits.
            using (var _hc = SetupMocks())
            {
                var ex_context = _ec.Object;
                ex_context.Variables.Set("DistributedTask.Agent.EnforceContainerVsoPathValidation", true.ToString());
                _ec.Setup(x => x.GetVariableValueOrDefault(It.IsAny<string>()))
                    .Returns<string>(name => ex_context.Variables.Get(name));

                var publishTelemetryCmd = new TelemetryCommandExtension();
                publishTelemetryCmd.Initialize(_hc);

                var data = new Dictionary<string, object>() { { "key1", "value1" } };
                var cmd = new Command("telemetry", "publish");
                cmd.Data = JsonConvert.SerializeObject(data, Formatting.None);
                cmd.Properties.Add("area", "Test");
                cmd.Properties.Add("feature", "Task");

                publishTelemetryCmd.ProcessCommand(ex_context, cmd);

                Assert.Single(ex_context.AsyncCommands);
                _mockCiService.Verify(s => s.PublishEventsAsync(It.Is<CustomerIntelligenceEvent[]>(e => VerifyEvent(e, data))), Times.Once);
            }
        }


        private TestHostContext SetupMocks([CallerMemberName] string name = "")
        {
            var _hc = new TestHostContext(this, name);
            _hc.SetSingleton(new TaskRestrictionsChecker() as ITaskRestrictionsChecker);

            _mockCiService = new Mock<ICustomerIntelligenceServer>();
            _hc.SetSingleton(_mockCiService.Object);

            _mockCommandContext = new Mock<IAsyncCommandContext>();
            _hc.EnqueueInstance(_mockCommandContext.Object);

            var endpointAuthorization = new EndpointAuthorization()
            {
                Scheme = EndpointAuthorizationSchemes.OAuth
            };
            List<string> warnings;
            var variables = new Variables(_hc, new Dictionary<string, VariableValue>(), out warnings);
            endpointAuthorization.Parameters[EndpointAuthorizationParameters.AccessToken] = "accesstoken";

            _ec = new Mock<IExecutionContext>();
            _ec.Setup(x => x.Restrictions).Returns(new List<TaskRestrictions>());
            _ec.Setup(x => x.Endpoints).Returns(new List<ServiceEndpoint> { new ServiceEndpoint { Url = new Uri("http://dummyurl"), Name = WellKnownServiceEndpointNames.SystemVssConnection, Authorization = endpointAuthorization } });
            _ec.Setup(x => x.Variables).Returns(variables);
            var asyncCommands = new List<IAsyncCommandContext>();
            _ec.Setup(x => x.AsyncCommands).Returns(asyncCommands);
            // Required by AgentKnobs.EnforceContainerVsoPathValidation.GetValue(context) in
            // PublishEvent (EnvironmentKnobSource falls back to context.GetScopedEnvironment()) —
            // without this, every test here throws NullReferenceException, not just the new ones.
            _ec.Setup(x => x.GetScopedEnvironment()).Returns(new SystemEnvironment());
            _ec.Setup(x => x.AddIssue(It.IsAny<Issue>()))
            .Callback<Issue>
            ((issue) =>
            {
                if (issue.Type == IssueType.Warning)
                {
                    _warnings.Add(issue.Message);
                }
                else if (issue.Type == IssueType.Error)
                {
                    _errors.Add(issue.Message);
                }
            });
            _ec.Setup(x => x.GetHostContext()).Returns(_hc);

            return _hc;
        }

        private bool VerifyEvent(CustomerIntelligenceEvent[] ciEvent, Dictionary<string, object> eventData)
        {
            Assert.True(ciEvent.Length == 1);
            Assert.True(ciEvent[0].Properties.Count == eventData.Count);
            foreach (var key in eventData.Keys)
            {
                object eventVal;
                object ciVal;
                eventData.TryGetValue(key, out eventVal);
                ciEvent[0].Properties.TryGetValue(key, out ciVal);
                Assert.True(eventVal.Equals(ciVal), "CI properties didn't match");
            }
            return true;
        }
    }
}
