// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Xunit;
using Agent.Worker.Handlers.Helpers;
using System.Collections.Generic;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Moq;

namespace Test.L0.Worker.Handlers
{
    public sealed class ProcessHandlerHelperL0
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker.Handlers")]
        public void EmptyLineTest()
        {
            string argsLine = "";
            string expectedArgs = "";

            var (actualArgs, _) = ProcessHandlerHelper.ExpandCmdEnv(argsLine, new());

            Assert.Equal(expectedArgs, actualArgs);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker.Handlers")]
        public void BasicTest()
        {
            string argsLine = "%VAR1% 2";
            string expectedArgs = "value1 2";
            var testEnv = new Dictionary<string, string>()
            {
                ["VAR1"] = "value1"
            };
            var (actualArgs, _) = ProcessHandlerHelper.ExpandCmdEnv(argsLine, testEnv);

            Assert.Equal(expectedArgs, actualArgs);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker.Handlers")]
        public void TestWithMultipleVars()
        {
            string argsLine = "1 %VAR1% %VAR2%";
            string expectedArgs = "1 value1 value2";
            var testEnv = new Dictionary<string, string>()
            {
                ["VAR1"] = "value1",
                ["VAR2"] = "value2"
            };

            var (actualArgs, _) = ProcessHandlerHelper.ExpandCmdEnv(argsLine, testEnv);

            Assert.Equal(expectedArgs, actualArgs);
        }

        [Theory]
        [InlineData("%VAR1% %VAR2%%VAR3%", "1 23")]
        [InlineData("%VAR1% %VAR2%_%VAR3%", "1 2_3")]
        [InlineData("%VAR1%%VAR2%%VAR3%", "123")]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker.Handlers")]
        public void TestWithCloseVars(string inputArgs, string expectedArgs)
        {
            var testEnv = new Dictionary<string, string>()
            {
                { "VAR1", "1" },
                { "VAR2", "2" },
                { "VAR3", "3" }
            };

            var (actualArgs, _) = ProcessHandlerHelper.ExpandCmdEnv(inputArgs, testEnv);

            Assert.Equal(expectedArgs, actualArgs);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker.Handlers")]
        public void NestedVariablesNotExpands()
        {
            string argsLine = "%VAR1% %VAR2%";
            string expectedArgs = "%NESTED% 2";
            var testEnv = new Dictionary<string, string>()
            {
                { "VAR1", "%NESTED%" },
                { "VAR2", "2"},
                { "NESTED", "nested" }
            };

            var (actualArgs, _) = ProcessHandlerHelper.ExpandCmdEnv(argsLine, testEnv);

            Assert.Equal(expectedArgs, actualArgs);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker.Handlers")]
        public void SkipsInvalidEnv()
        {
            string argsLine = "%VAR1% 2";
            var testEnv = new Dictionary<string, string>()
            {
                { "VAR1", null}
            };

            string expectedArgs = "%VAR1% 2";

            var (actualArgs, _) = ProcessHandlerHelper.ExpandCmdEnv(argsLine, testEnv);

            Assert.Equal(expectedArgs, actualArgs);
        }

        [Theory]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker.Handlers")]
        [InlineData("%var")]
        [InlineData("%someothervar%")]
        public void TestNoChanges(string input)
        {
            var testEnv = new Dictionary<string, string>
            {
                { "var", "value" }
            };
            var (output, _) = ProcessHandlerHelper.ExpandCmdEnv(input, testEnv);

            Assert.Equal(input, output);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker.Handlers")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "darwin")]
        [Trait("SkipOn", "linux")]
        public void WindowsCaseInsensetiveTest()
        {
            string argsLine = "%var1% 2";
            var testEnv = new Dictionary<string, string>()
            {
                { "VAR1", "value1"}
            };

            string expandedArgs = "value1 2";

            var (actualArgs, _) = ProcessHandlerHelper.ExpandCmdEnv(argsLine, testEnv);
            Assert.Equal(expandedArgs, actualArgs);
        }

        [Theory]
        [InlineData("%var%", "1 & echo 23")]
        [InlineData("%var%%", "1 & echo 23")]
        [InlineData("%%var%", "1 & echo 23")]
        [InlineData("1 & echo 23", "")]
        [InlineData("1 ; whoami", "")]
        [InlineData("1 | whoami", "")]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker.Handlers")]
        public void ArgsValidation_Failes(string inputArgs, string envVarValue)
        {
            var testEnv = new Dictionary<string, string>
            {
                {"var", envVarValue},
            };

            var mockContext = CreateMockExecContext();

            var (isValid, _) = ProcessHandlerHelper.ValidateInputArguments(inputArgs, testEnv, mockContext.Object);

            Assert.False(isValid);
        }

        [Theory]
        [InlineData("", "")]
        [InlineData("%", "")]
        [InlineData("1 2", "")]
        [InlineData("1 %var%", "2")]
        [InlineData("1 \"2\"", "")]
        [InlineData("%%var%%", "1")]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker.Handlers")]
        public void ArgsValidation_Passes(string inputArgs, string envVarValue)
        {
            var testEnv = new Dictionary<string, string>
            {
                {"var", envVarValue},
            };

            var mockContext = CreateMockExecContext();

            var (isValid, _) = ProcessHandlerHelper.ValidateInputArguments(inputArgs, testEnv, mockContext.Object);

            Assert.True(isValid);
        }


        private Mock<IExecutionContext> CreateMockExecContext()
        {
            var mockContext = new Mock<IExecutionContext>();
            mockContext.Setup(x => x.GetVariableValueOrDefault(It.IsAny<string>())).Returns("true");

            return mockContext;
        }
    }
}
