// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.Handlers;
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

                foreach (String variable in new String[] { "myVar", "myvar", "MYVAR", "otherVAR" })
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

                foreach (String variable in new String[] { "myVar", "mything", "MY" })
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
                foreach (String variable in new String[] { "myVar", "myThing" })
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
                    Assert.Equal(lastCount + 1, _warnings.Count);
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
                    Assert.Equal(lastCount + 1, _warnings.Count);
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
        [InlineData("HTTP_PROXY", "temporary-value")]
        [InlineData("HTTPS_PROXY", "temporary-value")]
        [InlineData("NO_PROXY", "temporary-value")]
        [InlineData("NODE_EXTRA_CA_CERTS", "temporary-value")]
        [InlineData("REQUESTS_CA_BUNDLE", "temporary-value")]
        [InlineData("NODE_USE_ENV_PROXY", "temporary-value")]
        [InlineData("JAVA_TOOL_OPTIONS", "temporary-value")]
        [InlineData("UV_NATIVE_TLS", "temporary-value")]
        [InlineData("SSL_CERT_FILE", "temporary-value")]
        [InlineData("HTTP_PROXY", "")]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void UnsetVariableRemovesExpandedAndUnexpandedValue(string name, string value)
        {
            using (TestHostContext hc = CreateTestContext())
            {
                _variables.Set(name, value);
                _variables.Set("other", "retained");
                var extension = new TaskCommandExtension();
                extension.Initialize(hc);

                extension.ProcessCommand(_ec.Object, UnsetVariableCommand(name.ToLowerInvariant()));
                _variables.RecalculateExpanded(out var warnings);

                Assert.False(_variables.TryGetValue(name, out _));
                Assert.DoesNotContain(_variables.Public, variable => variable.Name == name);
                Assert.DoesNotContain(_variables.Private, variable => variable.Name == name);
                Assert.Equal("retained", _variables.Get("other"));
                Assert.Empty(warnings);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void UnsetVariableIsDistinctFromSettingEmptyAndCanBeSetAgain()
        {
            using (TestHostContext hc = CreateTestContext())
            {
                var setVariable = new TaskSetVariableCommand();
                setVariable.Execute(_ec.Object, SetVariableCommand("HTTP_PROXY", ""));
                Assert.True(_variables.TryGetValue("HTTP_PROXY", out string value));
                Assert.Equal("", value);

                new TaskUnsetVariableCommand().Execute(_ec.Object, UnsetVariableCommand("HTTP_PROXY"));
                Assert.False(_variables.TryGetValue("HTTP_PROXY", out _));

                setVariable.Execute(_ec.Object, SetVariableCommand("HTTP_PROXY", "replacement-value"));
                Assert.Equal("replacement-value", _variables.Get("HTTP_PROXY"));
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void UnsetVariableRequiresName(string name)
        {
            using (TestHostContext hc = CreateTestContext())
            {
                Assert.Throws<ArgumentNullException>(() =>
                    new TaskUnsetVariableCommand().Execute(_ec.Object, UnsetVariableCommand(name)));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void UnsetVariableIsIdempotent()
        {
            using (TestHostContext hc = CreateTestContext())
            {
                _variables.Set("NO_PROXY", "temporary-value");
                var unsetVariable = new TaskUnsetVariableCommand();
                unsetVariable.Execute(_ec.Object, UnsetVariableCommand("NO_PROXY"));
                unsetVariable.Execute(_ec.Object, UnsetVariableCommand("NO_PROXY"));

                Assert.False(_variables.TryGetValue("NO_PROXY", out _));
                Assert.Empty(_warnings);
            }
        }

        [Theory]
        [InlineData("HTTP_PROXY", true)]
        [InlineData("system.accessToken", false)]
        [InlineData("producer.output", true)]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void UnsetVariableRejectsReadOnlyVariablesWithoutLegacyFlag(string name, bool readOnly)
        {
            using (TestHostContext hc = CreateTestContext())
            {
                _variables.Set(Constants.Variables.Agent.ReadOnlyVariables, "false");
                _variables.Set(name, "retained", readOnly: readOnly);

                Assert.Throws<InvalidOperationException>(() =>
                    new TaskUnsetVariableCommand().Execute(_ec.Object, UnsetVariableCommand(name)));

                Assert.Equal("retained", _variables.Get(name));
                Assert.True(_variables.IsReadOnly(name));
            }
        }

        [Theory]
        [InlineData("TEST_VARIABLE")]
        [InlineData("system.debug")]
        [InlineData("agent.readOnlyVariables")]
        [InlineData("build.sourceBranch")]
        [InlineData("HTTP_*")]
        [InlineData("HTTP_PROXY_EXTRA")]
        [InlineData("HTTP.PROXY")]
        [InlineData(" HTTP_PROXY")]
        [InlineData("HTTP_PROXY ")]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void UnsetVariableRejectsNamesOutsideFixedAllowlist(string name)
        {
            using (TestHostContext hc = CreateTestContext())
            {
                var restrictions = new TaskRestrictions { SettableVariables = new TaskVariableRestrictions() };
                restrictions.SettableVariables.Allowed.Add("*");
                _ec.Object.Restrictions.Add(restrictions);
                var unsetVariable = new TaskUnsetVariableCommand();

                Assert.Throws<InvalidOperationException>(() =>
                    unsetVariable.Execute(_ec.Object, UnsetVariableCommand(name)));

                _variables.Set(name, "retained");
                Assert.Throws<InvalidOperationException>(() =>
                    unsetVariable.Execute(_ec.Object, UnsetVariableCommand(name)));
                _variables.RecalculateExpanded(out var warnings);

                Assert.Equal("retained", _variables.Get(name));
                Assert.Empty(warnings);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void UnsetVariableRejectsSecretsAndPreservesStickySecrecy(bool expandedSecret)
        {
            using (TestHostContext hc = CreateTestContext())
            {
                const string name = "REQUESTS_CA_BUNDLE";
                if (expandedSecret)
                {
                    _variables.Set("source", "temporary-value", secret: true);
                    _variables.Set(name, "$(source)");
                    _variables.RecalculateExpanded(out _);
                }
                else
                {
                    _variables.Set(name, "temporary-value", secret: true);
                }

                Assert.Throws<InvalidOperationException>(() =>
                    new TaskUnsetVariableCommand().Execute(_ec.Object, UnsetVariableCommand(name.ToLowerInvariant())));
                Assert.Equal("temporary-value", _variables.Get(name));
                Assert.Contains(_variables.Private, variable => variable.Name == name);
                Assert.Equal("***", hc.SecretMasker.MaskSecrets("temporary-value"));

                new TaskSetVariableCommand().Execute(_ec.Object, SetVariableCommand(name, "replacement-value"));

                Assert.Contains(_variables.Private, variable => variable.Name == name && variable.Value == "replacement-value");
                Assert.DoesNotContain(_variables.Public, variable => variable.Name == name);
                Assert.Equal("***", hc.SecretMasker.MaskSecrets("replacement-value"));
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void UnsetVariableRespectsRestrictionsInRestrictedCommandMode(bool allowed)
        {
            using (TestHostContext hc = CreateTestContext())
            {
                var restrictions = new TaskRestrictions
                {
                    Commands = new TaskCommandRestrictions { Mode = TaskCommandMode.Restricted },
                    SettableVariables = new TaskVariableRestrictions()
                };
                if (allowed)
                {
                    restrictions.SettableVariables.Allowed.Add("HTTP_*");
                }
                _ec.Object.Restrictions.Add(restrictions);
                _variables.Set("HTTP_PROXY", "temporary-value");
                var extension = new TaskCommandExtension();
                extension.Initialize(hc);

                extension.ProcessCommand(_ec.Object, UnsetVariableCommand("HTTP_PROXY"));

                Assert.Equal(allowed ? null : "temporary-value", _variables.Get("HTTP_PROXY"));
                Assert.Equal(allowed ? 0 : 1, _warnings.Count);
                if (!allowed)
                {
                    Assert.Contains("SetVariableNotAllowed", _warnings[0]);
                }
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("explicit-task-value")]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void UnsetVariableStopsInjectionIntoSubsequentTaskEnvironment(string explicitValue)
        {
            using (TestHostContext hc = CreateTestContext())
            {
                hc.SetSingleton(Mock.Of<IWorkerCommandManager>());
                _variables.Set("NODE_USE_ENV_PROXY", "temporary-value");
                var currentTask = new EnvironmentTestHandler { RuntimeVariables = _variables };
                currentTask.Initialize(hc);
                currentTask.Environment = new Dictionary<string, string>();
                currentTask.PopulateEnvironment();
                Assert.Equal("temporary-value", currentTask.Environment["NODE_USE_ENV_PROXY"]);

                new TaskUnsetVariableCommand().Execute(_ec.Object, UnsetVariableCommand("NODE_USE_ENV_PROXY"));

                var nextTask = new EnvironmentTestHandler { RuntimeVariables = _variables };
                nextTask.Initialize(hc);
                nextTask.Environment = new Dictionary<string, string>();
                if (explicitValue != null)
                {
                    nextTask.Environment["NODE_USE_ENV_PROXY"] = explicitValue;
                }
                nextTask.PopulateEnvironment();

                Assert.Equal(explicitValue != null, nextTask.Environment.ContainsKey("NODE_USE_ENV_PROXY"));
                if (explicitValue != null)
                {
                    Assert.Equal(explicitValue, nextTask.Environment["NODE_USE_ENV_PROXY"]);
                }
                Assert.DoesNotContain("NODE_USE_ENV_PROXY", nextTask.Environment["VSTS_PUBLIC_VARIABLES"]);
                Assert.Equal("temporary-value", currentTask.Environment["NODE_USE_ENV_PROXY"]);
            }
        }

        private sealed class EnvironmentTestHandler : Handler
        {
            public void PopulateEnvironment() => AddVariablesToEnvironment();
        }

        private Command UnsetVariableCommand(string name)
        {
            var command = new Command("task", "unsetvariable");
            if (name != null)
            {
                command.Properties.Add("variable", name);
            }
            return command;
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
            _ec.Setup(x => x.SetVariable(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .Callback<string, string, bool, bool, bool, bool, bool>((name, value, secret, b2, b3, readOnly, preserveCase) => _variables.Set(name, value, secret, readOnly, preserveCase));
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
