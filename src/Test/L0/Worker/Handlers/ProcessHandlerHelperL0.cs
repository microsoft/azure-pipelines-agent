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

        [Fact]
        public void TestWithCloseVars()
        {
            string argsLine = "%VAR1% %VAR2%%VAR3%";
            string expectedArgs = "value1 value2value3";
            Environment.SetEnvironmentVariable("VAR1", "value1");
            Environment.SetEnvironmentVariable("VAR2", "value2");
            Environment.SetEnvironmentVariable("VAR3", "value3");

            var (actualArgs, _) = ProcessHandlerHelper.ProcessInputArguments(argsLine);

            Assert.Equal(expectedArgs, actualArgs);
        }

        [Fact]
        public void TestWithCloseVars2()
        {
            string argsLine = "%VAR1% %VAR2%_%VAR3%";
            string expectedArgs = "value1 value2_value3";
            Environment.SetEnvironmentVariable("VAR1", "value1");
            Environment.SetEnvironmentVariable("VAR2", "value2");
            Environment.SetEnvironmentVariable("VAR3", "value3");

            var (actualArgs, _) = ProcessHandlerHelper.ProcessInputArguments(argsLine);

            Assert.Equal(expectedArgs, actualArgs);
        }

        [Fact]
        public void TestWithCloseVars3()
        {
            string argsLine = "%VAR1%%VAR2%%VAR3%";
            string expectedArgs = "123";
            Environment.SetEnvironmentVariable("VAR1", "1");
            Environment.SetEnvironmentVariable("VAR2", "2");
            Environment.SetEnvironmentVariable("VAR3", "3");

            var (actualArgs, _) = ProcessHandlerHelper.ProcessInputArguments(argsLine);

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

            var (actualArgs, _) = ProcessHandlerHelper.ProcessInputArguments(argsLine);

            Assert.Equal(expectedArgs, actualArgs);
        }
    }
}
