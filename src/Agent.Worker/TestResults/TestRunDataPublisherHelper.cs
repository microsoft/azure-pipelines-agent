using Microsoft.TeamFoundation.TestClient.PublishTestResults;
using Microsoft.TeamFoundation.TestManagement.WebApi;
using Microsoft.VisualStudio.Services.Agent.Worker.TestResults.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LegacyTestRunPublisher = Microsoft.VisualStudio.Services.Agent.Worker.LegacyTestResults.ITestRunPublisher;
using ITestResultsServer = Microsoft.VisualStudio.Services.Agent.Worker.LegacyTestResults.ITestResultsServer;

namespace Microsoft.VisualStudio.Services.Agent.Worker.TestResults
{
    public class TestRunDataPublisherHelper
    {
        private IExecutionContext _executionContext;
        private ITestRunPublisher _libraryTestRunPublisher;
        private LegacyTestRunPublisher _agentTestRunPublisher;
        private ITestResultsServer _testResultsServer;

        public TestRunDataPublisherHelper(IExecutionContext executionContext, ITestRunPublisher libraryTestRunPublisher, LegacyTestRunPublisher agentTestRunPublisher, ITestResultsServer testResultServer)
        {
            _executionContext = executionContext;
            _libraryTestRunPublisher = libraryTestRunPublisher;
            _agentTestRunPublisher = agentTestRunPublisher;
            _testResultsServer = testResultServer;
        }

        protected internal virtual bool? CheckRunsForFlaky(IList<TestRun> runs, string projectName)
        {
            try
            {
                bool? runOutcome = DoesRunsContainsFailures(runs, projectName);

                return runOutcome;
            }
            catch (Exception ex)
            {
                // Exception in checking flaky will not fail the mainline pipeline
                _executionContext.Output("Failed to Check for Flaky : " + ex.ToString());
                return null;
            }
        }

        protected internal virtual bool? DoesRunsContainsFailures(IList<TestRun> testRuns, string projectName)
        {
            dynamic _testRunPublisher = _libraryTestRunPublisher;

            if (_agentTestRunPublisher != null)
            {
                _testRunPublisher = _agentTestRunPublisher;
            }

            if (testRuns == null || !testRuns.Any())
            {
                _executionContext.Output("No test runs are present");
                return null;
            }

            var cancellationToken = GetCancellationToken();

            if (GetFeatureFlagState(TestResultsConstants.UseNewRunSummaryAPIFeatureFlag, TestResultsConstants.TCMServiceInstanceGuid))
            {
                if (IsFlakyOptedInPassPercentage(projectName))
                {
                    _executionContext.Output("Flaky failed test results are opted out of pass percentage");
                    return null;
                }
            }

            try
            {
                // Reads through each run and if it contains failures, then check for failures with flaky awareness.
                foreach (var testRun in testRuns)
                {
                    if (testRun.TotalTests != testRun.PassedTests + testRun.NotApplicableTests)
                    {
                        if (GetFeatureFlagState(TestResultsConstants.UseNewRunSummaryAPIFeatureFlag, TestResultsConstants.TCMServiceInstanceGuid))
                        {
                            try
                            {
                                var testRunStats = _testRunPublisher.GetTestRunSummaryAsync(projectName, testRun.Id, true, cancellationToken).Result;
                                if (_testRunPublisher.IsTestRunFailed(testRunStats, cancellationToken))
                                {
                                    _executionContext.Output("Failed Results are published");
                                    return true;
                                }
                            }
                            catch (AggregateException exception)
                            {
                                _executionContext.Output($"TestRunDataPublisher.DoesRunsContainsFailures: Exception occured using test run summary api {exception}");
                                //After the max retries, if API still fails to calculate the summary and return with exception
                                //It will fallback to run stats api which may result in dirty summary i.e. old flow.
                                if (exception.InnerException is TestObjectNotFoundException)
                                {
                                    _executionContext.Warning($"TestRunDataPublisher.DoesRunsContainsFailures: Validating if test run is failed or succeeded based on summary where calulation of summary is not in completed state.");
                                    return _testRunPublisher.IsTestRunFailed(projectName, testRun.Id, cancellationToken);
                                }
                                else
                                {
                                    _executionContext.Error($"TestRunDataPublisher.DoesRunsContainsFailures: Not retriable exception occured, hence throwing the exception: {exception}");
                                    throw;
                                }
                            }
                        }
                        else
                        {
                            if (_testRunPublisher.IsTestRunFailed(projectName, testRun.Id, cancellationToken))
                            {
                                _executionContext.Output("Failed Results are published");
                                return true;
                            }
                        }
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _executionContext.Output($"Failed to get test run outcome from publisher: {ex}");
                return null;
            }
        }

        internal protected virtual string GetEnvironmentVar(string envVar)
        {
            return Environment.GetEnvironmentVariable(envVar);
        }

        internal protected virtual CancellationToken GetCancellationToken()
        {
            return new CancellationToken();
        }

        protected internal virtual bool GetFeatureFlagState(string featureFlagName, Guid serviceInstaceGuid)
        {
            var featureFlagValue = false;

            using (var connection = WorkerUtilities.GetVssConnection(_executionContext))
            {
                var featureFlagService = _executionContext.GetHostContext().GetService<IFeatureFlagService>();
                featureFlagService.InitializeFeatureService(_executionContext, connection);
                featureFlagValue = featureFlagService.GetFeatureFlagState(featureFlagName, serviceInstaceGuid);
            }

            return featureFlagValue;
        }

        protected internal virtual bool IsFlakyOptedInPassPercentage(string projectName)
        {
            var settings = GetTestResultsSettings(projectName, TestResultsSettingsType.Flaky);
            return settings.FlakySettings?.FlakyInSummaryReport != null && settings.FlakySettings?.FlakyInSummaryReport.Value == true;
        }

        protected internal virtual TestResultsSettings GetTestResultsSettings(string projectName, TestResultsSettingsType settingType)
        {
            var testResultSettings = _testResultsServer.GetTestResultsSettingsAsync(projectName, TestResultsSettingsType.Flaky, default(CancellationToken)).Result;
            return testResultSettings;
        }
    }
}
