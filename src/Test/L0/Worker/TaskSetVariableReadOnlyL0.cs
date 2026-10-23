using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agent.Sdk.Knob;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.Handlers;
using Moq;
using Xunit;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker
{
    [Collection("Worker proxy environment tests")]
    public sealed class TaskSetVariableReadOnlyL0 : IDisposable
    {
        private readonly string _previousEnvironmentValue = Environment.GetEnvironmentVariable(AgentKnobs.ProtectReadOnlyVariableNamesEnvironmentVariable);
        private readonly TestHostContext _host;
        private readonly Mock<IJobServerQueue> _queue = new Mock<IJobServerQueue>();
        private readonly List<TimelineRecord> _records = new List<TimelineRecord>();
        private readonly Agent.Worker.ExecutionContext _job = new Agent.Worker.ExecutionContext();
        private Agent.Worker.ExecutionContext _task;
        private TimelineRecord _taskRecord;

        public TaskSetVariableReadOnlyL0()
        {
            _host = new TestHostContext(this);
            _host.SetSingleton<IJobServerQueue>(_queue.Object);
            _host.SetSingleton<ITaskRestrictionsChecker>(new TaskRestrictionsChecker());
            _host.EnqueueInstance<IPagingLogger>(new Mock<IPagingLogger>().Object);
            _host.EnqueueInstance<IPagingLogger>(new Mock<IPagingLogger>().Object);
            _queue.Setup(x => x.QueueTimelineRecordUpdate(It.IsAny<Guid>(), It.IsAny<TimelineRecord>()))
                .Callback((Guid timeline, TimelineRecord record) => _records.Add(record));
        }

        [Theory]
        [InlineData("System.OidcRequestUri", "System.OidcRequestUri", false)]
        [InlineData("System.OidcRequestUri", "SYSTEM_OIDCREQUESTURI", false)]
        [InlineData("System.OidcRequestUri", "system_oidcrequesturi", true)]
        [InlineData("Custom.ReadOnly.Value", "Custom ReadOnly Value", true)]
        [InlineData("System.AccessToken", "SYSTEM_ACCESSTOKEN", false)]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Enabled_RejectsProtectedNamesWithoutSideEffects(string protectedName, string candidate, bool requestedFlags)
        {
            CreateContext("true", legacyFlag: false, protectedName: protectedName);
            var command = CreateCommand(candidate, secret: requestedFlags, preserveCase: requestedFlags, readOnly: requestedFlags);

            AssertRejected(command, protectedName, candidate);
            Assert.Equal("replacement", _host.SecretMasker.MaskSecrets("replacement"));
        }

        [Theory]
        [InlineData("System", "OIDCREQUESTURI", "System.OidcRequestUri", false)]
        [InlineData("system", "OidcRequestUri", "SYSTEM_OIDCREQUESTURI", false)]
        [InlineData("System", "OIDCREQUESTURI", "System.OidcRequestUri", true)]
        [InlineData("Build", "Result.Value", "BUILD_RESULT_VALUE", true)]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Enabled_RejectsFinalOutputDestinationBeforePublishing(string refName, string name, string protectedName, bool declared)
        {
            CreateContext("true", refName, false, protectedName);
            if (declared)
            {
                _task.OutputVariables.Add(name);
            }
            var command = CreateCommand(name, output: !declared, secret: true, preserveCase: true, readOnly: true);

            AssertRejected(command, protectedName, $"{refName}.{name}");
            Assert.Null(_task.Variables.Get(name));
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData(null, true)]
        [InlineData("false", false)]
        [InlineData("false", true)]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Disabled_PreservesLegacyDirectReadOnlyBehavior(string flag, bool legacyFlag)
        {
            const string name = "Custom.ReadOnly";
            CreateContext(flag, legacyFlag: legacyFlag, protectedName: name);
            var command = CreateCommand(name);
            if (legacyFlag)
            {
                AssertRejected(command, name, name);
            }
            else
            {
                new TaskSetVariableCommand().Execute(_task, command);
                Assert.Equal("replacement", _task.Variables.Get(name));
                Assert.True(_task.Variables.IsReadOnly(name));
                Assert.Equal(1, _taskRecord.WarningCount);
                Assert.Contains(_taskRecord.Issues, x => x.Message.Contains("ReadOnlyVariableWarning"));
                AssertNoOutputVariables();
            }
        }

        [Theory]
        [InlineData(null, true)]
        [InlineData("false", false)]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Disabled_PreservesAliasesAndOutputPrefixBehavior(string flag, bool legacyFlag)
        {
            CreateContext(flag, legacyFlag: legacyFlag, protectedName: "System.OidcRequestUri");
            new TaskSetVariableCommand().Execute(_task, CreateCommand("SYSTEM_OIDCREQUESTURI"));
            Assert.Equal("original", _task.Variables.Get("System.OidcRequestUri"));
            Assert.Equal("replacement", _task.Variables.Get("SYSTEM_OIDCREQUESTURI"));

            new TaskSetVariableCommand().Execute(_task, CreateCommand("OIDCREQUESTURI", output: true));
            Assert.Equal("replacement", _task.Variables.Get("System.OidcRequestUri"));
            Assert.Equal("replacement", _taskRecord.Variables["OIDCREQUESTURI"].Value);

            _task.Variables.Set("System.Declared", "original", readOnly: true);
            _task.OutputVariables.Add("Declared");
            new TaskSetVariableCommand().Execute(_task, CreateCommand("Declared"));
            Assert.Equal("replacement", _task.Variables.Get("System.Declared"));
            Assert.Equal("replacement", _taskRecord.Variables["Declared"].Value);
            Assert.Equal(0, _taskRecord.WarningCount);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, true)]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Enabled_AllowsUnrelatedPrefixedRawAlias(bool declared, bool secret)
        {
            CreateContext("true", "Build", protectedName: "System.OidcRequestUri");
            const string name = "SYSTEM_OIDCREQUESTURI";
            if (declared)
            {
                _task.OutputVariables.Add(name);
            }

            Assert.Equal("Build." + name, _task.GetVariableStorageName(name, !declared));
            Assert.Empty(_records);
            AssertNoOutputVariables();

            new TaskSetVariableCommand().Execute(_task, CreateCommand(name, output: !declared, secret: secret, preserveCase: true));

            Assert.Equal("original", _task.Variables.Get("System.OidcRequestUri"));
            Assert.Null(_task.Variables.Get(name));
            Assert.Equal("replacement", _task.Variables.Get("Build." + name));
            Assert.Equal(secret, _taskRecord.Variables[name].IsSecret);
            Assert.Single(_records);
        }

        [Theory]
        [InlineData(true, "Publisher.Result")]
        [InlineData(true, "PUBLISHER_RESULT")]
        [InlineData(false, "Publisher.Result")]
        [InlineData(false, "PUBLISHER_RESULT")]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void DeclaredOutputs_RespectReadOnlyProtectionAcrossTasks(bool protectReadOnlyVariableNames, string destination)
        {
            CreateContext(protectReadOnlyVariableNames.ToString(), refName: "Publisher");
            _task.OutputVariables.Add("Result");
            var publish = CreateCommand("Result");
            publish.Data = "published";
            new TaskSetVariableCommand().Execute(_task, publish);

            var output = _job.Variables.Public.Single(x => x.Name == "Publisher.Result");
            Assert.Equal("published", output.Value);
            Assert.Equal(protectReadOnlyVariableNames, output.ReadOnly);
            Assert.Null(_job.Variables.Get("Result"));
            Assert.Equal("published", _taskRecord.Variables["Result"].Value);
            Assert.Single(_records);

            _host.EnqueueInstance<IPagingLogger>(new Mock<IPagingLogger>().Object);
            var taskVariables = new Variables(_host, new Dictionary<string, VariableValue>(), out _);
            using (var consumer = (Agent.Worker.ExecutionContext)_job.CreateChild(Guid.NewGuid(), "consumer", "Consumer", taskVariables))
            {
                Assert.Empty(consumer.OutputVariables);
                var consumerRecord = _records.Last(x => x.Id == consumer.Id);
                _records.Clear();
                var command = CreateCommand(destination);

                if (protectReadOnlyVariableNames)
                {
                    var before = _job.Variables.Public.Concat(_job.Variables.Private)
                        .OrderBy(x => x.Name, StringComparer.Ordinal)
                        .Select(x => (x.Name, x.Value, x.Secret, x.ReadOnly, x.PreserveCase)).ToArray();

                    Assert.Throws<InvalidOperationException>(() => new TaskSetVariableCommand().Execute(consumer, command));

                    Assert.Equal(before, _job.Variables.Public.Concat(_job.Variables.Private)
                        .OrderBy(x => x.Name, StringComparer.Ordinal)
                        .Select(x => (x.Name, x.Value, x.Secret, x.ReadOnly, x.PreserveCase)).ToArray());
                }
                else
                {
                    new TaskSetVariableCommand().Execute(consumer, command);

                    Assert.Equal("replacement", _job.Variables.Get(destination));
                    Assert.False(_job.Variables.IsReadOnly(destination));
                    Assert.Equal(destination == "Publisher.Result" ? "replacement" : "published", _job.Variables.Get("Publisher.Result"));
                }

                Assert.Empty(_records);
                Assert.Single(consumerRecord.Variables);
                Assert.Equal(BuildConstants.AgentPackage.Version, consumerRecord.Variables[TaskWellKnownItems.AgentVersionTimelineVariable].Value);
                Assert.Equal("published", _taskRecord.Variables["Result"].Value);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void ExplicitOutputs_RepeatedPublicationRespectsProtection(bool protectReadOnlyVariableNames)
        {
            CreateContext(protectReadOnlyVariableNames.ToString(), refName: "Publisher");
            var publishedValues = new List<string>();
            _queue.Setup(x => x.QueueTimelineRecordUpdate(It.IsAny<Guid>(), It.Is<TimelineRecord>(r => r.Id == _task.Id)))
                .Callback((Guid timeline, TimelineRecord record) =>
                {
                    _records.Add(record);
                    publishedValues.Add(record.Variables["Result"].Value);
                });
            var first = CreateCommand("Result", output: true);
            first.Data = "FIRST";
            new TaskSetVariableCommand().Execute(_task, first);

            Assert.Equal("FIRST", _job.Variables.Get("Publisher.Result"));
            Assert.True(_job.Variables.IsReadOnly("Publisher.Result"));
            Assert.Equal("FIRST", _taskRecord.Variables["Result"].Value);
            Assert.Equal(new[] { "FIRST" }, publishedValues);

            var second = CreateCommand("Result", output: true);
            second.Data = "SECOND";
            if (protectReadOnlyVariableNames)
            {
                var error = Assert.Throws<InvalidOperationException>(() => new TaskSetVariableCommand().Execute(_task, second));
                Assert.Equal(StringUtil.Loc("ReadOnlyVariable", "Publisher.Result"), error.Message);
            }
            else
            {
                new TaskSetVariableCommand().Execute(_task, second);
            }

            var expected = protectReadOnlyVariableNames ? "FIRST" : "SECOND";
            Assert.Equal(expected, _job.Variables.Get("Publisher.Result"));
            Assert.Equal(expected, _taskRecord.Variables["Result"].Value);
            Assert.True(_job.Variables.IsReadOnly("Publisher.Result"));
            Assert.Null(_job.Variables.Get("Result"));
            Assert.Equal(protectReadOnlyVariableNames ? new[] { "FIRST" } : new[] { "FIRST", "SECOND" }, publishedValues);
            Assert.Equal(publishedValues.Count, _records.Count);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task TaskRunner_DeclaredOutputLifecycleRespectsProtection(bool protectReadOnlyVariableNames)
        {
            CreateContext(protectReadOnlyVariableNames.ToString(), refName: "Publisher");
            _task.Variables.Set("DistributedTask.Agent.USENEWNODEHANDLERTELEMETRY", "true");
            _host.SetSingleton<ITaskManager>(new TaskManager());
            _host.SetSingleton<IHandlerFactory>(new HandlerFactory());
            _host.SetSingleton<ITaskDecoratorManager>(new Mock<ITaskDecoratorManager>().Object);
            _host.SetSingleton<IResourceMetricsManager>(new Mock<IResourceMetricsManager>().Object);
            _host.EnqueueInstance<IDefaultStepHost>(new Mock<IDefaultStepHost>().Object);
            var handler = new Mock<INodeHandler>();
            handler.SetupAllProperties();
            handler.Setup(x => x.RunAsync()).Returns(() =>
            {
                Assert.Same(_task, handler.Object.ExecutionContext);
                Assert.Single(_task.OutputVariables);
                Assert.Contains("Result", _task.OutputVariables);
                var command = CreateCommand("Result");
                command.Data = "FIRST";
                new TaskSetVariableCommand().Execute(handler.Object.ExecutionContext, command);
                return Task.CompletedTask;
            });
            _host.EnqueueInstance<INodeHandler>(handler.Object);
            var task = new Pipelines.TaskStep
            {
                Id = _task.Id,
                Name = "Publisher",
                DisplayName = "Publisher",
                IsServerOwned = false,
                Reference = new Pipelines.TaskStepDefinitionReference
                {
                    Id = Guid.NewGuid(),
                    Name = "DeclaredOutput",
                    Version = "1.0.0"
                }
            };
            var workDirectory = _host.GetDirectory(WellKnownDirectory.Work);
            var taskDirectory = Path.Combine(_host.GetDirectory(WellKnownDirectory.Tasks), $"{task.Reference.Name}_{task.Reference.Id}", task.Reference.Version);
            try
            {
                Directory.CreateDirectory(taskDirectory);
                File.WriteAllText(Path.Combine(taskDirectory, Constants.Path.TaskJsonFile), $@"{{
                    ""id"": ""{task.Reference.Id}"",
                    ""name"": ""DeclaredOutput"",
                    ""friendlyName"": ""Declared output lifecycle"",
                    ""version"": {{ ""Major"": 1, ""Minor"": 0, ""Patch"": 0 }},
                    ""outputVariables"": [{{ ""name"": ""Result"" }}],
                    ""execution"": {{ ""Node20_1"": {{ ""target"": ""publish.js"" }} }}
                }}");
                var runner = new TaskRunner { ExecutionContext = _task, Task = task, Stage = JobRunStage.Main };
                runner.Initialize(_host);
                Assert.Empty(_task.OutputVariables);

                await runner.RunAsync();

                handler.Verify(x => x.RunAsync(), Times.Once);
                var output = _job.Variables.Public.Single(x => x.Name == "Publisher.Result");
                Assert.Equal("FIRST", output.Value);
                Assert.Equal(protectReadOnlyVariableNames, output.ReadOnly);
                Assert.Null(_job.Variables.Get("Result"));
                Assert.Equal("FIRST", _taskRecord.Variables["Result"].Value);
                Assert.Single(_records);

                _host.EnqueueInstance<IPagingLogger>(new Mock<IPagingLogger>().Object);
                var taskVariables = new Variables(_host, new Dictionary<string, VariableValue>(), out _);
                using (var consumer = (Agent.Worker.ExecutionContext)_job.CreateChild(Guid.NewGuid(), "consumer", "Consumer", taskVariables))
                {
                    Assert.Empty(consumer.OutputVariables);
                    var consumerRecord = _records.Last(x => x.Id == consumer.Id);
                    _records.Clear();
                    var overwrite = CreateCommand("Publisher.Result");
                    overwrite.Data = "SECOND";
                    if (protectReadOnlyVariableNames)
                    {
                        var error = Assert.Throws<InvalidOperationException>(() => new TaskSetVariableCommand().Execute(consumer, overwrite));
                        Assert.Equal(StringUtil.Loc("ReadOnlyVariable", "Publisher.Result"), error.Message);
                    }
                    else
                    {
                        new TaskSetVariableCommand().Execute(consumer, overwrite);
                    }

                    Assert.Equal(protectReadOnlyVariableNames ? "FIRST" : "SECOND", _job.Variables.Get("Publisher.Result"));
                    Assert.Equal("FIRST", _taskRecord.Variables["Result"].Value);
                    Assert.Empty(_records);
                    Assert.Single(consumerRecord.Variables);
                    Assert.Equal(BuildConstants.AgentPackage.Version, consumerRecord.Variables[TaskWellKnownItems.AgentVersionTimelineVariable].Value);
                }
            }
            finally
            {
                if (Directory.Exists(workDirectory))
                {
                    Directory.Delete(workDirectory, recursive: true);
                }
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Enabled_StillRejectsRawExactReadOnlyNameForOutputs()
        {
            const string name = "System.OidcRequestUri";
            CreateContext("true", "Build", false, name);

            AssertRejected(CreateCommand(name, output: true), name, name);
            Assert.Null(_task.Variables.Get("Build." + name));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Enabled_InvalidOutputReferenceHasNoSideEffects()
        {
            CreateContext("true", refName: null);

            Assert.Throws<ArgumentNullException>(() => new TaskSetVariableCommand().Execute(_task, CreateCommand("Result", output: true)));
            Assert.Empty(_records);
            AssertNoOutputVariables();
            Assert.Null(_task.Variables.Get("Result"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Enabled_PreservesWritableAndReadOnlyCreationSemantics()
        {
            CreateContext("true");
            _task.Variables.Set("Custom.Value", "original");
            new TaskSetVariableCommand().Execute(_task, CreateCommand("Custom.Value"));
            new TaskSetVariableCommand().Execute(_task, CreateCommand("CUSTOM_VALUE"));
            new TaskSetVariableCommand().Execute(_task, CreateCommand("New.ReadOnly", secret: true, preserveCase: true, readOnly: true));
            var stored = _task.Variables.Public.Concat(_task.Variables.Private).Single(x => x.Name == "New.ReadOnly");

            Assert.True(stored.Secret);
            Assert.True(stored.PreserveCase);
            Assert.True(stored.ReadOnly);
            Assert.Equal("replacement", stored.Value);
            Assert.Equal("replacement", _task.Variables.Get("Custom.Value"));
            Assert.Equal("replacement", _task.Variables.Get("CUSTOM_VALUE"));
            Assert.Equal("***", _host.SecretMasker.MaskSecrets("replacement"));
            Assert.Throws<InvalidOperationException>(() => new TaskSetVariableCommand().Execute(_task, CreateCommand("NEW_READONLY")));
            Assert.Empty(_records);
            AssertNoOutputVariables();
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Enabled_CannotBeDisabledBySetVariable()
        {
            CreateContext("true", protectedName: "System.OidcRequestUri");
            var command = CreateCommand(AgentKnobs.ProtectReadOnlyVariableNamesEnvironmentVariable);
            command.Data = "false";
            new TaskSetVariableCommand().Execute(_task, command);

            Assert.Equal("false", _task.Variables.Get(AgentKnobs.ProtectReadOnlyVariableNamesEnvironmentVariable));
            Assert.True(AgentKnobs.ProtectReadOnlyVariableNames.GetValue(_task).AsBoolean());
            Assert.True(_task.ProtectReadOnlyVariableNames);
            AssertRejected(CreateCommand("SYSTEM_OIDCREQUESTURI"), "System.OidcRequestUri", "SYSTEM_OIDCREQUESTURI");
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Enabled_LaterTasksKeepJobStartDecision()
        {
            CreateContext("true", protectedName: "System.OidcRequestUri");
            var command = CreateCommand(AgentKnobs.ProtectReadOnlyVariableNamesFeatureFlag);
            command.Data = "false";
            new TaskSetVariableCommand().Execute(_task, command);

            Assert.False(AgentKnobs.ProtectReadOnlyVariableNames.GetValue(_task).AsBoolean());
            Assert.True(_task.ProtectReadOnlyVariableNames);
            AssertRejected(CreateCommand("SYSTEM_OIDCREQUESTURI"), "System.OidcRequestUri", "SYSTEM_OIDCREQUESTURI");

            _host.EnqueueInstance<IPagingLogger>(new Mock<IPagingLogger>().Object);
            var taskVariables = new Variables(_host, new Dictionary<string, VariableValue>(), out _);
            using (var laterTask = (Agent.Worker.ExecutionContext)_job.CreateChild(Guid.NewGuid(), "later", "Later", taskVariables))
            {
                Assert.True(laterTask.ProtectReadOnlyVariableNames);
                Assert.Throws<InvalidOperationException>(() => new TaskSetVariableCommand().Execute(laterTask, CreateCommand("SYSTEM_OIDCREQUESTURI")));
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Enabled_TaskVariablesRemainIsolatedWithLegacyReadOnlyPolicy(bool legacyFlag)
        {
            CreateContext("true", legacyFlag: legacyFlag, protectedName: "System.OidcRequestUri");
            var setter = new TaskSetTaskVariableCommand();
            setter.Execute(_task, CreateCommand("System.OidcRequestUri"));
            setter.Execute(_task, CreateCommand("SYSTEM_OIDCREQUESTURI"));
            Assert.Equal("original", _task.Variables.Get("System.OidcRequestUri"));
            Assert.Null(_task.Variables.Get("SYSTEM_OIDCREQUESTURI"));
            Assert.Equal("replacement", _task.TaskVariables.Get("System.OidcRequestUri"));

            _task.TaskVariables.Set("Task.ReadOnly", "original", readOnly: true);
            setter.Execute(_task, CreateCommand("TASK_READONLY"));
            Assert.Equal("replacement", _task.TaskVariables.Get("TASK_READONLY"));
            if (legacyFlag)
            {
                Assert.Throws<InvalidOperationException>(() => setter.Execute(_task, CreateCommand("Task.ReadOnly")));
                Assert.Equal("original", _task.TaskVariables.Get("Task.ReadOnly"));
            }
            else
            {
                setter.Execute(_task, CreateCommand("Task.ReadOnly"));
                Assert.Equal("replacement", _task.TaskVariables.Get("Task.ReadOnly"));
                Assert.Contains(_taskRecord.Issues, x => x.Message.Contains("ReadOnlyTaskVariableWarning"));
            }
            AssertNoOutputVariables();
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Enabled_InternalSettersKeepExactWriteSemantics()
        {
            CreateContext("true", protectedName: "System.OidcRequestUri");
            Assert.True(_task.ProtectReadOnlyVariableNames);
            _task.Variables.Set("System.OidcRequestUri", "internal store");
            Assert.Equal("internal store", _task.Variables.Get("System.OidcRequestUri"));
            _task.SetVariable("System.OidcRequestUri", "internal context");
            Assert.Equal("internal context", _task.Variables.Get("System.OidcRequestUri"));
            Assert.True(_task.Variables.IsReadOnly("System.OidcRequestUri"));
            _task.SetVariable("OIDCREQUESTURI", "internal output", isOutput: true);
            Assert.Equal("internal output", _task.Variables.Get("System.OidcRequestUri"));
            Assert.Equal("internal output", _taskRecord.Variables["OIDCREQUESTURI"].Value);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Enabled_PreservesRestrictedModeAndAllowlist()
        {
            CreateContext("true", protectedName: "System.OidcRequestUri");
            _task.SetStepTarget(new Pipelines.StepTarget { Target = "host", Commands = "restricted" });
            var restrictions = new TaskRestrictions { SettableVariables = new TaskVariableRestrictions() };
            restrictions.SettableVariables.Allowed.Add("Allowed");
            restrictions.SettableVariables.Allowed.Add("SYSTEM_OIDCREQUESTURI");
            _task.Restrictions.Add(restrictions);
            var extension = new TaskCommandExtension();
            extension.Initialize(_host);

            extension.ProcessCommand(_task, CreateCommand("Allowed"));
            extension.ProcessCommand(_task, CreateCommand("Denied"));
            Assert.Equal("replacement", _task.Variables.Get("Allowed"));
            Assert.Null(_task.Variables.Get("Denied"));
            Assert.Contains(_taskRecord.Issues, x => x.Message.Contains("SetVariableNotAllowed"));
            Assert.Throws<InvalidOperationException>(() => extension.ProcessCommand(_task, CreateCommand("SYSTEM_OIDCREQUESTURI")));
            Assert.Equal("original", _task.Variables.Get("System.OidcRequestUri"));
            Assert.Null(_task.Variables.Get("SYSTEM_OIDCREQUESTURI"));
            AssertNoOutputVariables();
        }

        private void CreateContext(string flag, string refName = "System", bool legacyFlag = true, string protectedName = null)
        {
            Environment.SetEnvironmentVariable(AgentKnobs.ProtectReadOnlyVariableNamesEnvironmentVariable, flag);
            var variables = new Dictionary<string, VariableValue>
            {
                [Constants.Variables.Agent.ReadOnlyVariables] = legacyFlag.ToString(),
                ["AGENT_ENABLE_IMMEDIATE_TIMELINE_RECORD_UPDATES"] = "false"
            };
            if (protectedName != null)
            {
                variables[protectedName] = new VariableValue("original")
                {
                    IsReadOnly = !string.Equals(protectedName, Constants.Variables.System.AccessToken, StringComparison.OrdinalIgnoreCase)
                };
            }
            var message = new Pipelines.AgentJobRequestMessage(
                new TaskOrchestrationPlanReference(), new TimelineReference(), Guid.NewGuid(), "job", "job",
                null, new Dictionary<string, string>(), variables, new List<MaskHint>(),
                new Pipelines.JobResources(), new Pipelines.WorkspaceOptions(), new List<Pipelines.JobStep>());
            _job.Initialize(_host);
            _job.InitializeJob(message, CancellationToken.None);
            var taskVariables = new Variables(_host, new Dictionary<string, VariableValue>(), out _);
            _task = (Agent.Worker.ExecutionContext)_job.CreateChild(Guid.NewGuid(), "task", refName, taskVariables);
            _taskRecord = _records.Last(x => x.Id == _task.Id);
            _records.Clear();
        }

        private void AssertRejected(Command command, string protectedName, string rejectedName)
        {
            var before = _task.Variables.Public.Concat(_task.Variables.Private)
                .OrderBy(x => x.Name, StringComparer.Ordinal)
                .Select(x => (x.Name, x.Value, x.Secret, x.ReadOnly, x.PreserveCase)).ToArray();

            var error = Assert.Throws<InvalidOperationException>(() => new TaskSetVariableCommand().Execute(_task, command));

            Assert.Equal(StringUtil.Loc("ReadOnlyVariable", rejectedName), error.Message);
            Assert.Equal(before, _task.Variables.Public.Concat(_task.Variables.Private)
                .OrderBy(x => x.Name, StringComparer.Ordinal)
                .Select(x => (x.Name, x.Value, x.Secret, x.ReadOnly, x.PreserveCase)).ToArray());
            Assert.Equal("original", _task.Variables.Get(protectedName));
            Assert.Empty(_records);
            AssertNoOutputVariables();
        }

        private void AssertNoOutputVariables()
        {
            Assert.Single(_taskRecord.Variables);
            Assert.Equal(BuildConstants.AgentPackage.Version, _taskRecord.Variables[TaskWellKnownItems.AgentVersionTimelineVariable].Value);
        }

        private static Command CreateCommand(string name, bool output = false, bool secret = false, bool preserveCase = false, bool readOnly = false)
        {
            var command = new Command("task", "setvariable") { Data = "replacement" };
            command.Properties["variable"] = name;
            command.Properties["isOutput"] = output.ToString();
            command.Properties["isSecret"] = secret.ToString();
            command.Properties["preserveCase"] = preserveCase.ToString();
            command.Properties["isReadOnly"] = readOnly.ToString();
            return command;
        }

        public void Dispose()
        {
            _task?.Dispose();
            _job.Dispose();
            _host.Dispose();
            Environment.SetEnvironmentVariable(AgentKnobs.ProtectReadOnlyVariableNamesEnvironmentVariable, _previousEnvironmentValue);
        }
    }
}
