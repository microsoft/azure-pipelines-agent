
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.Services.Agent.Listener.Configuration;
using System;
using System.Runtime.CompilerServices;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Listener.Configuration
{
    public class ServiceControlManagerL0
    {
        private class ServiceNameTest
        {
            public String TestName;
            public String ExpectedServiceName;
            public String ExpectedServiceDisplayName;
            public String ServiceNamePattern;
            public String ServiceDisplayPattern;
            public String AgentName;
            public String PoolName;
            public String EnvironmentName;
            public String ServerUrl;

        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "ConfigurationManagement")]
        public void CalculateServiceNameL0()
        {
            using (TestHostContext tc = CreateTestContext())
            {
                Tracing trace = tc.GetTrace();

                trace.Info("Creating service control manager");
                ServiceControlManager scm = new ServiceControlManager();
                scm.Initialize(tc);
                ServiceNameTest[] tests = new ServiceNameTest[] {
                    new ServiceNameTest {
                        TestName = "SystemD Test",
                        ServiceNamePattern = "vsts.agent.{0}.{1}.{2}.service",
                        ServiceDisplayPattern = "Azure Pipelines Agent ({0}.{1}.{2})",
                        AgentName = "foo",
                        PoolName = "pool1",
                        ServerUrl = "https://dev.azure.com/bar",
                        ExpectedServiceName = "vsts.agent.bar.pool1.foo.service",
                        ExpectedServiceDisplayName = "Azure Pipelines Agent (bar.pool1.foo)"
                    },
                    new ServiceNameTest {
                        TestName = "Long Agent/Pool Test",
                        ServiceNamePattern = "vsts.agent.{0}.{1}.{2}.service",
                        ServiceDisplayPattern = "Azure Pipelines Agent ({0}.{1}.{2})",
                        AgentName = new string('X', 40),
                        PoolName = new string('Y', 40),
                        ServerUrl = "https://dev.azure.com/bar",
                        ExpectedServiceName = "vsts.agent.bar.YYYYYYYYYYYYYYYYYYYYYYYYY.XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX.service",
                        ExpectedServiceDisplayName = "Azure Pipelines Agent (bar.YYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYY.XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX)"
                    },
                    new ServiceNameTest {
                        TestName = "Pool With Unicode Dash Test",
                        ServiceNamePattern = "vsts.agent.{0}.{1}.{2}.service",
                        ServiceDisplayPattern = "Azure Pipelines Agent ({0}.{1}.{2})",
                        AgentName = "foo",
                        PoolName = "pool" + "\u002D" + "1",
                        ServerUrl = "https://dev.azure.com/bar",
                        ExpectedServiceName = "vsts.agent.bar.pool-1.foo.service",
                        ExpectedServiceDisplayName = "Azure Pipelines Agent (bar.pool-1.foo)"
                    },
                    new ServiceNameTest {
                        TestName = "Agent For Environment Test",
                        ServiceNamePattern = "vsts.agent.{0}.{1}.{2}.service",
                        ServiceDisplayPattern = "Azure Pipelines Agent ({0}.{1}.{2})",
                        AgentName = new string('X', 40),
                        EnvironmentName = new string('Y',40),
                        ServerUrl = "https://dev.azure.com/bar",
                        ExpectedServiceName = "vsts.agent.bar.YYYYYYYYYYYYYYYYYYYYYYYYY.XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX.service",
                        ExpectedServiceDisplayName = "Azure Pipelines Agent (bar.YYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYY.XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX)"
                    },
                };
                foreach (var test in tests)
                {
                    AgentSettings settings = new AgentSettings();
                    settings.ServerUrl = test.ServerUrl;
                    settings.AgentName = test.AgentName;
                    settings.PoolName = test.PoolName;
                    settings.EnvironmentName = test.EnvironmentName;

                    scm.CalculateServiceName(settings, test.ServiceNamePattern, test.ServiceDisplayPattern, out string serviceName, out string serviceDisplayName);

                    Assert.True(string.Equals(serviceName, test.ExpectedServiceName), $"{test.TestName} Service Name Expected: {test.ExpectedServiceName}, Got: {serviceName}");
                    Assert.True(serviceName.Length <= 80, $"{test.TestName} Service Name is <= 80");
                    Assert.True(string.Equals(serviceDisplayName, test.ExpectedServiceDisplayName), $"{test.TestName} Service Display Name Expected: {test.ExpectedServiceDisplayName}, Got: {serviceDisplayName}");
                }
            }
        }

        private TestHostContext CreateTestContext([CallerMemberName] String testName = "")
        {
            TestHostContext tc = new TestHostContext(this, testName);
            return tc;
        }
    }
}