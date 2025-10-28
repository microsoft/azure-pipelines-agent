// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.Services.Agent.Util;
using Agent.Sdk;
using Agent.Sdk.Knob;
using Moq;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Util
{
    public class VarUtilL0
    {
        [Theory]
        [InlineData("test.value1", "TEST_VALUE1", false)]
        [InlineData("test value2", "TEST_VALUE2", false)]
        [InlineData("tesT vaLue.3", "TEST_VALUE_3", false)]
        [InlineData(".tesT vaLue 4", "_TEST_VALUE_4", false)]
        [InlineData("TEST_VALUE_5", "TEST_VALUE_5", false)]
        [InlineData(".. TEST   VALUE. 6", "___TEST___VALUE__6", false)]
        [InlineData(null, "", false)]
        [InlineData("", "", false)]
        [InlineData(" ", "_", false)]
        [InlineData(".", "_", false)]
        [InlineData("TestValue", "TestValue", true)]
        [InlineData("Test.Value", "Test_Value", true)]
        public void TestConverterToEnvVariableFormat(string input, string expected, bool preserveCase)
        {
            var result = VarUtil.ConvertToEnvVariableFormat(input, preserveCase);

            Assert.Equal(expected, result);
        }


        [Fact]
        public void VarUtil_HasGetServerOMPathMethod()
        {
            // Verify the method exists and can be called
            var hostContextType = typeof(IHostContext);
            var knobValueContextType = typeof(IKnobValueContext);
            var method = typeof(VarUtil).GetMethod("GetServerOMPath");
            
            Assert.NotNull(method);
            Assert.Equal(typeof(string), method.ReturnType);
            
            var parameters = method.GetParameters();
            Assert.Equal(2, parameters.Length);
            Assert.Equal(hostContextType, parameters[0].ParameterType);
            Assert.Equal(knobValueContextType, parameters[1].ParameterType);
        }

        [Fact] 
        public void VarUtil_HasGetTfPathMethod()
        {
            // Verify the method exists and can be called
            var hostContextType = typeof(IHostContext);
            var knobValueContextType = typeof(IKnobValueContext);
            var method = typeof(VarUtil).GetMethod("GetTfPath");
            
            Assert.NotNull(method);
            Assert.Equal(typeof(string), method.ReturnType);
            
            var parameters = method.GetParameters();
            Assert.Equal(2, parameters.Length);
            Assert.Equal(hostContextType, parameters[0].ParameterType);
            Assert.Equal(knobValueContextType, parameters[1].ParameterType);
        }

        [Fact]
        public void VarUtil_HasGetTfDirectoryNameMethod()
        {
            // Verify the method exists and can be called
            var knobValueContextType = typeof(IKnobValueContext);
            var method = typeof(VarUtil).GetMethod("GetTfDirectoryName");
            
            Assert.NotNull(method);
            Assert.Equal(typeof(string), method.ReturnType);
            
            var parameters = method.GetParameters();
            Assert.Equal(1, parameters.Length);
            Assert.Equal(knobValueContextType, parameters[0].ParameterType);
        }
    }
}
