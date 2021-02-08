// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
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
                var command = SetVariableCommand("myVar", "myVal");
                Assert.Throws<InvalidOperationException>(() => new TaskSetVariableCommand().Execute(_ec.Object, command));
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
                }

                var badVar = "badVar";
                command = SetVariableCommand(badVar, value);
                setVariable = new TaskSetVariableCommand();
                Assert.Throws<InvalidOperationException>(() => setVariable.Execute(_ec.Object, command));
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
                }

                var badVar = "badVar";
                command = SetVariableCommand(badVar, value);
                setVariable = new TaskSetVariableCommand();
                Assert.Throws<InvalidOperationException>(() => setVariable.Execute(_ec.Object, command));
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
                }

                // settable in only one
                foreach (String variable in new String[] { "myStuff", "otherVar", "extra", "neither" })
                {
                    command = SetVariableCommand(variable, value);
                    setVariable = new TaskSetVariableCommand();
                    Assert.Throws<InvalidOperationException>(() => setVariable.Execute(_ec.Object, command));
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
                foreach (String variable in new String[] { "myVar", "otherVar", "neither" })
                {
                    command = SetVariableCommand(variable, value);
                    setVariable = new TaskSetVariableCommand();
                    Assert.Throws<InvalidOperationException>(() => setVariable.Execute(_ec.Object, command));
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

                // disallow path
                var restrictions = new TaskRestrictions() { SettableVariables = new TaskVariableRestrictions() };
                _ec.Object.Restrictions.Add(restrictions);
                prependPath = new TaskPrepandPathCommand();
                command = PrependPathCommand("path2");
                Assert.Throws<InvalidOperationException>(() => prependPath.Execute(_ec.Object, command));

                // allow path
                restrictions.SettableVariables.Allowed.Add("path");
                prependPath = new TaskPrepandPathCommand();
                command = PrependPathCommand("path3");
                prependPath.Execute(_ec.Object, command);
                Assert.True(_ec.Object.PrependPath.Contains("path3"));
            }
        }
        private TestHostContext CreateTestContext([CallerMemberName] String testName = "")
        {
            var hc = new TestHostContext(this, testName);

            _variables = new Variables(
                hostContext: hc,
                copy: new Dictionary<string, VariableValue>(),
                warnings: out _);

            _ec = new Mock<IExecutionContext>();
            _ec.SetupAllProperties();
            _ec.Setup(x => x.PrependPath).Returns(new List<string>());
            _ec.Setup(x => x.Restrictions).Returns(new List<TaskRestrictions>());
            _ec.Setup(x => x.GetHostContext()).Returns(hc);
            _ec.Setup(x => x.Variables).Returns(_variables);
            _ec.Setup(x => x.SetVariable(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .Callback<string, string, bool, bool, bool, bool>((name, value, secret, b2, b3, readOnly) => _variables.Set(name, value, secret, readOnly));

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
    }
}
