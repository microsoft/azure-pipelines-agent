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
                cmd.Properties.Add("id", "11111111-1111-1111-1111-111111111111");
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
                cmd.Properties.Add("id", "11111111-1111-1111-1111-111111111111");
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
                cmd.Properties.Add("id", "11111111-1111-1111-1111-111111111111");

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
        public void SetEndpointServiceConnectionNotDeclared()
        {
            using (var _hc = SetupMocks())
            {
                TaskCommandExtension commandExtension = new TaskCommandExtension();
                var cmd = new Command("task", "setEndpoint");
                cmd.Data = "http://example.com/";
                cmd.Properties.Add("field", "url");
                // Use a valid GUID that is not in the Endpoints list (SetupMocks only adds Guid.Empty)
                var testEndpointId = "12345678-1234-1234-1234-123456789012";
                cmd.Properties.Add("id", testEndpointId);

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
                cmd.Properties.Add("id", "11111111-1111-1111-1111-111111111111");

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
                cmd.Properties.Add("id", "11111111-1111-1111-1111-111111111111");

                Assert.Throws<ArgumentNullException>(() => commandExtension.ProcessCommand(_ec.Object, cmd));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void IssueSourceValidationSuccessed()
        {
            using (var _hc = SetupMocks())
            {
                TaskCommandExtension commandExtension = new TaskCommandExtension();

                var testCorrelationId = Guid.NewGuid().ToString();

                _ec.Setup(x => x.JobSettings).Returns(new Dictionary<string, string> { { WellKnownJobSettings.CommandCorrelationId, testCorrelationId } }); 
                
                var cmd = new Command("task", "issue");
                cmd.Data = "test error";
                cmd.Properties.Add("source", "CustomerScript");
                cmd.Properties.Add("correlationId", testCorrelationId);
                cmd.Properties.Add("type", "error");

                Issue currentIssue = null;

                _ec.Setup(x => x.AddIssue(It.IsAny<Issue>())).Callback((Issue issue) => currentIssue = issue);
                _ec.Setup(x => x.GetVariableValueOrDefault("ENABLE_ISSUE_SOURCE_VALIDATION")).Returns("true");

                commandExtension.ProcessCommand(_ec.Object, cmd);
                Assert.Equal("test error", currentIssue.Message);
                Assert.Equal("CustomerScript", currentIssue.Data["source"]);
                Assert.Equal("error", currentIssue.Data["type"]);
                Assert.Equal(false, currentIssue.Data.ContainsKey("correlationId"));
                Assert.Equal(IssueType.Error, currentIssue.Type);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void IssueSourceValidationFailedBecauseCorrelationIdWasInvalid()
        {
            using (var _hc = SetupMocks())
            {
                TaskCommandExtension commandExtension = new TaskCommandExtension();

                var testCorrelationId = Guid.NewGuid().ToString();

                _ec.Setup(x => x.JobSettings).Returns(new Dictionary<string, string> { { WellKnownJobSettings.CommandCorrelationId, testCorrelationId } });

                var cmd = new Command("task", "issue");
                cmd.Data = "test error";
                cmd.Properties.Add("source", "CustomerScript");
                cmd.Properties.Add("correlationId", Guid.NewGuid().ToString());
                cmd.Properties.Add("type", "error");

                Issue currentIssue = null;
                string debugMsg = null;

                _ec.Setup(x => x.AddIssue(It.IsAny<Issue>())).Callback((Issue issue) => currentIssue = issue);
                _ec.Setup(x => x.WriteDebug).Returns(true);
                _ec.Setup(x => x.Write(WellKnownTags.Debug, It.IsAny<string>(), It.IsAny<bool>()))
                   .Callback((string tag, string message, bool maskSecrets) => debugMsg = message);
                _ec.Setup(x => x.GetVariableValueOrDefault("ENABLE_ISSUE_SOURCE_VALIDATION")).Returns("true");

                commandExtension.ProcessCommand(_ec.Object, cmd);
                Assert.Equal("test error", currentIssue.Message);
                Assert.Equal(false, currentIssue.Data.ContainsKey("source"));
                Assert.Equal("error", currentIssue.Data["type"]);
                Assert.Equal(false, currentIssue.Data.ContainsKey("correlationId"));
                Assert.Equal(IssueType.Error, currentIssue.Type);
                Assert.Equal(debugMsg, "The task provided an invalid correlation ID when using the task.issue command.");
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void IssueSourceValidationFailedBecauseCorrelationIdWasAbsent()
        {
            using (var _hc = SetupMocks())
            {
                TaskCommandExtension commandExtension = new TaskCommandExtension();

                var testCorrelationId = Guid.NewGuid().ToString();

                _ec.Setup(x => x.JobSettings).Returns(new Dictionary<string, string> { { WellKnownJobSettings.CommandCorrelationId, testCorrelationId } });

                var cmd = new Command("task", "issue");
                cmd.Data = "test error";
                cmd.Properties.Add("type", "error");
                cmd.Properties.Add("source", "TaskInternal");

                Issue currentIssue = null;

                _ec.Setup(x => x.AddIssue(It.IsAny<Issue>())).Callback((Issue issue) => currentIssue = issue);
                _ec.Setup(x => x.GetVariableValueOrDefault("ENABLE_ISSUE_SOURCE_VALIDATION")).Returns("true");

                commandExtension.ProcessCommand(_ec.Object, cmd);
                Assert.Equal("test error", currentIssue.Message);
                Assert.Equal(false, currentIssue.Data.ContainsKey("source"));
                Assert.Equal("error", currentIssue.Data["type"]);
                Assert.Equal(IssueType.Error, currentIssue.Type);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void SetEndpoint_BlocksUrlModification_OnSystemEndpoint()
        {
            using (var _hc = SetupMocks())
            {
                var systemEndpoint = new ServiceEndpoint()
                {
                    Id = Guid.Empty,
                    Name = "SystemVssConnection",
                    Url = new Uri("https://dev.azure.com/myorg"),
                    Authorization = new EndpointAuthorization() { Scheme = "OAuth" }
                };
                _ec.Setup(x => x.Endpoints).Returns(new List<ServiceEndpoint> { _endpoint, systemEndpoint });

                TaskCommandExtension commandExtension = new TaskCommandExtension();
                commandExtension.Initialize(_hc);

                var cmd = new Command("task", "setEndpoint");
                cmd.Data = "http://evil.com/";
                cmd.Properties.Add("field", "url");
                cmd.Properties.Add("id", Guid.Empty.ToString());

                commandExtension.ProcessCommand(_ec.Object, cmd);

                Assert.Equal(new Uri("https://dev.azure.com/myorg"), systemEndpoint.Url);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void SetEndpoint_BlocksAuthParameter_OnSystemEndpoint()
        {
            using (var _hc = SetupMocks())
            {
                var systemEndpoint = new ServiceEndpoint()
                {
                    Id = Guid.Empty,
                    Name = "SystemVssConnection",
                    Url = new Uri("https://dev.azure.com/myorg"),
                    Authorization = new EndpointAuthorization() { Scheme = "OAuth" }
                };
                _ec.Setup(x => x.Endpoints).Returns(new List<ServiceEndpoint> { _endpoint, systemEndpoint });

                TaskCommandExtension commandExtension = new TaskCommandExtension();
                commandExtension.Initialize(_hc);

                var cmd = new Command("task", "setEndpoint");
                cmd.Data = "stolen-token";
                cmd.Properties.Add("field", "authParameter");
                cmd.Properties.Add("id", Guid.Empty.ToString());
                cmd.Properties.Add("key", "AccessToken");

                commandExtension.ProcessCommand(_ec.Object, cmd);

                Assert.False(systemEndpoint.Authorization.Parameters.ContainsKey("AccessToken"));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void SetEndpoint_AllowsModification_OnUserEndpoint()
        {
            using (var _hc = SetupMocks())
            {
                var userEndpointId = Guid.NewGuid();
                var userEndpoint = new ServiceEndpoint()
                {
                    Id = userEndpointId,
                    Url = new Uri("https://original.com"),
                    Authorization = new EndpointAuthorization() { Scheme = "Test" }
                };
                _ec.Setup(x => x.Endpoints).Returns(new List<ServiceEndpoint> { _endpoint, userEndpoint });

                TaskCommandExtension commandExtension = new TaskCommandExtension();
                commandExtension.Initialize(_hc);

                var cmd = new Command("task", "setEndpoint");
                cmd.Data = "http://newurl.com/";
                cmd.Properties.Add("field", "url");
                cmd.Properties.Add("id", userEndpointId.ToString());

                commandExtension.ProcessCommand(_ec.Object, cmd);

                Assert.Equal(new Uri("http://newurl.com/"), userEndpoint.Url);
            }
        }

        [Theory]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        [InlineData("logissue", "/root/.nuget/packages/example/build/package.targets")]
        [InlineData("issue", "/root/.nuget/packages/example/build/package.targets")]
        [InlineData("ISSUE", "/root/.nuget/packages/example/build/package.targets")]
        [InlineData("logissue", "src/missing.cs")]
        [InlineData("issue", "src/missing.cs")]
        [InlineData("logissue", "")]
        [InlineData("issue", "")]
        [InlineData("logissue", null)]
        [InlineData("issue", null)]
        public void LogIssue_UsesDiagnosticTranslation(string commandName, string sourcePath)
        {
            using (var hc = SetupMocks())
            {
                SetupIssueExtension(hc, hasExtension: true);
                _ec.Setup(x => x.TranslateToHostPath(It.IsAny<string>(), It.IsAny<VsoPathTranslationSource>()))
                    .Throws(new InvalidOperationException("Unexpected path-translation source for a diagnostic."));
                _ec.Setup(x => x.TranslateToHostPath(It.IsAny<string>(), VsoPathTranslationSource.TaskLogIssueSourcePath))
                    .Returns((string path, VsoPathTranslationSource caller) => path);
                var extension = new TaskCommandExtension();
                extension.Initialize(hc);
                var command = new Command("task", commandName) { Data = "Build warning" };
                command.Properties["type"] = "warning";
                command.Properties["sourcepath"] = sourcePath;
                command.Properties["linenumber"] = "12";
                command.Properties["columnnumber"] = "3";
                command.Properties["code"] = "TEST001";

                extension.ProcessCommand(_ec.Object, command);

                _ec.Verify(x => x.TranslateToHostPath(sourcePath, VsoPathTranslationSource.TaskLogIssueSourcePath), Times.Once);
                _ec.Verify(x => x.TranslateToHostPath(It.IsAny<string>(), It.IsAny<VsoPathTranslationSource>()), Times.Once);
                _ec.Verify(x => x.AddIssue(It.Is<Issue>(issue =>
                    issue.Type == IssueType.Warning &&
                    issue.Category == "Code" &&
                    issue.Data["sourcepath"] == sourcePath &&
                    issue.Message == $"{sourcePath}(12,3): warning TEST001: Build warning")), Times.Once);
            }
        }

        [Theory]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        [InlineData("logissue", false, true)]
        [InlineData("issue", false, true)]
        [InlineData("logissue", true, false)]
        [InlineData("issue", true, false)]
        public void LogIssue_WithoutSourceOrExtensionPreservesBehavior(string commandName, bool hasSourcePath, bool hasExtension)
        {
            using (var hc = SetupMocks())
            {
                SetupIssueExtension(hc, hasExtension);
                var extension = new TaskCommandExtension();
                extension.Initialize(hc);
                var command = new Command("task", commandName) { Data = "Build warning" };
                command.Properties["type"] = "warning";
                if (hasSourcePath)
                {
                    command.Properties["sourcepath"] = "src/file.cs";
                }

                extension.ProcessCommand(_ec.Object, command);

                _ec.Verify(x => x.TranslateToHostPath(It.IsAny<string>(), It.IsAny<VsoPathTranslationSource>()), Times.Never);
                _ec.Verify(x => x.AddIssue(It.Is<Issue>(issue =>
                    issue.Message == "Build warning" &&
                    issue.Category == (hasSourcePath ? "Code" : "General"))), Times.Once);
            }
        }

        [Theory]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        [InlineData("addattachment", VsoPathTranslationSource.TaskAddAttachment)]
        [InlineData("uploadfile", VsoPathTranslationSource.TaskUploadFile)]
        [InlineData("uploadsummary", VsoPathTranslationSource.TaskUploadSummary)]
        public void AttachmentCommands_KeepFileAuthorization(string commandName, VsoPathTranslationSource source)
        {
            using (var hc = SetupMocks())
            {
                var denied = new InvalidOperationException("Outside Work.");
                _ec.Setup(x => x.TranslateToHostPath("outside.txt", source)).Throws(denied);
                var extension = new TaskCommandExtension();
                extension.Initialize(hc);
                var command = new Command("task", commandName) { Data = "outside.txt" };
                command.Properties["type"] = "test";
                command.Properties["name"] = "attachment";
                command.Properties["source"] = "untrusted";

                Assert.Same(denied, Assert.Throws<InvalidOperationException>(() => extension.ProcessCommand(_ec.Object, command)));

                _ec.Verify(x => x.TranslateToHostPath("outside.txt", source), Times.Once);
                _ec.Verify(x => x.TranslateToHostPath(It.IsAny<string>(), It.IsAny<VsoPathTranslationSource>()), Times.Once);
                _ec.Verify(x => x.QueueAttachFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            }
        }

        private void SetupIssueExtension(TestHostContext hc, bool hasExtension)
        {
            var variables = new Variables(hc, new Dictionary<string, VariableValue>
            {
                [Constants.Variables.System.HostType] = "build"
            }, out _);
            _ec.Setup(x => x.Variables).Returns(variables);
            _ec.Setup(x => x.GetVariableValueOrDefault("DistributedTask.Agent.EnableIssueSourceValidation")).Returns("false");
            var extension = new Mock<IJobExtension>();
            extension.Setup(x => x.HostType).Returns(HostTypes.Build);
            string repoName = "";
            string relativeSourcePath = "";
            extension.Setup(x => x.ConvertLocalPath(_ec.Object, It.IsAny<string>(), out repoName, out relativeSourcePath));
            var extensionManager = new Mock<IExtensionManager>();
            extensionManager.Setup(x => x.GetExtensions<IJobExtension>())
                .Returns(hasExtension ? new List<IJobExtension> { extension.Object } : new List<IJobExtension>());
            hc.SetSingleton(extensionManager.Object);
        }

        private TestHostContext SetupMocks([CallerMemberName] string name = "")
        {
            var _hc = new TestHostContext(this, name);
            _hc.SetSingleton(new TaskRestrictionsChecker() as ITaskRestrictionsChecker);
            _ec = new Mock<IExecutionContext>();

            _endpoint = new ServiceEndpoint()
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
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
