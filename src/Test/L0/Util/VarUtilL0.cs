// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using Microsoft.VisualStudio.Services.Agent.Util;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Util
{
    public class VarUtilL0
    {
        public static TheoryData<string, string[]> InputsForVariablesReplacementTest => new TheoryData<string, string[]>
        {
            { "Bash", new string[]{ "$SYSTEM_DEFINITIONNAME", "$BUILD_SOURCEVERSIONMESSAGE" } },
            { "PowerShell", new string[]{ "$env:SYSTEM_DEFINITIONNAME", "$env:BUILD_SOURCEVERSIONMESSAGE" }},
        };

        [Theory]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        [MemberData(nameof(InputsForVariablesReplacementTest))]
        public void ExpandValues_Replaces_VulnerableVariables_To_EnvVariables_Per_ShellTask(string taskName, string[] expectedVariables)
        {
            using TestHostContext hc = new TestHostContext(this);
            var source = new Dictionary<string, string>();
            var target = GetTargetValuesWithVulnerableVariables();

            VarUtil.ExpandValues(hc, source, target, taskName);

            Assert.Equal($"test {expectedVariables[0]}", target["system.DefinitionName var"]);
            Assert.Equal($"test {expectedVariables[1]}", target["build.SourceVersionMessage var"]);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void ExpandValues_Replaces_VulnerableVariables_Ignoring_LetterCase()
        {
            using TestHostContext hc = new TestHostContext(this);
            var source = new Dictionary<string, string>();
            var target = new Dictionary<string, string>()
            {
                ["system.DefinitionName var"] = $"$(systeM.DeFiNiTioNname)",
                ["build.SourceVersionMessage var"] = $"$(buiLd.sourCeVersionMeSsagE)",
            };

            VarUtil.ExpandValues(hc, source, target, "Bash");

            Assert.Equal("$SYSTEM_DEFINITIONNAME", target["system.DefinitionName var"]);
            Assert.Equal("$BUILD_SOURCEVERSIONMESSAGE", target["build.SourceVersionMessage var"]);
        }

        [Theory]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        [InlineData(null, "")]
        [InlineData("", "")]
        [InlineData(" ", " ")]
        public void ExpandValues_Prevents_InvalidValues_In_Target(string targetValue, string extectedValue)
        {
            using TestHostContext hc = new TestHostContext(this);
            var source = new Dictionary<string, string>();
            var target = new Dictionary<string, string>()
            {
                ["targetVar"] = targetValue
            };

            VarUtil.ExpandValues(hc, source, target);

            Assert.Equal(extectedValue, target["targetVar"]);
        }

        [Theory]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        [InlineData("testVar", "source = 'testVar'")]
        [InlineData(null, "source = ''")]
        [InlineData("", "source = ''")]
        [InlineData(" ", "source = ' '")]
        [InlineData("_", "source = '_'")]
        public void ExpandValues_Replaces_Variable_To_SourceValue_In_Target(string sourceValue, string expectedValue)
        {
            using TestHostContext hc = new TestHostContext(this);
            var source = new Dictionary<string, string>()
            {
                ["sourceVar"] = sourceValue,
            };
            var target = new Dictionary<string, string>()
            {
                ["targetVar"] = "source = '$(sourceVar)'",
            };

            VarUtil.ExpandValues(hc, source, target);

            Assert.Equal(expectedValue, target["targetVar"]);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void ExpandValues_Replaces_Multiple_VulnerableVariables_In_Target()
        {
            using TestHostContext hc = new TestHostContext(this);
            var source = new Dictionary<string, string>();
            var target = new Dictionary<string, string>()
            {
                ["targetVar"] = "variable1 = $(system.stageDisplayName); variable2 = $(system.phaseDisplayName); variable3 = $(release.environmentName)",
            };

            VarUtil.ExpandValues(hc, source, target, "Bash");

            Assert.Equal($"variable1 = $SYSTEM_STAGEDISPLAYNAME; variable2 = $SYSTEM_PHASEDISPLAYNAME; variable3 = $RELEASE_ENVIRONMENTNAME", target["targetVar"]);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void ExpandValues_Keeping_Same_If_No_Task_Specified()
        {
            using TestHostContext hc = new TestHostContext(this);
            var source = new Dictionary<string, string>();
            var target = GetTargetValuesWithVulnerableVariables();

            VarUtil.ExpandValues(hc, source, target);

            Assert.Equal(target["system.DefinitionName var"], target["system.DefinitionName var"]);
            Assert.Equal(target["build.DefinitionName var"], target["build.DefinitionName var"]);
            Assert.Equal(target["build.SourceVersionMessage var"], target["build.SourceVersionMessage var"]);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void ExpandValues_Keeping_Same_If_Wrong_TaskName_Specified()
        {
            using TestHostContext hc = new TestHostContext(this);
            var source = new Dictionary<string, string>();
            var target = new Dictionary<string, string>()
            {
                ["targetVar"] = $"test $(system.definitionName)",
            };

            VarUtil.ExpandValues(hc, source, target, "SomeRandomName");

            Assert.Equal("test $(system.definitionName)", target["targetVar"]);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void ExpandValues_ExpandNestedVariableTest()
        {
            using TestHostContext hc = new TestHostContext(this);
            var source = new Dictionary<string, string>
            {
                ["sourceVar"] = "sourceValue",
            };
            var target = new Dictionary<string, string>
            {
                ["targetVar"] = "targetValue $(sourceVar)",
            };

            VarUtil.ExpandValues(hc, source, target);

            Assert.Equal("targetValue sourceValue", target["targetVar"]);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void ExpandValues_Keeping_Same_Value_If_No_Match_With_Target()
        {
            using TestHostContext hc = new TestHostContext(this);
            var source = new Dictionary<string, string>
            {
                ["sourceVar1"] = "source value"
            };
            var target = new Dictionary<string, string>
            {
                ["targetVar"] = "targetValue $(sourceVar2)",
            };

            VarUtil.ExpandValues(hc, source, target);

            Assert.Equal("targetValue $(sourceVar2)", target["targetVar"]);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void ExpandValues_RecursiveExpanding_NotHappening()
        {
            using TestHostContext hc = new TestHostContext(this);
            var source = new Dictionary<string, string>
            {
                ["sourceVar1"] = "sourceValue1",
                ["sourceVar2"] = "sourceValue1 $(sourceVar1)",
            };
            var target = new Dictionary<string, string>
            {
                ["targetVar"] = "targetValue $(sourceVar2)",
            };

            VarUtil.ExpandValues(hc, source, target);

            Assert.Equal("targetValue sourceValue1 $(sourceVar1)", target["targetVar"]);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void ExpandValues_CommandShell_InputExpanding()
        {
            using TestHostContext hc = new TestHostContext(this);
            var source = new Dictionary<string, string>
            {
                ["sourceVar1"] = "sourceValue1",
                ["sourceVar2"] = "1 & echo 2",
                ["sourceVar3"] = "3 | 4",
                ["sourceVar4"] = "5 < 6 > 1",
                ["sourceVar5"] = "7 &&>|<echo 34",
            };
            var target = new Dictionary<string, string>
            {
                ["targetVar1"] = "echo $(sourceVar1)",
                ["targetVar2"] = "echo $(sourceVar1) & echo $(sourceVar2)",
                ["targetVar3"] = "echo $(sourceVar3) | echo $(sourceVar4)",
                ["targetVar4"] = "echo $(sourceVar5) && 123",
            };

            VarUtil.ExpandValues(hc, source, target, "CmdLine");

            Assert.Equal("echo sourceValue1", target["targetVar1"]);
            Assert.Equal("echo sourceValue1 & echo 1 ^& echo 2", target["targetVar2"]);
            Assert.Equal("echo 3 ^| 4 | echo 5 ^< 6 ^> 1", target["targetVar3"]);
            Assert.Equal("echo 7 ^&^&^>^|^<echo 34 && 123", target["targetVar4"]);
        }

        [Theory]
        [InlineData("test.value1", "TEST_VALUE1")]
        [InlineData("test value2", "TEST_VALUE2")]
        [InlineData("tesT vaLue.3", "TEST_VALUE_3")]
        [InlineData(".tesT vaLue 4", "_TEST_VALUE_4")]
        [InlineData("TEST_VALUE_5", "TEST_VALUE_5")]
        [InlineData(".. TEST   VALUE. 6", "___TEST___VALUE__6")]
        [InlineData(null, "")]
        [InlineData("", "")]
        [InlineData(" ", "_")]
        [InlineData(".", "_")]
        public void TestConverterToEnvVariableFormat(string input, string expected)
        {
            var result = VarUtil.ConvertToEnvVariableFormat(input);

            Assert.Equal(expected, result);
        }

        private Dictionary<string, string> GetTargetValuesWithVulnerableVariables()
        {
            return new Dictionary<string, string>()
            {
                ["system.DefinitionName var"] = $"test $({Constants.Variables.System.DefinitionName})",
                ["build.DefinitionName var"] = $"test $({Constants.Variables.Build.DefinitionName})",
                ["build.SourceVersionMessage var"] = $"test $({Constants.Variables.Build.SourceVersionMessage})",
            };
        }
    }
}