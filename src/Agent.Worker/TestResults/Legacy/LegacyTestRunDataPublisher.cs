// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.TestManagement.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker.CodeCoverage;
using Microsoft.VisualStudio.Services.Agent.Worker.TestResults;
using Microsoft.VisualStudio.Services.Agent.Worker.TestResults.Utils;
using Microsoft.VisualStudio.Services.WebApi;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestRunContext = Microsoft.TeamFoundation.TestClient.PublishTestResults.TestRunContext;

namespace Microsoft.VisualStudio.Services.Agent.Worker.LegacyTestResults
{
    [ServiceLocator(Default = typeof(LegacyTestRunDataPublisher))]
    public interface ILegacyTestRunDataPublisher : IAgentService
    {
        void InitializePublisher(IExecutionContext context, string projectName, VssConnection connection, string testRunner, bool publishRunLevelAttachments);

        Task<bool> PublishAsync(TestRunContext runContext, List<string> testResultFiles, string runTitle, int? buildId, bool mergeResults);
    }

    public class LegacyTestRunDataPublisher : AgentService, ILegacyTestRunDataPublisher
    {
        private IExecutionContext _executionContext;
        private string _projectName;
        private ITestRunPublisher _testRunPublisher;
        private IResultReader _resultReader;
        int PublishBatchSize = 10;
        private const string _testRunSystemCustomFieldName = "TestRunSystem";
        private readonly object _sync = new object();
        private int _runCounter = 0;
        private bool _calculateTestRunSummary;
        private bool _isFlakyCheckEnabled;
        private string _testRunner;
        private ITestResultsServer _testResultsServer;
        private TestRunDataPublisherHelper _testRunPublisherHelper;

        public void InitializePublisher(IExecutionContext context, string projectName, VssConnection connection, string testRunner, bool publishRunLevelAttachments)
        {
            Trace.Entering();
            _executionContext = context;
            _projectName = projectName;
            _testRunner = testRunner;
            _resultReader = GetTestResultReader(_testRunner, publishRunLevelAttachments);
            _testRunPublisher = HostContext.GetService<ITestRunPublisher>();
            _testRunPublisher.InitializePublisher(_executionContext, connection, projectName, _resultReader);
            _testResultsServer = HostContext.GetService<ITestResultsServer>();
            _testResultsServer.InitializeServer(connection, _executionContext);
            _testRunPublisherHelper = new TestRunDataPublisherHelper(_executionContext, null, _testRunPublisher, _testResultsServer);
            LoadFeatureFlagState();
            Trace.Leaving();
        }

        public async Task<bool> PublishAsync(TestRunContext runContext, List<string> testResultFiles, string runTitle, int? buildId, bool mergeResults)
        {
            ArgUtil.NotNull(runContext, nameof(runContext));
            ArgUtil.NotNull(testResultFiles, nameof(testResultFiles));
            if (mergeResults)
            {
                return await PublishAllTestResultsToSingleTestRunAsync(testResultFiles, _testRunPublisher, runContext, _resultReader.Name, runTitle, buildId, _executionContext.CancellationToken);
            }
            else
            {
                return await PublishToNewTestRunPerTestResultFileAsync(testResultFiles, _testRunPublisher, runContext, _resultReader.Name, runTitle, PublishBatchSize, _executionContext.CancellationToken);
            }
        }

        private IResultReader GetTestResultReader(string testRunner, bool publishRunLevelAttachments)
        {
            var extensionManager = HostContext.GetService<IExtensionManager>();
            IResultReader reader = (extensionManager.GetExtensions<IResultReader>()).FirstOrDefault(x => testRunner.Equals(x.Name, StringComparison.OrdinalIgnoreCase));

            if (reader == null)
            {
                throw new ArgumentException("Unknown Test Runner.");
            }

            reader.AddResultsFileToRunLevelAttachments = publishRunLevelAttachments;
            return reader;
        }

        /// <summary>
        /// Publish single test run
        /// </summary>
        private async Task<bool> PublishAllTestResultsToSingleTestRunAsync(List<string> resultFiles, ITestRunPublisher publisher, TestRunContext runContext, string resultReader, string runTitle, int? buildId, CancellationToken cancellationToken)
        {
            bool isTestRunOutcomeFailed = false;
            try
            {
                //use local time since TestRunData defaults to local times
                DateTime minStartDate = DateTime.MaxValue;
                DateTime maxCompleteDate = DateTime.MinValue;
                DateTime presentTime = DateTime.UtcNow;
                bool dateFormatError = false;
                TimeSpan totalTestCaseDuration = TimeSpan.Zero;
                List<string> runAttachments = new List<string>();
                List<TestCaseResultData> runResults = new List<TestCaseResultData>();
                TestRunSummary testRunSummary = new TestRunSummary();
                //read results from each file
                foreach (string resultFile in resultFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    //test case results
                    _executionContext.Debug(StringUtil.Format("Reading test results from file '{0}'", resultFile));
                    TestRunData resultFileRunData = publisher.ReadResultsFromFile(runContext, resultFile);
                    isTestRunOutcomeFailed = isTestRunOutcomeFailed || GetTestRunOutcome(resultFileRunData, testRunSummary);

                    if (resultFileRunData != null)
                    {
                        if (resultFileRunData.Results != null && resultFileRunData.Results.Length > 0)
                        {
                            try
                            {
                                if (string.IsNullOrEmpty(resultFileRunData.StartDate) || string.IsNullOrEmpty(resultFileRunData.CompleteDate))
                                {
                                    dateFormatError = true;
                                }

                                //As per discussion with Manoj(refer bug 565487): Test Run duration time should be minimum Start Time to maximum Completed Time when merging
                                if (!string.IsNullOrEmpty(resultFileRunData.StartDate))
                                {
                                    DateTime startDate = DateTime.Parse(resultFileRunData.StartDate, null, DateTimeStyles.RoundtripKind);
                                    minStartDate = minStartDate > startDate ? startDate : minStartDate;

                                    if (!string.IsNullOrEmpty(resultFileRunData.CompleteDate))
                                    {
                                        DateTime endDate = DateTime.Parse(resultFileRunData.CompleteDate, null, DateTimeStyles.RoundtripKind);
                                        maxCompleteDate = maxCompleteDate < endDate ? endDate : maxCompleteDate;
                                    }
                                }
                            }
                            catch (FormatException)
                            {
                                _executionContext.Warning(StringUtil.Loc("InvalidDateFormat", resultFile, resultFileRunData.StartDate, resultFileRunData.CompleteDate));
                                dateFormatError = true;
                            }

                            //continue to calculate duration as a fallback for case: if there is issue with format or dates are null or empty
                            foreach (TestCaseResultData tcResult in resultFileRunData.Results)
                            {
                                int durationInMs = Convert.ToInt32(tcResult.DurationInMs);
                                totalTestCaseDuration = totalTestCaseDuration.Add(TimeSpan.FromMilliseconds(durationInMs));
                            }

                            runResults.AddRange(resultFileRunData.Results);

                            //run attachments
                            if (resultFileRunData.Attachments != null)
                            {
                                runAttachments.AddRange(resultFileRunData.Attachments);
                            }
                        }
                        else
                        {
                            _executionContext.Output(StringUtil.Loc("NoResultFound", resultFile));
                        }
                    }
                    else
                    {
                        _executionContext.Warning(StringUtil.Loc("InvalidResultFiles", resultFile, resultReader));
                    }
                }

                //publish run if there are results.
                if (runResults.Count > 0)
                {
                    string runName = string.IsNullOrWhiteSpace(runTitle)
                    ? StringUtil.Format("{0}_TestResults_{1}", _resultReader.Name, buildId)
                    : runTitle;

                    if (DateTime.Compare(minStartDate, maxCompleteDate) > 0)
                    {
                        _executionContext.Warning(StringUtil.Loc("InvalidCompletedDate", maxCompleteDate, minStartDate));
                        dateFormatError = true;
                    }

                    minStartDate = DateTime.Equals(minStartDate, DateTime.MaxValue) ? presentTime : minStartDate;
                    maxCompleteDate = dateFormatError || DateTime.Equals(maxCompleteDate, DateTime.MinValue) ? minStartDate.Add(totalTestCaseDuration) : maxCompleteDate;

                    // create test run
                    TestRunData testRunData = new TestRunData(
                        name: runName,
                        startedDate: minStartDate.ToString("o"),
                        completedDate: maxCompleteDate.ToString("o"),
                        state: "InProgress",
                        isAutomated: true,
                        buildId: runContext != null ? runContext.BuildId : 0,
                        buildFlavor: runContext != null ? runContext.Configuration : string.Empty,
                        buildPlatform: runContext != null ? runContext.Platform : string.Empty,
                        releaseUri: runContext != null ? runContext.ReleaseUri : null,
                        releaseEnvironmentUri: runContext != null ? runContext.ReleaseEnvironmentUri : null
                    );
                    testRunData.PipelineReference = runContext.PipelineReference;
                    testRunData.Attachments = runAttachments.ToArray();
                    testRunData.AddCustomField(_testRunSystemCustomFieldName, runContext.TestRunSystem);
                    AddTargetBranchInfoToRunCreateModel(testRunData, runContext.TargetBranchName);

                    TestRun testRun = await publisher.StartTestRunAsync(testRunData, _executionContext.CancellationToken);
                    await publisher.AddResultsAsync(testRun, runResults.ToArray(), _executionContext.CancellationToken);
                    TestRun updatedRun = await publisher.EndTestRunAsync(testRunData, testRun.Id, true, _executionContext.CancellationToken);

                    // Check failed results for flaky aware
                    // Fallback to flaky aware if there are any failures.
                    if (isTestRunOutcomeFailed && _isFlakyCheckEnabled)
                    {
                        IList<TestRun> publishedRuns = new List<TestRun>();
                        publishedRuns.Add(updatedRun);
                        var runOutcome = _testRunPublisherHelper.CheckRunsForFlaky(publishedRuns, _projectName);
                        if (runOutcome != null && runOutcome.HasValue)
                        {
                            isTestRunOutcomeFailed = runOutcome.Value;
                        }
                    }

                    StoreTestRunSummaryInEnvVar(testRunSummary);
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && _executionContext.CancellationToken.IsCancellationRequested))
            {
                // Not catching all the operationcancelled exceptions, as the pipeline cancellation should cancel the command as well.
                // Do not fail the task.
                LogPublishTestResultsFailureWarning(ex);
            }
            return isTestRunOutcomeFailed;
        }

        private void StoreTestRunSummaryInEnvVar(TestRunSummary testRunSummary)
        {
            // Storing testrun summary in environment variable, which will be read by PublishPipelineMetadataTask and publish to evidence store.
            if (_calculateTestRunSummary)
            {
                TestResultUtils.StoreTestRunSummaryInEnvVar(_executionContext, testRunSummary, _testRunner, "PublishTestResults");
            }
        }

        /// <summary>
        /// Publish separate test run for each result file that has results.
        /// </summary>
        private async Task<bool> PublishToNewTestRunPerTestResultFileAsync(List<string> resultFiles,
            ITestRunPublisher publisher,
            TestRunContext runContext,
            string resultReader,
            string runTitle,
            int batchSize,
            CancellationToken cancellationToken)
        {
            bool isTestRunOutcomeFailed = false;
            try
            {
                IList<TestRun> publishedRuns = new List<TestRun>();
                var groupedFiles = resultFiles
                .Select((resultFile, index) => new { Index = index, file = resultFile })
                .GroupBy(pair => pair.Index / batchSize)
                .Select(bucket => bucket.Select(pair => pair.file).ToList())
                .ToList();

                bool changeTestRunTitle = resultFiles.Count > 1;
                TestRunSummary testRunSummary = new TestRunSummary();
                foreach (var files in groupedFiles)
                {
                    // Publish separate test run for each result file that has results.
                    var publishTasks = files.Select(async resultFile =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string runName = runTitle;
                        if (!string.IsNullOrWhiteSpace(runTitle) && changeTestRunTitle)
                        {
                            runName = GetRunName(runTitle);
                        }

                        _executionContext.Debug(StringUtil.Format("Reading test results from file '{0}'", resultFile));
                        TestRunData testRunData = publisher.ReadResultsFromFile(runContext, resultFile, runName);
                        testRunData.PipelineReference = runContext.PipelineReference;

                        isTestRunOutcomeFailed = isTestRunOutcomeFailed || GetTestRunOutcome(testRunData, testRunSummary);

                        cancellationToken.ThrowIfCancellationRequested();

                        if (testRunData != null)
                        {
                            if (testRunData.Results != null)
                            {
                                testRunData.AddCustomField(_testRunSystemCustomFieldName, runContext.TestRunSystem);
                                AddTargetBranchInfoToRunCreateModel(testRunData, runContext.TargetBranchName);
                                TestRun testRun = await publisher.StartTestRunAsync(testRunData, _executionContext.CancellationToken);
                                await publisher.AddResultsAsync(testRun, testRunData.Results, _executionContext.CancellationToken);
                                TestRun updatedRun = await publisher.EndTestRunAsync(testRunData, testRun.Id, cancellationToken: _executionContext.CancellationToken);

                                publishedRuns.Add(updatedRun);
                            }
                            else
                            {
                                _executionContext.Output(StringUtil.Loc("NoResultFound", resultFile));
                            }
                        }
                        else
                        {
                            _executionContext.Warning(StringUtil.Loc("InvalidResultFiles", resultFile, resultReader));
                        }
                    });
                    await Task.WhenAll(publishTasks);
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

                StoreTestRunSummaryInEnvVar(testRunSummary);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && _executionContext.CancellationToken.IsCancellationRequested))
            {
                // Not catching all the operationcancelled exceptions, as the pipeline cancellation should cancel the command as well.
                // Do not fail the task.
                LogPublishTestResultsFailureWarning(ex);
            }
            return isTestRunOutcomeFailed;
        }

        private string GetRunName(string runTitle)
        {
            lock (_sync)
            {
                return StringUtil.Format("{0}_{1}", runTitle, ++_runCounter);
            }
        }

        private void LogPublishTestResultsFailureWarning(Exception ex)
        {
            string message = ex.Message;
            if (ex.InnerException != null)
            {
                message += Environment.NewLine;
                message += ex.InnerException.Message;
            }
            _executionContext.Warning(StringUtil.Loc("FailedToPublishTestResults", message));
        }

        // Adds Target Branch Name info to run create model
        private void AddTargetBranchInfoToRunCreateModel(RunCreateModel runCreateModel, string pullRequestTargetBranchName)
        {
            if (string.IsNullOrEmpty(pullRequestTargetBranchName) ||
                !string.IsNullOrEmpty(runCreateModel.BuildReference?.TargetBranchName))
            {
                return;
            }

            if (runCreateModel.BuildReference == null)
            {
                runCreateModel.BuildReference = new BuildConfiguration() { TargetBranchName = pullRequestTargetBranchName };
            }
            else
            {
                runCreateModel.BuildReference.TargetBranchName = pullRequestTargetBranchName;
            }
        }

        /// <summary>
        /// Reads a list testRunData Object and returns true if any test case outcome is failed
        /// </summary>
        /// <param name="testRunDataList"></param>
        /// <returns></returns>
        private bool GetTestRunOutcome(TestRunData testRunData, TestRunSummary testRunSummary)
        {
            bool anyFailedTests = false;
            foreach (var testCaseResultData in testRunData.Results)
            {
                testRunSummary.Total += 1;
                Enum.TryParse(testCaseResultData.Outcome, out TestOutcome outcome);
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
            return anyFailedTests;
        }

        private void LoadFeatureFlagState()
        {
            using (var connection = WorkerUtilities.GetVssConnection(_executionContext))
            {
                var featureFlagService = _executionContext.GetHostContext().GetService<IFeatureFlagService>();
                featureFlagService.InitializeFeatureService(_executionContext, connection);
                _calculateTestRunSummary = featureFlagService.GetFeatureFlagState(TestResultsConstants.CalculateTestRunSummaryFeatureFlag, TestResultsConstants.TFSServiceInstanceGuid);
                _isFlakyCheckEnabled = featureFlagService.GetFeatureFlagState(TestResultsConstants.EnableFlakyCheckInAgentFeatureFlag, TestResultsConstants.TCMServiceInstanceGuid);
            }
        }
    }
}
