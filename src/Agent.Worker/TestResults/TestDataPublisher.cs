// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.TestClient.PublishTestResults;
using Microsoft.TeamFoundation.TestManagement.WebApi;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.TeamFoundation.Core.WebApi;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Services.Agent.Worker.TestResults.Utils;
using ITestResultsServer = Microsoft.VisualStudio.Services.Agent.Worker.LegacyTestResults.ITestResultsServer;
using Microsoft.IdentityModel.Tokens;

namespace Microsoft.VisualStudio.Services.Agent.Worker.TestResults
{
    [ServiceLocator(Default = typeof(TestDataPublisher))]
    public interface ITestDataPublisher : IAgentService
    {
        void InitializePublisher(IExecutionContext executionContext, string projectName, VssConnection connection, string testRunner);

        Task<bool> PublishAsync(TestRunContext runContext, List<string> testResultFiles, PublishOptions publishOptions, CancellationToken cancellationToken = default(CancellationToken));

        Task<bool> PublishAsync(TestRunContext runContext, List<string> testResultFiles, TestCaseResult[] testCaseResults, PublishOptions publishOptions, CancellationToken cancellationToken = default(CancellationToken));
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA2000:Dispose objects before losing scope", MessageId = "CommandTraceListener")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1001:Types that own disposable fields should be disposable", MessageId = "DisposableFieldsArePassedIn")]
    public sealed class TestDataPublisher : AgentService, ITestDataPublisher
    {
        private IExecutionContext _executionContext;
        private string _projectName;
        private ITestRunPublisher _testRunPublisher;

        private ITestLogStore _testLogStore;
        private IParser _parser;

        private VssConnection _connection;
        private IFeatureFlagService _featureFlagService;
        private string _testRunner;
        private bool _calculateTestRunSummary;
        private bool _isFlakyCheckEnabled;
        private TestRunDataPublisherHelper _testRunPublisherHelper;
        private ITestResultsServer _testResultsServer;

        public void InitializePublisher(IExecutionContext context, string projectName, VssConnection connection, string testRunner)
        {
            Trace.Entering();
            _executionContext = context;
            _projectName = projectName;
            _connection = connection;
            _testRunner = testRunner;
            _testRunPublisher = new TestRunPublisher(connection, new CommandTraceListener(context));
            _testLogStore = new TestLogStore(connection, new CommandTraceListener(context));
            _testResultsServer = HostContext.GetService<ITestResultsServer>();
            _testResultsServer.InitializeServer(connection, _executionContext);
            var extensionManager = HostContext.GetService<IExtensionManager>();
            _featureFlagService = HostContext.GetService<IFeatureFlagService>();
            _featureFlagService.InitializeFeatureService(_executionContext, connection);
            _calculateTestRunSummary = _featureFlagService.GetFeatureFlagState(TestResultsConstants.CalculateTestRunSummaryFeatureFlag, TestResultsConstants.TFSServiceInstanceGuid);
            _isFlakyCheckEnabled = _featureFlagService.GetFeatureFlagState(TestResultsConstants.EnableFlakyCheckInAgentFeatureFlag, TestResultsConstants.TCMServiceInstanceGuid); ;
            _parser = (extensionManager.GetExtensions<IParser>()).FirstOrDefault(x => _testRunner.Equals(x.Name, StringComparison.OrdinalIgnoreCase));
            _testRunPublisherHelper = new TestRunDataPublisherHelper(_executionContext, _testRunPublisher, null, _testResultsServer);
            Trace.Leaving();
        }

        public async Task<bool> PublishAsync(TestRunContext runContext, List<string> testResultFiles, PublishOptions publishOptions, CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                TestDataProvider testDataProvider = ParseTestResultsFile(runContext, testResultFiles);
                var publishTasks = new List<Task>();

                if (testDataProvider != null)
                {
                    var testRunData = testDataProvider.GetTestRunData();
                    Task<IList<TestRun>> publishtestRunDataTask = Task.Run(() => _testRunPublisher.PublishTestRunDataAsync(runContext, _projectName, testRunData, publishOptions, cancellationToken));
                    Task uploadBuildDataAttachmentTask = Task.Run(() => UploadBuildDataAttachment(runContext, testDataProvider.GetBuildData(), cancellationToken));

                    publishTasks.Add(publishtestRunDataTask);

                    //publishing build level attachment
                    publishTasks.Add(uploadBuildDataAttachmentTask);

                    await Task.WhenAll(publishTasks);

                    IList<TestRun> publishedRuns = publishtestRunDataTask.Result;

                    bool isTestRunOutcomeFailed;
                    TestRunSummary testRunSummary;
                    if (publishOptions.IsDetectTestRunRetry)
                    {
                        // Group retries after publishing – the library handles attempt creation,
                        // but we still need the grouping for retry-aware outcome evaluation.
                        _executionContext.Debug("Detecting test run retries for outcome evaluation.");
                        DetectAndSetRetriesForTestRun(testRunData);
                        _executionContext.Debug("Re-evaluating test run outcome with retry awareness.");
                        isTestRunOutcomeFailed = GetTestRunOutcomeForRetries(testRunData, out testRunSummary);
                    }
                    else
                    {
                        // For non-retry-aware publishing, determine test run outcome based on the primary run results (legacy behavior)
                        isTestRunOutcomeFailed = GetTestRunOutcome(_executionContext, testRunData, out testRunSummary);
                    }

                    // Storing testrun summary in environment variable, which will be read by PublishPipelineMetadataTask and publish to evidence store.
                    if (_calculateTestRunSummary)
                    {
                        TestResultUtils.StoreTestRunSummaryInEnvVar(_executionContext, testRunSummary, _testRunner, "PublishTestResults");
                    }

                    // Check failed results for flaky aware
                    // Fallback to flaky aware if there are any failures.
                    if (isTestRunOutcomeFailed && _isFlakyCheckEnabled)
                    {
                        var runOutcome = _testRunPublisherHelper.CheckRunsForFlaky(publishedRuns, _projectName);
                        if (runOutcome != null && runOutcome.HasValue)
                        {
                            isTestRunOutcomeFailed = runOutcome.Value;
                        }
                    }

                    return isTestRunOutcomeFailed;
                }

                return false;
            }
            catch (Exception ex)
            {
                _executionContext.Warning("Failed to publish test run data: " + ex.ToString());
            }
            return false;
        }

        public async Task<bool> PublishAsync(TestRunContext runContext, List<string> testResultFiles, TestCaseResult[] testCaseResults, PublishOptions publishOptions, CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                TestDataProvider testDataProvider = ParseTestResultsFile(runContext, testResultFiles);
                var publishTasks = new List<Task>();

                if (testDataProvider != null)
                {
                    var testRunData = testDataProvider.GetTestRunData();

                    if (!testCaseResults.IsNullOrEmpty())
                    {
                        //Dictionary because FQN to Test Case Results is 1 to many
                        Dictionary<string, List<TestCaseResult>> testResultByFQN = new();

                        // Iterate through the list of objects
                        foreach (TestCaseResult testResult in testCaseResults)
                        {
                            if (!testResultByFQN.ContainsKey(testResult.AutomatedTestName))
                            {
                                // If not, initialize the list associated with the key
                                testResultByFQN[testResult.AutomatedTestName] = new List<TestCaseResult>();
                            }
                            // Add the object to the dictionary using its Id as the key
                            testResultByFQN[testResult.AutomatedTestName].Add(testResult);
                        }

                        int testRunDataIterator = 0;
                        int testResultDataIterator = 0;

                        for (testRunDataIterator = 0; testRunDataIterator < testRunData.Count; testRunDataIterator++)
                        {
                            var testResultsUpdated = new List<TestCaseResultData>();
                            for (testResultDataIterator = 0; testResultDataIterator < testRunData[testRunDataIterator].TestResults.Count; testResultDataIterator++)
                            {
                                var testResultFQN = testRunData[testRunDataIterator].TestResults[testResultDataIterator].AutomatedTestStorage +
                                    "." + testRunData[testRunDataIterator].TestResults[testResultDataIterator].AutomatedTestName;

                                if (testResultByFQN.TryGetValue(testResultFQN, out List<TestCaseResult> inputs))
                                {
                                    if (testRunData[testRunDataIterator].TestResults[testResultDataIterator].Outcome != "NotExecuted")
                                    {
                                        foreach (var input in inputs)
                                        {
                                            var testCaseResultDataUpdated = TestResultUtils.CloneTestCaseResultData(testRunData[testRunDataIterator].TestResults[testResultDataIterator]);

                                            testCaseResultDataUpdated.TestPoint = input.TestPoint;
                                            testCaseResultDataUpdated.TestCaseTitle = input.TestCaseTitle;
                                            testCaseResultDataUpdated.Configuration = input.Configuration;
                                            testCaseResultDataUpdated.TestCase = input.TestCase;
                                            testCaseResultDataUpdated.Owner = input.Owner;
                                            testCaseResultDataUpdated.State = "5";
                                            testCaseResultDataUpdated.TestCaseRevision = input.TestCaseRevision;

                                            testResultsUpdated.Add(testCaseResultDataUpdated);
                                        }
                                    }
                                }
                            }
                            testRunData[testRunDataIterator].TestResults = testResultsUpdated;
                        }
                    }

                    //publishing run level attachment
                    Task<IList<TestRun>> publishtestRunDataTask = Task.Run(() => _testRunPublisher.PublishTestRunDataAsync(runContext, _projectName, testRunData, publishOptions, cancellationToken));
                    Task uploadBuildDataAttachmentTask = Task.Run(() => UploadBuildDataAttachment(runContext, testDataProvider.GetBuildData(), cancellationToken));

                    publishTasks.Add(publishtestRunDataTask);

                    //publishing build level attachment
                    publishTasks.Add(uploadBuildDataAttachmentTask);

                    await Task.WhenAll(publishTasks);

                    IList<TestRun> publishedRuns = publishtestRunDataTask.Result;

                    bool isTestRunOutcomeFailed;
                    TestRunSummary testRunSummary;
                    if (publishOptions.IsDetectTestRunRetry)
                    {
                        // Group retries after publishing – the library handles attempt creation,
                        // but we still need the grouping for retry-aware outcome evaluation.
                        _executionContext.Debug("Detecting test run retries for outcome evaluation.");
                        DetectAndSetRetriesForTestRun(testRunData);
                        _executionContext.Debug("Re-evaluating test run outcome with retry awareness.");
                        isTestRunOutcomeFailed = GetTestRunOutcomeForRetries(testRunData, out testRunSummary);
                    }
                    else
                    {
                        // For non-retry-aware publishing, determine test run outcome based on the primary run results (legacy behavior)
                        isTestRunOutcomeFailed = GetTestRunOutcome(_executionContext, testRunData, out testRunSummary);
                    }

                    // Storing testrun summary in environment variable, which will be read by PublishPipelineMetadataTask and publish to evidence store.
                    if (_calculateTestRunSummary)
                    {
                        TestResultUtils.StoreTestRunSummaryInEnvVar(_executionContext, testRunSummary, _testRunner, "PublishTestResults");
                    }

                    // Check failed results for flaky aware
                    // Fallback to flaky aware if there are any failures.
                    if (isTestRunOutcomeFailed && _isFlakyCheckEnabled)
                    {
                        var runOutcome = _testRunPublisherHelper.CheckRunsForFlaky(publishedRuns, _projectName);
                        if (runOutcome != null && runOutcome.HasValue)
                        {
                            isTestRunOutcomeFailed = runOutcome.Value;
                        }
                    }

                    return isTestRunOutcomeFailed;
                }

                return false;
            }
            catch (Exception ex)
            {
                _executionContext.Warning("Failed to publish test run data: " + ex.ToString());
            }
            return false;
        }

        private TestDataProvider ParseTestResultsFile(TestRunContext runContext, List<string> testResultFiles)
        {
            if (_parser == null)
            {
                throw new ArgumentException("Unknown test runner");
            }
            return _parser.ParseTestResultFiles(_executionContext, runContext, testResultFiles);
        }

        private bool GetTestRunOutcome(IExecutionContext executionContext, IList<TestRunData> testRunDataList, out TestRunSummary testRunSummary)
        {
            bool anyFailedTests = false;
            testRunSummary = new TestRunSummary();
            foreach (var testRunData in testRunDataList)
            {
                foreach (var testCaseResult in testRunData.TestResults)
                {
                    testRunSummary.Total += 1;
                    Enum.TryParse(testCaseResult.Outcome, out TestOutcome outcome);
                    switch (outcome)
                    {
                        case TestOutcome.Failed:
                        case TestOutcome.Aborted:
                            testRunSummary.Failed += 1;
                            anyFailedTests = true;
                            break;
                        case TestOutcome.Passed:
                            testRunSummary.Passed += 1;
                            break;
                        case TestOutcome.Inconclusive:
                            testRunSummary.Skipped += 1;
                            break;
                        default: break;
                    }

                    if (!_calculateTestRunSummary && anyFailedTests)
                    {
                        return anyFailedTests;
                    }
                }
            }
            return anyFailedTests;
        }

        private async Task UploadRunDataAttachment(TestRunContext runContext, List<TestRunData> testRunData, PublishOptions publishOptions, CancellationToken cancellationToken = default(CancellationToken))
        {
            await _testRunPublisher.PublishTestRunDataAsync(runContext, _projectName, testRunData, publishOptions, cancellationToken);
        }

        private async Task UploadBuildDataAttachment(TestRunContext runContext, List<BuildData> buildDataList, CancellationToken cancellationToken = default(CancellationToken))
        {
            _executionContext.Debug("Uploading build level attachements individually");

            Guid projectId = await GetProjectId(_projectName);

            var attachFilesTasks = new List<Task>();
            HashSet<BuildAttachment> attachments = new HashSet<BuildAttachment>(new BuildAttachmentComparer());

            foreach (var buildData in buildDataList)
            {
                attachFilesTasks.AddRange(buildData.BuildAttachments
                    .Select(
                    async attachment =>
                    {
                        if (attachments.Contains(attachment))
                        {
                            _executionContext.Debug($"Skipping upload of {attachment.Filename} as it was already uploaded.");
                            await Task.Yield();
                        }
                        else
                        {
                            attachments.Add(attachment);
                            await UploadTestBuildLog(projectId, attachment, runContext, cancellationToken);
                        }
                    })
                );
            }

            _executionContext.Debug($"Total build level attachments: {attachFilesTasks.Count}.");
            await Task.WhenAll(attachFilesTasks);
        }

        private async Task UploadTestBuildLog(Guid projectId, BuildAttachment buildAttachment, TestRunContext runContext, CancellationToken cancellationToken)
        {
            await _testLogStore.UploadTestBuildLogAsync(projectId, runContext.BuildId, buildAttachment.TestLogType, buildAttachment.Filename, buildAttachment.Metadata, null, buildAttachment.AllowDuplicateUploads, buildAttachment.TestLogCompressionType, cancellationToken);
        }

        private async Task<Guid> GetProjectId(string projectName)
        {
            var _projectClient = _connection.GetClient<ProjectHttpClient>();

            TeamProject proj = null;

            try
            {
                proj = await _projectClient.GetProject(projectName);
            }
            catch (Exception ex)
            {
                _executionContext.Warning("Get project failed" + projectName + " , exception: " + ex);
            }

            return proj.Id;
        }

        #region Test retry helper
        /// <summary>
        /// Detects retry test runs by grouping them based on TestRunIdFromAttachmentFile.
        /// Modifies the input list in place: retry runs are removed from the top-level list
        /// and added to the primary run's <see cref="TestRunData.Retries"/> collection.
        /// </summary>
        /// <param name="testRunDataList">Mutable list of parsed test run data.</param>
        public static void DetectAndSetRetriesForTestRun(IList<TestRunData> testRunDataList)
        {
            if (testRunDataList == null || testRunDataList.Count <= 1)
            {
                return;
            }

            // Group by TestRunIdFromAttachmentFile – runs that share the same ID are retries
            var groupedByRunId = testRunDataList
                .Where(t => !string.IsNullOrEmpty(t.TestRunIdFromAttachmentFile))
                .GroupBy(t => t.TestRunIdFromAttachmentFile)
                .Where(g => g.Count() > 1)
                .ToList();

            if (groupedByRunId.Count == 0)
            {
                return;
            }

            var retryRunsToRemove = new HashSet<TestRunData>();

            foreach (var group in groupedByRunId)
            {
                // Sort by TestRunStartDate – the earliest is the primary run, the rest are retries
                var sortedRuns = group
                    .OrderBy(t => t.TestRunStartDate)
                    .ToList();

                var primaryRun = sortedRuns[0];
                primaryRun.Retries = new List<TestRunData>();

                for (int i = 1; i < sortedRuns.Count; i++)
                {
                    primaryRun.Retries.Add(sortedRuns[i]);
                    retryRunsToRemove.Add(sortedRuns[i]);
                }
            }

            // Remove retry runs from the main list so only primary runs remain
            foreach (var retryRun in retryRunsToRemove)
            {
                testRunDataList.Remove(retryRun);
            }
        }

        /// <summary>
        /// Gets the latest attempt result for each test in a run that has retries.
        /// Returns a dictionary mapping test identifier to its latest outcome.
        /// Later retry attempts override earlier outcomes for the same test.
        /// </summary>
        private Dictionary<string, string> GetLatestAttemptResults(TestRunData testRunData)
        {
            var latestResults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Process primary run results first
            ProcessTestResults(testRunData, latestResults);

            // Process each retry in order – later retries override earlier ones
            if (testRunData.Retries != null)
            {
                foreach (var retry in testRunData.Retries)
                {
                    ProcessTestResults(retry, latestResults);
                }
            }

            return latestResults;
        }

        /// <summary>
        /// Checks whether any test is still marked as failed after all retry attempts
        /// and computes a <see cref="TestRunSummary"/> based on the final outcome per test.
        /// For runs with retries, only the latest attempt outcome per test is considered.
        /// For runs without retries, the standard outcome check is applied.
        /// </summary>
        /// <param name="testRunDataList">The list of test run data (primary runs only; retries are nested).</param>
        /// <param name="testRunSummary">
        /// When this method returns, contains a <see cref="TestRunSummary"/> whose counters
        /// reflect only the latest attempt per test (i.e., intermediate retry results are not double-counted).
        /// </param>
        /// <returns>
        /// <c>true</c> if at least one test is still failing after all retry attempts;
        /// <c>false</c> if all previously-failed tests were resolved by retries or there are no failures.
        /// </returns>
        private bool GetTestRunOutcomeForRetries(IList<TestRunData> testRunDataList, out TestRunSummary testRunSummary)
        {
            testRunSummary = new TestRunSummary();

            if (testRunDataList == null || testRunDataList.Count == 0)
            {
                return false;
            }

            bool anyFailedTests = false;

            foreach (var testRunData in testRunDataList)
            {
                // GetLatestAttemptResults works for both cases:
                // - With retries: returns only the final outcome per test across all attempts
                // - Without retries: returns each test's outcome from the primary run
                var latestAttemptResults = GetLatestAttemptResults(testRunData);
                foreach (var outcome in latestAttemptResults.Values)
                {
                    testRunSummary.Total += 1;
                    Enum.TryParse(outcome, true, out TestOutcome parsedOutcome);
                    switch (parsedOutcome)
                    {
                        case TestOutcome.Failed:
                        case TestOutcome.Aborted:
                            testRunSummary.Failed += 1;
                            anyFailedTests = true;
                            break;
                        case TestOutcome.Passed:
                            testRunSummary.Passed += 1;
                            break;
                        case TestOutcome.Inconclusive:
                            testRunSummary.Skipped += 1;
                            break;
                        default:
                            break;
                    }
                }
            }

            return anyFailedTests;
        }


        private static void ProcessTestResults(TestRunData testRunData, Dictionary<string, string> latestResults)
        {
            if (testRunData?.TestResults == null)
            {
                return;
            }

            foreach (var result in testRunData.TestResults)
            {
                string testName = result.AutomatedTestName;
                if (!string.IsNullOrEmpty(testName))
                {
                    latestResults[testName] = result.Outcome;
                }
            }
        }

        #endregion

    }
}
