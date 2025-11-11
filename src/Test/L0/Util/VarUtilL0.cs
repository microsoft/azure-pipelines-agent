// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.Services.Agent.Util;
using Agent.Sdk;
using Agent.Sdk.Knob;
using Moq;
using System;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Util
{
    public class VarUtilL0
    {
        [Theory]
        [Trait("Level", "L0")]
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

        [Theory]
        [Trait("Level", "L0")]
        [InlineData("false", "false", "tf")]           // Default: both false → ServerOMDirectory (tf)
        [InlineData("true", "false", "tf-latest")]     // UseLatest only → TfLatestDirectory  
        [InlineData("false", "true", "tf-legacy")]     // UseLegacy only → TfLegacyDirectory
        [InlineData("true", "true", "tf-latest")]      // Both true → TfLatestDirectory (latest wins)
        public void TestGetTfDirectoryPath(string useLatest, string useLegacy, string expectedDirectory)
        {
            // Arrange
            using (TestHostContext hc = new TestHostContext(this))
            {
                try
                {
                    // Set environment variables based on test parameters
                    Environment.SetEnvironmentVariable("AGENT_USE_LATEST_TF_EXE", useLatest);
                    Environment.SetEnvironmentVariable("AGENT_INSTALL_LEGACY_TF_EXE", useLegacy);

                    // Create a mock IKnobValueContext that returns the Agent.HomeDirectory
                    var mockContext = new Mock<IKnobValueContext>();
                    mockContext.Setup(x => x.GetVariableValueOrDefault(Constants.Variables.Agent.HomeDirectory))
                              .Returns(hc.GetDirectory(WellKnownDirectory.Root));
                    mockContext.Setup(x => x.GetScopedEnvironment())
                              .Returns(new SystemEnvironment());

                    // Act
                    var result = VarUtil.GetTfDirectoryPath(mockContext.Object);

                    // Assert
                    Assert.NotNull(result);
                    Assert.Contains("externals", result);
                    Assert.Contains(expectedDirectory, result);
                    
                    // Ensure we don't get unexpected directories
                    if (expectedDirectory == "tf")
                    {
                        Assert.DoesNotContain("tf-latest", result);
                        Assert.DoesNotContain("tf-legacy", result);
                    }
                    else if (expectedDirectory == "tf-latest")
                    {
                        Assert.DoesNotContain("tf-legacy", result);
                        Assert.DoesNotContain("\\tf\\", result);  // Ensure it's not the base tf directory
                    }
                    else if (expectedDirectory == "tf-legacy")
                    {
                        Assert.DoesNotContain("tf-latest", result);
                        Assert.DoesNotContain("\\tf\\", result);  // Ensure it's not the base tf directory
                    }
                }
                finally
                {
                    // Clean up environment variables
                    Environment.SetEnvironmentVariable("AGENT_USE_LATEST_TF_EXE", null);
                    Environment.SetEnvironmentVariable("AGENT_INSTALL_LEGACY_TF_EXE", null);
                }
            }
        }

        [Theory]
        [Trait("Level", "L0")]
        [InlineData("false", "false", "vstshost")]        // Default: both false → LegacyPSHostDirectory (vstshost)
        [InlineData("true", "false", "vstshost")]         // UseLatest only → LegacyPSHostDirectory (vstshost)
        [InlineData("false", "true", "vstshost-legacy")]  // UseLegacy only → LegacyPSHostLegacyDirectory
        [InlineData("true", "true", "vstshost")]          // Both true → LegacyPSHostDirectory (vstshost)
        public void TestGetLegacyPowerShellHostDirectoryPath(string useLatest, string useLegacy, string expectedDirectory)
        {
            // Arrange
            using (TestHostContext hc = new TestHostContext(this))
            {
                try
                {
                    // Set environment variables based on test parameters
                    Environment.SetEnvironmentVariable("AGENT_USE_LATEST_TF_EXE", useLatest);
                    Environment.SetEnvironmentVariable("AGENT_INSTALL_LEGACY_TF_EXE", useLegacy);

                    // Create a mock IKnobValueContext that returns the Agent.HomeDirectory
                    var mockContext = new Mock<IKnobValueContext>();
                    mockContext.Setup(x => x.GetVariableValueOrDefault(Constants.Variables.Agent.HomeDirectory))
                              .Returns(hc.GetDirectory(WellKnownDirectory.Root));
                    mockContext.Setup(x => x.GetScopedEnvironment())
                              .Returns(new SystemEnvironment());

                    // Act
                    var result = VarUtil.GetLegacyPowerShellHostDirectoryPath(mockContext.Object);

                    // Assert
                    Assert.NotNull(result);
                    Assert.Contains("externals", result);
                    Assert.Contains(expectedDirectory, result);
                    
                    // Ensure we don't get unexpected directories
                    if (expectedDirectory == "vstshost")
                    {
                        Assert.DoesNotContain("vstshost-legacy", result);
                    }
                    else if (expectedDirectory == "vstshost-legacy")
                    {
                        Assert.DoesNotContain("\\vstshost\\", result);  // Ensure it's not the base vstshost directory
                    }
                }
                finally
                {
                    // Clean up environment variables
                    Environment.SetEnvironmentVariable("AGENT_USE_LATEST_TF_EXE", null);
                    Environment.SetEnvironmentVariable("AGENT_INSTALL_LEGACY_TF_EXE", null);
                }
            }
        }

        [Theory]
        [Trait("Level", "L0")]
        [InlineData("false", "false", false, false)] // Default: both false
        [InlineData("true", "false", true, false)]   // UseLatest only
        [InlineData("false", "true", false, true)]   // UseLegacy only
        [InlineData("true", "true", true, true)]     // Both true
        public void TestGetKnobsAndExternalsPath(string useLatest, string useLegacy, bool expectedUseLatest, bool expectedUseLegacy)
        {
            // Arrange
            using (TestHostContext hc = new TestHostContext(this))
            {
                try
                {
                    // Set environment variables based on test parameters
                    Environment.SetEnvironmentVariable("AGENT_USE_LATEST_TF_EXE", useLatest);
                    Environment.SetEnvironmentVariable("AGENT_INSTALL_LEGACY_TF_EXE", useLegacy);

                    // Create a mock IKnobValueContext that returns the Agent.HomeDirectory
                    var mockContext = new Mock<IKnobValueContext>();
                    mockContext.Setup(x => x.GetVariableValueOrDefault(Constants.Variables.Agent.HomeDirectory))
                              .Returns(hc.GetDirectory(WellKnownDirectory.Root));
                    mockContext.Setup(x => x.GetScopedEnvironment())
                              .Returns(new SystemEnvironment());

                    // Use reflection to access the private method
                    var method = typeof(VarUtil).GetMethod("GetKnobsAndExternalsPath", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    Assert.NotNull(method);

                    // Act - use the mock context instead of TestHostContext
                    var result = method.Invoke(null, new object[] { mockContext.Object });
                    
                    // Use reflection to access the tuple properties (useLatest, useLegacy, externalsPath)
                    var resultType = result.GetType();
                    var useLatestProperty = resultType.GetField("Item1");
                    var useLegacyProperty = resultType.GetField("Item2");
                    var externalsPathProperty = resultType.GetField("Item3");

                    var actualUseLatest = (bool)useLatestProperty.GetValue(result);
                    var actualUseLegacy = (bool)useLegacyProperty.GetValue(result);
                    var actualExternalsPath = (string)externalsPathProperty.GetValue(result);

                    // Assert
                    Assert.Equal(expectedUseLatest, actualUseLatest);
                    Assert.Equal(expectedUseLegacy, actualUseLegacy);
                    Assert.NotNull(actualExternalsPath);
                    Assert.Contains("externals", actualExternalsPath);
                    Assert.True(actualExternalsPath.EndsWith("externals"));
                }
                finally
                {
                    // Clean up environment variables
                    Environment.SetEnvironmentVariable("AGENT_USE_LATEST_TF_EXE", null);
                    Environment.SetEnvironmentVariable("AGENT_INSTALL_LEGACY_TF_EXE", null);
                }
            }
        }


    }
}
