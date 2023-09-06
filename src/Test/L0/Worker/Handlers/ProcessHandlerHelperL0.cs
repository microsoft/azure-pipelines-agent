using Agent.Worker.Handlers.Helpers;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Test.L0.Worker.Handlers
{
    public sealed class ProcessHandlerHelperL0
    {
        [Fact]
        public void EmptyLineTest()
        {
            string argsLine = "";
            string expectedArgs = "";

            var (actualArgs, _) = ProcessHandlerHelper.ProcessInputArguments(argsLine);

            Assert.Equal(expectedArgs, actualArgs);
        }

        [Fact]
        public void BasicTest()
        {
            string argsLine = "%VAR1% 2";
            string expectedArgs = "value1 2";
            Environment.SetEnvironmentVariable("VAR1", "value1");

            var (actualArgs, _) = ProcessHandlerHelper.ProcessInputArguments(argsLine);

            Assert.Equal(expectedArgs, actualArgs);
        }

        [Fact]
        public void TestWithMultipleVars()
        {
            string argsLine = "1 %VAR1% %VAR2%";
            string expectedArgs = "1 value1 value2";
            Environment.SetEnvironmentVariable("VAR1", "value1");
            Environment.SetEnvironmentVariable("VAR2", "value2");

            var (actualArgs, _) = ProcessHandlerHelper.ProcessInputArguments(argsLine);

            Assert.Equal(expectedArgs, actualArgs);
        }

        [Theory]
        [InlineData("%VAR1% %VAR2%%VAR3%", "1 23")]
        [InlineData("%VAR1% %VAR2%_%VAR3%", "1 2_3")]
        [InlineData("%VAR1%%VAR2%%VAR3%", "123")]
        public void TestWithCloseVars(string inputArgs, string expectedArgs)
        {
            Environment.SetEnvironmentVariable("VAR1", "1");
            Environment.SetEnvironmentVariable("VAR2", "2");
            Environment.SetEnvironmentVariable("VAR3", "3");

            var (actualArgs, _) = ProcessHandlerHelper.ProcessInputArguments(inputArgs);

            Assert.Equal(expectedArgs, actualArgs);
        }

        [Fact]
        public void NestedVariablesNotExpands()
        {
            string argsLine = "%VAR1% %VAR2%";
            string expectedArgs = "%NESTED% 2";
            Environment.SetEnvironmentVariable("VAR1", "%NESTED%");
            Environment.SetEnvironmentVariable("VAR2", "2");
            Environment.SetEnvironmentVariable("NESTED", "nested");

            var (actualArgs, _) = ProcessHandlerHelper.ExpandArguments(argsLine);

            Assert.Equal(expectedArgs, actualArgs);
        }

        [Fact]
        public void SkipsInvalidEnv()
        {
            string argsLine = "%VAR1% 2";
            Environment.SetEnvironmentVariable("VAR1", null);

            string expectedArgs = "%VAR1% 2";

            var (actualArgs, _) = ProcessHandlerHelper.ExpandArguments(argsLine);

            Assert.Equal(expectedArgs, actualArgs);
        }
    }
}
