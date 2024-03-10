// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.TeamFoundation.TestClient.PublishTestResults;
using Microsoft.TeamFoundation.TestManagement.WebApi;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.Telemetry;
using Microsoft.VisualStudio.Services.Agent.Worker.TestResults;
using Microsoft.VisualStudio.Services.Agent.Worker.TestResults.Utils;
using Microsoft.VisualStudio.Services.WebPlatform;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker.TestResults
{
    public sealed class ResultsCommandTests
    {
        private Mock<IExecutionContext> _ec;
        private List<string> _warnings = new List<string>();
        private List<string> _errors = new List<string>();
        private Mock<IAsyncCommandContext> _mockCommandContext;
        private Mock<ITestDataPublisher> _mockTestRunDataPublisher;
        private Mock<IExtensionManager> _mockExtensionManager;
        private Mock<IParser> _mockParser;
        private Mock<ICustomerIntelligenceServer> _mockCustomerIntelligenceServer;
        private Mock<IFeatureFlagService> _mockFeatureFlagService;
        private Variables _variables;

        public ResultsCommandTests()
        {
            _mockTestRunDataPublisher = new Mock<ITestDataPublisher>();
            _ = _mockTestRunDataPublisher.Setup(x => x.PublishAsync(It.IsAny<TestRunContext>(), It.IsAny<List<string>>(), It.IsAny<TestCaseResult[]>(), It.IsAny<PublishOptions>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

            _mockParser = new Mock<IParser>();
            TestDataProvider mockTestRunData = MockParserData();
            _mockParser.Setup(x => x.Name).Returns("mockResults");
            _mockParser.Setup(x => x.ParseTestResultFiles(It.IsAny<IExecutionContext>(), It.IsAny<TestRunContext>(), It.IsAny<List<String>>())).Returns(mockTestRunData);

            _mockCustomerIntelligenceServer = new Mock<ICustomerIntelligenceServer>();
            _mockCustomerIntelligenceServer.Setup(x => x.PublishEventsAsync(It.IsAny<CustomerIntelligenceEvent[]>()));

            _mockFeatureFlagService = new Mock<IFeatureFlagService>();
            _mockFeatureFlagService.Setup(x => x.GetFeatureFlagState(It.IsAny<string>(), It.IsAny<Guid>())).Returns(true);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "PublishTestResults")]
        public void Publish_NullTestRunner()
        {
            using (var _hc = SetupMocks())
            {
                var resultCommand = new ResultsCommandExtension();
                resultCommand.Initialize(_hc);
                var command = new Command("results", "publish");
                command.Properties.Add("resultFiles", "ResultFile.txt");

                Assert.Throws<ArgumentException>(() => resultCommand.ProcessCommand(_ec.Object, command));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "PublishTestResults")]
        public void Publish_NullTestResultFiles()
        {
            using (var _hc = SetupMocks())
            {
                var resultCommand = new ResultsCommandExtension();
                resultCommand.Initialize(_hc);
                var command = new Command("results", "publish");
                Assert.Throws<ArgumentException>(() => resultCommand.ProcessCommand(_ec.Object, command));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "PublishTestResults")]
        public void Publish_DataIsHonoredWhenTestResultsFieldIsNotSpecified()
        {
            using (var _hc = SetupMocks())
            {
                var resultCommand = new ResultsCommandExtension();
                resultCommand.Initialize(_hc);
                var command = new Command("results", "publish");
                command.Properties.Add("type", "mockResults");
                command.Data = "testfile1,testfile2";
                resultCommand.ProcessCommand(_ec.Object, command);

                Assert.Equal(0, _errors.Count());
            }
        }

        private List<TestRun> MockTestRun()
        {
            List<TestRun> testRunList = new List<TestRun>();
            TestRun testRun = new TestRun();
            testRun.Name = "Mock test run";
            testRunList.Add(testRun);

            return testRunList;
        }

        private TestDataProvider MockParserData()
        {
            List<TestRunData> mockTestRunData = new List<TestRunData>();
            TestRunData testRunData1 = new TestRunData(new RunCreateModel("First"));
            TestRunData testRunData2 = new TestRunData(new RunCreateModel("Second"));
            var buildData1 = new BuildData()
            {
                BuildAttachments = new List<BuildAttachment>()
                {
                    new BuildAttachment() { AllowDuplicateUploads= true, Filename="file", Metadata= null, TestLogType=TestLogType.Intermediate, TestLogCompressionType = TestLogCompressionType.None }
                }
            };

            var buildData2 = new BuildData()
            {
                BuildAttachments = new List<BuildAttachment>()
                {
                    new BuildAttachment() { AllowDuplicateUploads= true, Filename="file", Metadata= null, TestLogType=TestLogType.Intermediate, TestLogCompressionType = TestLogCompressionType.None }
                }
            };

            mockTestRunData.Add(testRunData1);
            mockTestRunData.Add(testRunData2);

            return new TestDataProvider(new List<TestData>()
            {
                new TestData() { TestRunData = testRunData1, BuildData = buildData1},
                new TestData() { TestRunData = testRunData2, BuildData = buildData1}
            });
        }


        private TestHostContext SetupMocks([CallerMemberName] string name = "", bool includePipelineVariables = false)
        {
            var _hc = new TestHostContext(this, name);
            _hc.SetSingleton(new TaskRestrictionsChecker() as ITaskRestrictionsChecker);

            _hc.SetSingleton(_mockTestRunDataPublisher.Object);
            _hc.SetSingleton(_mockParser.Object);

            _hc.SetSingleton(_mockCustomerIntelligenceServer.Object);
            _hc.SetSingleton(_mockFeatureFlagService.Object);

            _mockExtensionManager = new Mock<IExtensionManager>();
            _ = _mockExtensionManager.Setup(x => x.GetExtensions<IParser>()).Returns(new List<IParser> { _mockParser.Object, new JUnitParser(), new NUnitParser() });
            _hc.SetSingleton(_mockExtensionManager.Object);

            _mockCommandContext = new Mock<IAsyncCommandContext>();
            _hc.EnqueueInstance(_mockCommandContext.Object);

            var endpointAuthorization = new EndpointAuthorization()
            {
                Scheme = EndpointAuthorizationSchemes.OAuth
            };
            _variables = new Variables(_hc, new Dictionary<string, VariableValue>(), out List<string> warnings);
            _variables.Set("build.buildId", "1");
            if (includePipelineVariables)
            {
                _variables.Set("system.jobName", "job1");
                _variables.Set("system.phaseName", "phase1");
                _variables.Set("system.stageName", "stage1");
                _variables.Set("system.jobAttempt", "1");
                _variables.Set("system.phaseAttempt", "1");
                _variables.Set("system.stageAttempt", "1");
            }
            endpointAuthorization.Parameters[EndpointAuthorizationParameters.AccessToken] = "accesstoken";

            _ec = new Mock<IExecutionContext>();
            _ec.Setup(x => x.Restrictions).Returns(new List<TaskRestrictions>());
            _ec.Setup(x => x.Endpoints).Returns(new List<ServiceEndpoint> { new ServiceEndpoint { Url = new Uri("http://dummyurl"), Name = WellKnownServiceEndpointNames.SystemVssConnection, Authorization = endpointAuthorization } });
            _ec.Setup(x => x.Variables).Returns(_variables);
            var asyncCommands = new List<IAsyncCommandContext>();
            _ec.Setup(x => x.AsyncCommands).Returns(asyncCommands);
            _ec.Setup(x => x.AddIssue(It.IsAny<Issue>()))
            .Callback<Issue>
            ((issue) =>
            {
                if (issue.Type == IssueType.Warning)
                {
                    _warnings.Add(issue.Message);
                }
                else if (issue.Type == IssueType.Error)
                {
                    _errors.Add(issue.Message);
                }
            });
            _ec.Setup(x => x.GetHostContext()).Returns(_hc);

            return _hc;
        }
    }
}
