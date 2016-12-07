using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Services.Agent.Listener.Configuration;
using Moq;
using Xunit;
using System.Security.Principal;
using Microsoft.VisualStudio.Services.Agent;
using Microsoft.VisualStudio.Services.Agent.Tests;

namespace Test.L0.Listener.Configuration
{
    public class NativeWindowsServiceHelperL0
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "ConfigurationManagement")]
        public void EnsureCorrectDefaultServiceAccountIsCorrectForBuildAndReleaseAgent()
        {
            using (TestHostContext tc = new TestHostContext(this, "EnsureCorrectDefaultServiceAccountIsCorrectForBuildAndReleaseAgent"))
            {
                Tracing trace = tc.GetTrace();

                trace.Info("Creating an instance of the NativeWindowsServiceHelper class");
                var windowsServiceHelper = new NativeWindowsServiceHelper();

                trace.Info("Trying to get the Default Service Account when a BuildRelease Agent is being configured");
                var defaultServiceAccount = windowsServiceHelper.GetDefaultServiceAccount(Constants.Agent.AgentConfigurationProvider.BuildReleasesAgentConfiguration);
                Assert.True(defaultServiceAccount.ToString().Equals(@"NT AUTHORITY\NETWORK SERVICE"), "If agent is getting configured as build-release agent, default service accout should be 'NT AUTHORITY\\NETWORK SERVICE'");
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "ConfigurationManagement")]
        public void EnsureCorrectDefaultServiceAccountIsCorrectForDeploymentAgent()
        {
            using (TestHostContext tc = new TestHostContext(this, "EnsureCorrectDefaultServiceAccountIsCorrectForDeploymentAgent"))
            {
                Tracing trace = tc.GetTrace();

                trace.Info("Creating an instance of the NativeWindowsServiceHelper class");
                var windowsServiceHelper = new NativeWindowsServiceHelper();

                trace.Info("Trying to get the Default Service Account when a DeploymentAgent is being configured");
                var defaultServiceAccount = windowsServiceHelper.GetDefaultServiceAccount(Constants.Agent.AgentConfigurationProvider.DeploymentAgentConfiguration);
                Assert.True(defaultServiceAccount.ToString().Equals(@"NT AUTHORITY\SYSTEM"), "If agent is getting configured as deployment agent, default service accout should be 'NT AUTHORITY\\SYSTEM'");
            }
        }

    }
}
