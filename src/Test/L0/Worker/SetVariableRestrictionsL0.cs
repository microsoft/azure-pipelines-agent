// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Moq;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker
{
    public sealed class SetVariableRestrictionsL0
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void NoRestrictions()
        {
            using (TestHostContext hc = CreateTestContext())
            {
                var variable = "myVar";
                var value = "myValue";
                var setVariable = new TaskSetVariableCommand();
                var command = SetVariableCommand(variable, value);
                setVariable.Execute(_ec.Object, command);
                Assert.Equal(value, _variables.Get(variable));
                Assert.Equal(0, _warnings.Count);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void NullVariableRestrictions()
        {
            using (TestHostContext hc = CreateTestContext())
            {
                _ec.Object.Restrictions.Add(new TaskRestrictions());
                var variable = "myVar";
                var value = "myValue";
                var setVariable = new TaskSetVariableCommand();
                var command = SetVariableCommand(variable, value);
                setVariable.Execute(_ec.Object, command);
                Assert.Equal(value, _variables.Get(variable));
                Assert.Equal(0, _warnings.Count);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void EmptyAllowed()
        {
            using (TestHostContext hc = CreateTestContext())
            {
                _ec.Object.Restrictions.Add(new TaskRestrictions() { SettableVariables = new TaskVariableRestrictions() });
                var variable = "myVar";
                var setVariable = new TaskSetVariableCommand();
                var command = SetVariableCommand(variable, "myVal");
                setVariable.Execute(_ec.Object, command);
                Assert.Equal(null, _variables.Get(variable));
                Assert.Equal(1, _warnings.Count);
                Assert.Contains("SetVariableNotAllowed", _warnings[0]);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void ExactMatchAllowed()
        {
            using (TestHostContext hc = CreateTestContext())
            {
                var restrictions = new TaskRestrictions() { SettableVariables = new TaskVariableRestrictions() };
                restrictions.SettableVariables.Allowed.Add("myVar");
                restrictions.SettableVariables.Allowed.Add("otherVar");
                _ec.Object.Restrictions.Add(restrictions);

                TaskSetVariableCommand setVariable;
                Command command;
                var value = "myValue";

                foreach(String variable in new String[] { "myVar", "myvar", "MYVAR", "otherVAR" })
                {
                    command = SetVariableCommand(variable, value);
                    setVariable = new TaskSetVariableCommand();
                    setVariable.Execute(_ec.Object, command);
                    Assert.Equal(value, _variables.Get(variable));
                    Assert.Equal(0, _warnings.Count);
                }

                var badVar = "badVar";
                command = SetVariableCommand(badVar, value);
                setVariable = new TaskSetVariableCommand();
                setVariable.Execute(_ec.Object, command);
                Assert.Equal(null, _variables.Get(badVar));
                Assert.Equal(1, _warnings.Count);
                Assert.Contains("SetVariableNotAllowed", _warnings[0]);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void MiniMatchAllowed()
        {
            using (TestHostContext hc = CreateTestContext())
            {
                var restrictions = new TaskRestrictions() { SettableVariables = new TaskVariableRestrictions() };
                restrictions.SettableVariables.Allowed.Add("my*");
                _ec.Object.Restrictions.Add(restrictions);

                TaskSetVariableCommand setVariable;
                Command command;
                var value = "myValue";

                foreach(String variable in new String[] { "myVar", "mything", "MY" })
                {
                    command = SetVariableCommand(variable, value);
                    setVariable = new TaskSetVariableCommand();
                    setVariable.Execute(_ec.Object, command);
                    Assert.Equal(value, _variables.Get(variable));
                    Assert.Equal(0, _warnings.Count);
                }

                var badVar = "badVar";
                command = SetVariableCommand(badVar, value);
                setVariable = new TaskSetVariableCommand();
                setVariable.Execute(_ec.Object, command);
                Assert.Equal(null, _variables.Get(badVar));
                Assert.Equal(1, _warnings.Count);
                Assert.Contains("SetVariableNotAllowed", _warnings[0]);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void MultipleRestrictionsMostRestrictive()
        {
            using (TestHostContext hc = CreateTestContext())
            {
                // multiple sets of restrictions, such as from task.json and the pipeline yaml
                var restrictions1 = new TaskRestrictions() { SettableVariables = new TaskVariableRestrictions() };
                restrictions1.SettableVariables.Allowed.Add("my*");
                restrictions1.SettableVariables.Allowed.Add("otherVar");
                _ec.Object.Restrictions.Add(restrictions1);
                var restrictions2 = new TaskRestrictions() { SettableVariables = new TaskVariableRestrictions() };
                restrictions2.SettableVariables.Allowed.Add("myVar");
                restrictions2.SettableVariables.Allowed.Add("myThing");
                restrictions2.SettableVariables.Allowed.Add("extra");
                _ec.Object.Restrictions.Add(restrictions2);

                TaskSetVariableCommand setVariable;
                Command command;
                var value = "myValue";

                // settable is both allowed lists
                foreach(String variable in new String[] { "myVar", "myThing" })
                {
                    command = SetVariableCommand(variable, value);
                    setVariable = new TaskSetVariableCommand();
                    setVariable.Execute(_ec.Object, command);
                    Assert.Equal(value, _variables.Get(variable));
                    Assert.Equal(0, _warnings.Count);
                }

                // settable in only one
                int lastCount = _warnings.Count;
                foreach (String variable in new String[] { "myStuff", "otherVar", "extra", "neither" })
                {
                    command = SetVariableCommand(variable, value);
                    setVariable = new TaskSetVariableCommand();
                    setVariable.Execute(_ec.Object, command);
                    Assert.Equal(null, _variables.Get(variable));
                    Assert.Equal(lastCount+1, _warnings.Count);
                    Assert.Contains("SetVariableNotAllowed", _warnings.Last());
                    lastCount = _warnings.Count;
                }
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void MultipleRestrictionsNothingAllowed()
        {
            using (TestHostContext hc = CreateTestContext())
            {
                var restrictions1 = new TaskRestrictions() { SettableVariables = new TaskVariableRestrictions() };
                restrictions1.SettableVariables.Allowed.Add("myVar");
                restrictions1.SettableVariables.Allowed.Add("otherVar");
                _ec.Object.Restrictions.Add(restrictions1);
                var restrictions2 = new TaskRestrictions() { SettableVariables = new TaskVariableRestrictions() };
                _ec.Object.Restrictions.Add(restrictions2);

                TaskSetVariableCommand setVariable;
                Command command;
                var value = "myValue";

                // nothing is settable based on the second, empty allowed list
                int lastCount = _warnings.Count;
                foreach (String variable in new String[] { "myVar", "otherVar", "neither" })
                {
                    command = SetVariableCommand(variable, value);
                    setVariable = new TaskSetVariableCommand();
                    setVariable.Execute(_ec.Object, command);
                    Assert.Equal(null, _variables.Get(variable));
                    Assert.Equal(lastCount+1, _warnings.Count);
                    Assert.Contains("SetVariableNotAllowed", _warnings.Last());
                    lastCount = _warnings.Count;
                }
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void PrependPathAllowed()
        {
            using (TestHostContext hc = CreateTestContext())
            {
                // everything allowed
                TaskPrepandPathCommand prependPath = new TaskPrepandPathCommand();
                Command command = PrependPathCommand("path1");
                prependPath.Execute(_ec.Object, command);
                Assert.True(_ec.Object.PrependPath.Contains("path1"));
                Assert.Equal(0, _warnings.Count);

                // disallow path
                var restrictions = new TaskRestrictions() { SettableVariables = new TaskVariableRestrictions() };
                _ec.Object.Restrictions.Add(restrictions);
                prependPath = new TaskPrepandPathCommand();
                command = PrependPathCommand("path2");
                prependPath.Execute(_ec.Object, command);
                Assert.False(_ec.Object.PrependPath.Contains("path2"));
                Assert.Equal(1, _warnings.Count);

                // allow path
                restrictions.SettableVariables.Allowed.Add("path");
                prependPath = new TaskPrepandPathCommand();
                command = PrependPathCommand("path3");
                prependPath.Execute(_ec.Object, command);
                Assert.True(_ec.Object.PrependPath.Contains("path3"));
            }
        }

        [Theory]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        [InlineData("Enabled", false, true)]
        [InlineData("enabled", false, true)]
        [InlineData("WarningOnly", true, true)]
        [InlineData("Disabled", true, false)]
        [InlineData("disAbled", true, false)]
        [InlineData("Unexpected", true, true)]
        public void TaskDefinitionRestrictionsEnforcementMode(string mode, bool variableExpected, bool warningExpected)
        {
            using (TestHostContext hc = CreateTestContext())
            {
                _ec.Setup(x => x.GetVariableValueOrDefault("agent.taskRestrictionsEnforcementMode")).Returns(mode);
                var definition = new DefinitionData { Name = "TestTask", Version = new DefinitionVersion { Major = 2, Minor = 7, Patch = 1 } };
                // allow no variables
                var restrictions = new TaskDefinitionRestrictions(definition) { SettableVariables = new TaskVariableRestrictions() };
                _ec.Object.Restrictions.Add(restrictions);
                var variable = "myVar";
                var value = "myValue";
                Command command = SetVariableCommand(variable, value);
                TaskSetVariableCommand setVariable = new TaskSetVariableCommand();
                setVariable.Execute(_ec.Object, command);
                Assert.Equal((variableExpected ? value : null), _variables.Get(variable));
                if (warningExpected)
                {
                    Assert.Equal(1, _warnings.Count);
                    if (variableExpected)
                    {
                        Assert.EndsWith("SetVariableNotAllowedWarnOnly", _warnings[0]);
                    }
                    else
                    {
                        Assert.EndsWith("SetVariableNotAllowed", _warnings[0]);
                    }
                }
                else
                {
                    Assert.Equal(0, _warnings.Count);
                }
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void EnforcementModeDoesNotImpactPipelineRestrictions()
        {
            using (TestHostContext hc = CreateTestContext())
            {
                // This does nothing because this isn't using a TaskDefinitionRestrictions
                _ec.Setup(x => x.GetVariableValueOrDefault("agent.taskRestrictionsEnforcementMode")).Returns("Disabled");
                _ec.Object.Restrictions.Add(new TaskRestrictions() { SettableVariables = new TaskVariableRestrictions() });
                var variable = "myVar";
                var setVariable = new TaskSetVariableCommand();
                var command = SetVariableCommand(variable, "myVal");
                setVariable.Execute(_ec.Object, command);
                Assert.Equal(null, _variables.Get(variable));
                Assert.Equal(1, _warnings.Count);
                Assert.Contains("SetVariableNotAllowed", _warnings[0]);
            }
        }

        private TestHostContext CreateTestContext([CallerMemberName] String testName = "")
        {
            var hc = new TestHostContext(this, testName);
            hc.SetSingleton(new TaskRestrictionsChecker() as ITaskRestrictionsChecker);

            _variables = new Variables(
                hostContext: hc,
                copy: new Dictionary<string, VariableValue>(),
                warnings: out _);

            _warnings = new List<string>();

            _ec = new Mock<IExecutionContext>();
            _ec.SetupAllProperties();
            _ec.Setup(x => x.PrependPath).Returns(new List<string>());
            _ec.Setup(x => x.Restrictions).Returns(new List<TaskRestrictions>());
            _ec.Setup(x => x.GetHostContext()).Returns(hc);
            _ec.Setup(x => x.Variables).Returns(_variables);
            _ec.Setup(x => x.SetVariable(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .Callback<string, string, bool, bool, bool, bool>((name, value, secret, b2, b3, readOnly) => _variables.Set(name, value, secret, readOnly));
            _ec.Setup(x => x.AddIssue(It.IsAny<Issue>()))
                .Callback<Issue>((issue) =>
                    {
                        if (issue.Type == IssueType.Warning)
                        {
                            _warnings.Add(issue.Message);
                        }
                    });
            return hc;
        }

        private Command SetVariableCommand(String name, String value)
        {
            var command = new Command("task", "setvariable");
            command.Properties.Add("variable", name);
            command.Data = value;
            return command;
        }

        private Command PrependPathCommand(String value)
        {
            var command = new Command("task", "prependpath");
            command.Data = value;
            return command;
        }

        private Mock<IExecutionContext> _ec;
        private Variables _variables;
        private List<string> _warnings;
    }
}
