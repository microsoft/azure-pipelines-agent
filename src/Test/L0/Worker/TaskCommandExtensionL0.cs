// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Agent.Sdk;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Moq;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker
{
    public sealed class TaskCommandExtensionL0
    {
        private Mock<IExecutionContext> _ec;
        private ServiceEndpoint _endpoint;

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void SetEndpointAuthParameter()
        {
            using (var _hc = SetupMocks())
            {
                TaskCommandExtension commandExtension = new TaskCommandExtension();
                commandExtension.Initialize(_hc);
                var cmd = new Command("task", "setEndpoint");
                cmd.Data = "blah";
                cmd.Properties.Add("field", "authParameter");
                cmd.Properties.Add("id", Guid.Empty.ToString());
                cmd.Properties.Add("key", "test");

                commandExtension.ProcessCommand(_ec.Object, cmd);

                Assert.Equal(_endpoint.Authorization.Parameters["test"], "blah");
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void SetEndpointDataParameter()
        {
            using (var _hc = SetupMocks())
            {
                TaskCommandExtension commandExtension = new TaskCommandExtension();
                var cmd = new Command("task", "setEndpoint");
                cmd.Data = "blah";
                cmd.Properties.Add("field", "dataParameter");
                cmd.Properties.Add("id", Guid.Empty.ToString());
                cmd.Properties.Add("key", "test");

                commandExtension.ProcessCommand(_ec.Object, cmd);

                Assert.Equal(_endpoint.Data["test"], "blah");
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void SetEndpointUrlParameter()
        {
            using (var _hc = SetupMocks())
            {
                TaskCommandExtension commandExtension = new TaskCommandExtension();
                var cmd = new Command("task", "setEndpoint");
                cmd.Data = "http://blah/";
                cmd.Properties.Add("field", "url");
                cmd.Properties.Add("id", Guid.Empty.ToString());

                commandExtension.ProcessCommand(_ec.Object, cmd);

                Assert.Equal(_endpoint.Url.ToString(), cmd.Data);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void SetEndpointWithoutValue()
        {
            using (var _hc = SetupMocks())
            {
                TaskCommandExtension commandExtension = new TaskCommandExtension();
                var cmd = new Command("task", "setEndpoint");
                Assert.Throws<ArgumentNullException>(() => commandExtension.ProcessCommand(_ec.Object, cmd));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void SetEndpointWithoutEndpointField()
        {
            using (var _hc = SetupMocks())
            {
                TaskCommandExtension commandExtension = new TaskCommandExtension();
                var cmd = new Command("task", "setEndpoint");

                Assert.Throws<ArgumentNullException>(() => commandExtension.ProcessCommand(_ec.Object, cmd));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void SetEndpointInvalidEndpointField()
        {
            using (var _hc = SetupMocks())
            {
                TaskCommandExtension commandExtension = new TaskCommandExtension();
                var cmd = new Command("task", "setEndpoint");
                cmd.Properties.Add("field", "blah");

                Assert.Throws<ArgumentNullException>(() => commandExtension.ProcessCommand(_ec.Object, cmd));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void SetEndpointWithoutEndpointId()
        {
            using (var _hc = SetupMocks())
            {
                TaskCommandExtension commandExtension = new TaskCommandExtension();
                var cmd = new Command("task", "setEndpoint");
                cmd.Properties.Add("field", "url");

                Assert.Throws<ArgumentNullException>(() => commandExtension.ProcessCommand(_ec.Object, cmd));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void SetEndpointInvalidEndpointId()
        {
            using (var _hc = SetupMocks())
            {
                TaskCommandExtension commandExtension = new TaskCommandExtension();
                var cmd = new Command("task", "setEndpoint");
                cmd.Properties.Add("field", "url");
                cmd.Properties.Add("id", "blah");

                Assert.Throws<ArgumentNullException>(() => commandExtension.ProcessCommand(_ec.Object, cmd));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void SetEndpointIdWithoutEndpointKey()
        {
            using (var _hc = SetupMocks())
            {
                TaskCommandExtension commandExtension = new TaskCommandExtension();
                var cmd = new Command("task", "setEndpoint");
                cmd.Properties.Add("field", "authParameter");
                cmd.Properties.Add("id", Guid.Empty.ToString());

                Assert.Throws<ArgumentNullException>(() => commandExtension.ProcessCommand(_ec.Object, cmd));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void SetEndpointUrlWithInvalidValue()
        {
            using (var _hc = SetupMocks())
            {
                TaskCommandExtension commandExtension = new TaskCommandExtension();
                var cmd = new Command("task", "setEndpoint");
                cmd.Data = "blah";
                cmd.Properties.Add("field", "url");
                cmd.Properties.Add("id", Guid.Empty.ToString());

                Assert.Throws<ArgumentNullException>(() => commandExtension.ProcessCommand(_ec.Object, cmd));
            }
        }

        private TestHostContext SetupMocks([CallerMemberName] string name = "")
        {
            var _hc = new TestHostContext(this, name);
            _hc.SetSingleton(new TaskRestrictionsChecker() as ITaskRestrictionsChecker);
            _ec = new Mock<IExecutionContext>();

            _endpoint = new ServiceEndpoint()
            {
                Id = Guid.Empty,
                Url = new Uri("https://test.com"),
                Authorization = new EndpointAuthorization()
                {
                    Scheme = "Test",
                }
            };

            _ec.Setup(x => x.Endpoints).Returns(new List<ServiceEndpoint> { _endpoint });
            _ec.Setup(x => x.GetHostContext()).Returns(_hc);
            _ec.Setup(x => x.GetScopedEnvironment()).Returns(new SystemEnvironment());

            return _hc;
        }
    }
}