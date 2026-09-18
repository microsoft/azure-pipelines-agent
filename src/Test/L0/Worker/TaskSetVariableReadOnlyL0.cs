using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Agent.Sdk.Knob;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker;
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

            Assert.True(AgentKnobs.ProtectReadOnlyVariableNames.GetValue(_task).AsBoolean());
            AssertRejected(CreateCommand("SYSTEM_OIDCREQUESTURI"), "System.OidcRequestUri", "SYSTEM_OIDCREQUESTURI");
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
            Assert.True(AgentKnobs.ProtectReadOnlyVariableNames.GetValue(_task).AsBoolean());
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
