// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Xunit;
using Agent.Worker.Handlers.Helpers;
using System.Collections.Generic;

namespace Test.L0.Worker.Handlers
{
    public sealed class ProcessHandlerHelperL0
    {
        [Fact]
        public void EmptyLineTest()
        {
            string argsLine = "";
            string expectedArgs = "";

            var (actualArgs, _) = ProcessHandlerHelper.ExpandCmdEnv(argsLine, new());

            Assert.Equal(expectedArgs, actualArgs);
        }

        [Fact]
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

        [Fact]
        [Trait("Level", "L0")]
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
    }
}
