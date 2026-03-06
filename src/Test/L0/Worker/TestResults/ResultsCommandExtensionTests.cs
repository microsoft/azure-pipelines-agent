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
        private TestDataPublisher _publisher;

        public ResultsCommandTests()
        {
            _mockTestRunDataPublisher = new Mock<ITestDataPublisher>();
            _mockTestRunDataPublisher.Setup(x => x.PublishAsync(It.IsAny<TestRunContext>(), It.IsAny<List<string>>(), It.IsAny<PublishOptions>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

            _mockParser = new Mock<IParser>();
            TestDataProvider mockTestRunData = MockParserData();
            _mockParser.Setup(x => x.Name).Returns("mockResults");
            _mockParser.Setup(x => x.ParseTestResultFiles(It.IsAny<IExecutionContext>(), It.IsAny<TestRunContext>(), It.IsAny<List<String>>())).Returns(mockTestRunData);

            _mockCustomerIntelligenceServer = new Mock<ICustomerIntelligenceServer>();
            _mockCustomerIntelligenceServer.Setup(x => x.PublishEventsAsync(It.IsAny<CustomerIntelligenceEvent[]>()));

            _mockFeatureFlagService = new Mock<IFeatureFlagService>();
            _mockFeatureFlagService.Setup(x => x.GetFeatureFlagState(It.IsAny<string>(), It.IsAny<Guid>())).Returns(true);
            _publisher = new TestDataPublisher();
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
            _mockExtensionManager.Setup(x => x.GetExtensions<IParser>()).Returns(new List<IParser> { _mockParser.Object, new JUnitParser(), new NUnitParser() });
            _hc.SetSingleton(_mockExtensionManager.Object);

            _mockCommandContext = new Mock<IAsyncCommandContext>();
            _hc.EnqueueInstance(_mockCommandContext.Object);

            var endpointAuthorization = new EndpointAuthorization()
            {
                Scheme = EndpointAuthorizationSchemes.OAuth
            };
            List<string> warnings;
            _variables = new Variables(_hc, new Dictionary<string, VariableValue>(), out warnings);
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

        #region Helper methods

        /// <summary>
        /// Creates a TestRunData with the given run ID, start date, and test results.
        /// </summary>
        private static TestRunData CreateTestRunData(
            string runId,
            DateTime startDate,
            params (string automatedTestName, string testCaseTitle, string outcome)[] results)
        {
            var testRunData = new TestRunData(new RunCreateModel(runId ?? "Run"))
            {
                TestRunIdFromAttachmentFile = runId,
                TestRunStartDate = startDate,
                TestResults = new List<TestCaseResultData>()
            };

            foreach (var (automatedTestName, testCaseTitle, outcome) in results)
            {
                testRunData.TestResults.Add(new TestCaseResultData
                {
                    AutomatedTestName = automatedTestName,
                    TestCaseTitle = testCaseTitle,
                    Outcome = outcome
                });
            }

            return testRunData;
        }

        #endregion

        #region DetectAndSetRetriesForTestRun tests

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "TestRetryHelper")]
        public void DetectAndSetRetries_NullList_DoesNotThrow()
        {
            // Act & Assert – should not throw
            _publisher.DetectAndSetRetriesForTestRun(null);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "TestRetryHelper")]
        public void DetectAndSetRetries_EmptyList_DoesNotThrow()
        {
            var list = new List<TestRunData>();

            _publisher.DetectAndSetRetriesForTestRun(list);

            Assert.Empty(list);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "TestRetryHelper")]
        public void DetectAndSetRetries_TwoRunsDifferentIds_NoGrouping()
        {
            var run1 = CreateTestRunData("runA", DateTime.UtcNow, ("Test1", "Title1", "Passed"));
            var run2 = CreateTestRunData("runB", DateTime.UtcNow, ("Test2", "Title2", "Failed"));
            var list = new List<TestRunData> { run1, run2 };

            _publisher.DetectAndSetRetriesForTestRun(list);

            Assert.Equal(2, list.Count);
            Assert.Null(run1.Retries);
            Assert.Null(run2.Retries);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "TestRetryHelper")]
        public void DetectAndSetRetries_MultipleGroups_GroupedCorrectly()
        {
            var dateA1 = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
            var dateA2 = new DateTime(2025, 1, 1, 11, 0, 0, DateTimeKind.Utc);
            var dateB1 = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
            var dateB2 = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            var runA1 = CreateTestRunData("groupA", dateA1, ("TestA", "TitleA", "Failed"));
            var runA2 = CreateTestRunData("groupA", dateA2, ("TestA", "TitleA", "Passed"));
            var runB1 = CreateTestRunData("groupB", dateB1, ("TestB", "TitleB", "Failed"));
            var runB2 = CreateTestRunData("groupB", dateB2, ("TestB", "TitleB", "Passed"));

            var list = new List<TestRunData> { runA2, runB2, runA1, runB1 };

            _publisher.DetectAndSetRetriesForTestRun(list);

            // Only primary runs remain
            Assert.Equal(2, list.Count);
            Assert.Contains(runA1, list);
            Assert.Contains(runB1, list);

            Assert.Single(runA1.Retries);
            Assert.Same(runA2, runA1.Retries[0]);

            Assert.Single(runB1.Retries);
            Assert.Same(runB2, runB1.Retries[0]);
        }

        #endregion

        #region ProcessTestResults tests

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "TestRetryHelper")]
        public void ProcessTestResults_NullTestRunData_DoesNotThrow()
        {
            var dict = new Dictionary<string, string>();
            TestDataPublisher.ProcessTestResults(null, dict);
            Assert.Empty(dict);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "TestRetryHelper")]
        public void ProcessTestResults_PopulatesDictionary_WithTestNameAsKey()
        {
            var run = CreateTestRunData(null, DateTime.UtcNow,
                ("Namespace.TestA", "Test A Title", "Passed"),
                ("Namespace.TestB", "Test B Title", "Failed"));

            var dict = new Dictionary<string, string>();
            TestDataPublisher.ProcessTestResults(run, dict);

            Assert.Equal(2, dict.Count);
            Assert.Equal("Passed", dict["Namespace.TestA Test A Title"]);
            Assert.Equal("Failed", dict["Namespace.TestB Test B Title"]);
        }

        #endregion

        #region GetLatestAttemptResults tests

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "TestRetryHelper")]
        public void GetLatestAttemptResults_MultipleRetries_LastRetryWins()
        {
            var primaryRun = CreateTestRunData("run1", DateTime.UtcNow,
                ("Test1", "Title1", "Failed"));

            var retry1 = CreateTestRunData("run1", DateTime.UtcNow,
                ("Test1", "Title1", "Failed")); // still failing
            var retry2 = CreateTestRunData("run1", DateTime.UtcNow,
                ("Test1", "Title1", "Passed")); // finally passes

            primaryRun.Retries = new List<TestRunData> { retry1, retry2 };

            var results = _publisher.GetLatestAttemptResults(primaryRun);

            Assert.Single(results);
            Assert.Equal("Passed", results["Test1 Title1"]);
        }

        #endregion

        #region GetTestRunOutcomeForRetries tests

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "TestRetryHelper")]
        public void GetTestRunOutcomeForRetries_NullList_ReturnsFalse()
        {
            var anyFailed = _publisher.GetTestRunOutcomeForRetries(null, out var summary);

            Assert.False(anyFailed);
            Assert.Equal(0, summary.Total);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "TestRetryHelper")]
        public void GetTestRunOutcomeForRetries_AllPassing_ReturnsFalse()
        {
            var run = CreateTestRunData("run1", DateTime.UtcNow,
                ("Test1", "Title1", "Passed"),
                ("Test2", "Title2", "Passed"));

            var list = new List<TestRunData> { run };
            var anyFailed = _publisher.GetTestRunOutcomeForRetries(list, out var summary);

            Assert.False(anyFailed);
            Assert.Equal(2, summary.Total);
            Assert.Equal(2, summary.Passed);
            Assert.Equal(0, summary.Failed);
            Assert.Equal(0, summary.Skipped);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "TestRetryHelper")]
        public void GetTestRunOutcomeForRetries_FailedTestResolvedByRetry_ReturnsFalse()
        {
            var primaryRun = CreateTestRunData("run1", DateTime.UtcNow,
                ("Test1", "Title1", "Failed"),
                ("Test2", "Title2", "Passed"));

            var retry = CreateTestRunData("run1", DateTime.UtcNow,
                ("Test1", "Title1", "Passed")); // fixed in retry

            primaryRun.Retries = new List<TestRunData> { retry };

            var list = new List<TestRunData> { primaryRun };
            var anyFailed = _publisher.GetTestRunOutcomeForRetries(list, out var summary);

            Assert.False(anyFailed);
            Assert.Equal(2, summary.Total);
            Assert.Equal(2, summary.Passed);
            Assert.Equal(0, summary.Failed);
        }

        #endregion

        #region End-to-end: DetectAndSetRetries + GetTestRunOutcomeForRetries

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "TestRetryHelper")]
        public void EndToEnd_DetectRetries_ThenEvaluateOutcome_FailureResolvedByRetry()
        {
            var date1 = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
            var date2 = new DateTime(2025, 1, 1, 11, 0, 0, DateTimeKind.Utc);

            var primaryRun = CreateTestRunData("run1", date1,
                ("Test1", "Title1", "Failed"),
                ("Test2", "Title2", "Passed"));

            var retryRun = CreateTestRunData("run1", date2,
                ("Test1", "Title1", "Passed")); // fixed on retry

            var list = new List<TestRunData> { primaryRun, retryRun };

            // Step 1: detect retries
            _publisher.DetectAndSetRetriesForTestRun(list);

            Assert.Single(list);
            Assert.NotNull(list[0].Retries);

            // Step 2: evaluate outcome
            var anyFailed = _publisher.GetTestRunOutcomeForRetries(list, out var summary);

            Assert.False(anyFailed);
            Assert.Equal(2, summary.Total);
            Assert.Equal(2, summary.Passed);
            Assert.Equal(0, summary.Failed);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "TestRetryHelper")]
        public void EndToEnd_DetectRetries_ThenEvaluateOutcome_FailurePersistsAfterRetry()
        {
            var date1 = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
            var date2 = new DateTime(2025, 1, 1, 11, 0, 0, DateTimeKind.Utc);

            var primaryRun = CreateTestRunData("run1", date1,
                ("Test1", "Title1", "Failed"),
                ("Test2", "Title2", "Passed"));

            var retryRun = CreateTestRunData("run1", date2,
                ("Test1", "Title1", "Failed")); // still failing

            var list = new List<TestRunData> { primaryRun, retryRun };

            _publisher.DetectAndSetRetriesForTestRun(list);

            var anyFailed = _publisher.GetTestRunOutcomeForRetries(list, out var summary);

            Assert.True(anyFailed);
            Assert.Equal(2, summary.Total);
            Assert.Equal(1, summary.Passed);
            Assert.Equal(1, summary.Failed);
        }

        #endregion
    }
}
