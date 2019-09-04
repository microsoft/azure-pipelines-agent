using Microsoft.TeamFoundation.TestManagement.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.TeamFoundation.TestClient.PublishTestResults;
using System.Diagnostics;
using Microsoft.VisualStudio.Services.FeatureAvailability.WebApi;
using Microsoft.VisualStudio.Services.FeatureAvailability;
using Microsoft.VisualStudio.Services.Agent.Worker.CodeCoverage;

namespace Microsoft.VisualStudio.Services.Agent.Worker.TestResults
{
    public sealed class ResultsCommandExtension : AgentService, IWorkerCommandExtension
    {
        private IExecutionContext _executionContext;
        //publish test results inputs
        private List<string> _testResultFiles;
        private string _testRunner;
        private bool _mergeResults;
        private string _platform;
        private string _configuration;
        private string _runTitle;
        private bool _publishRunLevelAttachments;
        private int _runCounter = 0;

        private bool _failTaskOnFailedTests;

        private bool _isTestRunOutcomeFailed = false;
        private readonly object _sync = new object();
        private string _testRunSystem;
        private const string _testRunSystemCustomFieldName = "TestRunSystem";

        public Type ExtensionType => typeof(IWorkerCommandExtension);

        public string CommandArea => "results";

        public HostTypes SupportedHostTypes => HostTypes.All;

        public static int PublishBatchSize = 10;

        public void ProcessCommand(IExecutionContext context, Command command)
        {
            if (string.Equals(command.Event, WellKnownResultsCommand.PublishTestResults, StringComparison.OrdinalIgnoreCase))
            {
                ProcessPublishTestResultsCommand(context, command.Properties, command.Data);
            }
            else
            {
                throw new Exception(StringUtil.Loc("ResultsCommandNotFound", command.Event));
            }
        }

        private void ProcessPublishTestResultsCommand(IExecutionContext context, Dictionary<string, string> eventProperties, string data)
        {
            ArgUtil.NotNull(context, nameof(context));
            _executionContext = context;

            LoadPublishTestResultsInputs(context, eventProperties, data);

            string teamProject = context.Variables.System_TeamProject;
            TestRunContext runContext = CreateTestRunContext();

            VssConnection connection = WorkerUtilities.GetVssConnection(_executionContext);

            var commandContext = HostContext.CreateService<IAsyncCommandContext>();
            commandContext.InitializeCommandContext(context, StringUtil.Loc("PublishTestResults"));

            FeatureAvailabilityHttpClient featureAvailabilityHttpClient = connection.GetClient<FeatureAvailabilityHttpClient>();
            if (FeatureFlagUtility.GetFeatureFlagState(featureAvailabilityHttpClient, TestResultsConstants.EnablePublishToTestResultsLibrary, commandContext))
            {
                ITestRunPublisher testRunPublisher = new TestRunPublisher(connection, new CommandTraceListener(_executionContext));

                var publisher = HostContext.GetService<ITestRunDataPublisher>();
                publisher.InitializePublisher(context, teamProject, testRunPublisher);

                var parser = new Parser(context);
                TestDataProvider testDataProvider = parser.ParseTestResultFiles(_testRunner, runContext, _testResultFiles);

                commandContext.Task = PublishTestRunData(publisher, testDataProvider, runContext);
            }
            else
            {
                IResultReader resultReader = GetTestResultReader(_testRunner);
                
                var legacyPublisher = HostContext.GetService<ILegacyTestRunPublisher>();
                legacyPublisher.InitializePublisher(context, connection, teamProject, resultReader);

                if (_mergeResults)
                {
                    commandContext.Task = PublishAllTestResultsToSingleTestRunAsync(_testResultFiles, legacyPublisher, runContext, resultReader.Name, context.CancellationToken);
                }
                else
                {
                    commandContext.Task = PublishToNewTestRunPerTestResultFileAsync(_testResultFiles, legacyPublisher, runContext, resultReader.Name, PublishBatchSize, context.CancellationToken);
                }
            }

            _executionContext.AsyncCommands.Add(commandContext);

            if (_isTestRunOutcomeFailed)
            {
                _executionContext.Result = TaskResult.Failed;
                _executionContext.Error(StringUtil.Loc("FailedTestsInResults"));
            }
            
        }

        /// <summary>
        /// Publish single test run
        /// </summary>
        private async Task PublishAllTestResultsToSingleTestRunAsync(List<string> resultFiles, ILegacyTestRunPublisher publisher, TestRunContext runContext, string resultReader, CancellationToken cancellationToken)
        {
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

                //read results from each file
                foreach (string resultFile in resultFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    //test case results
                    _executionContext.Debug(StringUtil.Format("Reading test results from file '{0}'", resultFile));
                    LegacyTestRunData resultFileRunData = publisher.ReadResultsFromFile(runContext, resultFile);

                    if (_failTaskOnFailedTests)
                    {
                        _isTestRunOutcomeFailed = _isTestRunOutcomeFailed || GetTestRunOutcome(resultFileRunData);
                    }

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
                    string runName = string.IsNullOrWhiteSpace(_runTitle)
                    ? StringUtil.Format("{0}_TestResults_{1}", _testRunner, runContext.BuildId)
                    : _runTitle;

                    if (DateTime.Compare(minStartDate, maxCompleteDate) > 0)
                    {
                        _executionContext.Warning(StringUtil.Loc("InvalidCompletedDate", maxCompleteDate, minStartDate));
                        dateFormatError = true;
                    }

                    minStartDate = DateTime.Equals(minStartDate, DateTime.MaxValue) ? presentTime : minStartDate;
                    maxCompleteDate = dateFormatError || DateTime.Equals(maxCompleteDate, DateTime.MinValue) ? minStartDate.Add(totalTestCaseDuration) : maxCompleteDate;

                    //creat test run
                    LegacyTestRunData testRunData = new LegacyTestRunData(
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
                    testRunData.AddCustomField(_testRunSystemCustomFieldName, _testRunSystem);
                    AddTargetBranchInfoToRunCreateModel(testRunData, runContext.TargetBranchName);

                    TestRun testRun = await publisher.StartTestRunAsync(testRunData, _executionContext.CancellationToken);
                    await publisher.AddResultsAsync(testRun, runResults.ToArray(), _executionContext.CancellationToken);
                    await publisher.EndTestRunAsync(testRunData, testRun.Id, true, _executionContext.CancellationToken);
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                //Do not fail the task.
                LogPublishTestResultsFailureWarning(ex);
            }
        }

        /// <summary>
        /// Publish separate test run for each result file that has results.
        /// </summary>
        private async Task PublishToNewTestRunPerTestResultFileAsync(List<string> resultFiles,
            ILegacyTestRunPublisher publisher,
            TestRunContext runContext,
            string resultReader,
            int batchSize,
            CancellationToken cancellationToken)
        {
            try
            {
                var groupedFiles = resultFiles
                    .Select((resultFile, index) => new { Index = index, file = resultFile })
                    .GroupBy(pair => pair.Index / batchSize)
                    .Select(bucket => bucket.Select(pair => pair.file).ToList())
                    .ToList();

                bool changeTestRunTitle = resultFiles.Count > 1;

                foreach (var files in groupedFiles)
                {
                    // Publish separate test run for each result file that has results.
                    var publishTasks = files.Select(async resultFile =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string runName = _runTitle;
                        if (!string.IsNullOrWhiteSpace(_runTitle) && changeTestRunTitle)
                        {
                            runName = GetRunTitle();
                        }

                        _executionContext.Debug(StringUtil.Format("Reading test results from file '{0}'", resultFile));
                        LegacyTestRunData testRunData = publisher.ReadResultsFromFile(runContext, resultFile, runName);
                        testRunData.PipelineReference = runContext.PipelineReference;
                        if (_failTaskOnFailedTests)
                        {
                            _isTestRunOutcomeFailed = _isTestRunOutcomeFailed || GetTestRunOutcome(testRunData);
                        }

                        cancellationToken.ThrowIfCancellationRequested();

                        if (testRunData != null)
                        {
                            if (testRunData.Results != null && testRunData.Results.Length > 0)
                            {
                                testRunData.AddCustomField(_testRunSystemCustomFieldName, _testRunSystem);
                                AddTargetBranchInfoToRunCreateModel(testRunData, runContext.TargetBranchName);
                                TestRun testRun = await publisher.StartTestRunAsync(testRunData, _executionContext.CancellationToken);
                                await publisher.AddResultsAsync(testRun, testRunData.Results, _executionContext.CancellationToken);
                                await publisher.EndTestRunAsync(testRunData, testRun.Id, cancellationToken: _executionContext.CancellationToken);
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
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                //Do not fail the task.
                LogPublishTestResultsFailureWarning(ex);
            }
        }

        private string GetRunTitle()
        {
            lock (_sync)
            {
                return StringUtil.Format("{0}_{1}", _runTitle, ++_runCounter);
            }
        }

        /// <summary>
        /// Reads a list testRunData Object and returns true if any test case outcome is failed
        /// </summary>
        /// <param name="testRunDataList"></param>
        /// <returns></returns>
        private bool GetTestRunOutcome(LegacyTestRunData testRunData)
        {
            foreach (var testCaseResultData in testRunData.Results)
            {
                if (testCaseResultData.Outcome == TestOutcome.Failed.ToString() || testCaseResultData.Outcome == TestOutcome.Aborted.ToString())
                {
                    return true;
                }
            }
            return false;
        }

        private IResultReader GetTestResultReader(string testRunner)
        {
            var extensionManager = HostContext.GetService<IExtensionManager>();
            IResultReader reader = (extensionManager.GetExtensions<IResultReader>()).FirstOrDefault(x => testRunner.Equals(x.Name, StringComparison.OrdinalIgnoreCase));

            if (reader == null)
            {
                throw new ArgumentException("Unknown Test Runner.");
            }

            reader.AddResultsFileToRunLevelAttachments = _publishRunLevelAttachments;
            return reader;
        }

        private void LoadPublishTestResultsInputs(IExecutionContext context, Dictionary<string, string> eventProperties, string data)
        {
            // Validate input test results files
            string resultFilesInput;
            eventProperties.TryGetValue(PublishTestResultsEventProperties.ResultFiles, out resultFilesInput);
            // To support compat we parse data first. If data is empty parse 'TestResults' parameter
            if (!string.IsNullOrWhiteSpace(data) && data.Split(',').Count() != 0)
            {
                if (context.Container != null)
                {
                    _testResultFiles = data.Split(',').Select(x => context.Container.TranslateToHostPath(x)).ToList();
                }
                else
                {
                    _testResultFiles = data.Split(',').ToList();
                }
            }
            else
            {
                if (string.IsNullOrEmpty(resultFilesInput) || resultFilesInput.Split(',').Count() == 0)
                {
                    throw new ArgumentException(StringUtil.Loc("ArgumentNeeded", "TestResults"));
                }

                if (context.Container != null)
                {
                    _testResultFiles = resultFilesInput.Split(',').Select(x => context.Container.TranslateToHostPath(x)).ToList();
                }
                else
                {
                    _testResultFiles = resultFilesInput.Split(',').ToList();
                }
            }

            //validate testrunner input
            eventProperties.TryGetValue(PublishTestResultsEventProperties.Type, out _testRunner);
            if (string.IsNullOrEmpty(_testRunner))
            {
                throw new ArgumentException(StringUtil.Loc("ArgumentNeeded", "Testrunner"));
            }

            string mergeResultsInput;
            eventProperties.TryGetValue(PublishTestResultsEventProperties.MergeResults, out mergeResultsInput);
            if (string.IsNullOrEmpty(mergeResultsInput) || !bool.TryParse(mergeResultsInput, out _mergeResults))
            {
                // if no proper input is provided by default we merge test results
                _mergeResults = true;
            }

            eventProperties.TryGetValue(PublishTestResultsEventProperties.Platform, out _platform);
            if (_platform == null)
            {
                _platform = string.Empty;
            }

            eventProperties.TryGetValue(PublishTestResultsEventProperties.Configuration, out _configuration);
            if (_configuration == null)
            {
                _configuration = string.Empty;
            }

            eventProperties.TryGetValue(PublishTestResultsEventProperties.RunTitle, out _runTitle);
            if (_runTitle == null)
            {
                _runTitle = string.Empty;
            }

            eventProperties.TryGetValue(PublishTestResultsEventProperties.TestRunSystem, out _testRunSystem);
            if (_testRunSystem == null)
            {
                _testRunSystem = string.Empty;
            }

            string failTaskInput;
            eventProperties.TryGetValue(PublishTestResultsEventProperties.FailTaskOnFailedTests, out failTaskInput);
            if (string.IsNullOrEmpty(failTaskInput) || !bool.TryParse(failTaskInput, out _failTaskOnFailedTests))
            {
                // if no proper input is provided by default fail task is false
                _failTaskOnFailedTests = false;
            }

            string publishRunAttachmentsInput;
            eventProperties.TryGetValue(PublishTestResultsEventProperties.PublishRunAttachments, out publishRunAttachmentsInput);
            if (string.IsNullOrEmpty(publishRunAttachmentsInput) || !bool.TryParse(publishRunAttachmentsInput, out _publishRunLevelAttachments))
            {
                // if no proper input is provided by default we publish attachments.
                _publishRunLevelAttachments = true;
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

        private TestRunContext CreateTestRunContext()
        {
            string releaseUri = null;
            string releaseEnvironmentUri = null;

            string teamProject = _executionContext.Variables.System_TeamProject;
            string owner = _executionContext.Variables.Build_RequestedFor;
            string buildUri = _executionContext.Variables.Build_BuildUri;
            int buildId = _executionContext.Variables.Build_BuildId ?? 0;
            string pullRequestTargetBranchName = _executionContext.Variables.System_PullRequest_TargetBranch;
            string stageName = _executionContext.Variables.System_StageName;
            string phaseName = _executionContext.Variables.System_PhaseName;
            string jobName = _executionContext.Variables.System_JobName;
            int stageAttempt = _executionContext.Variables.System_StageAttempt ?? 0;
            int phaseAttempt = _executionContext.Variables.System_PhaseAttempt ?? 0;
            int jobAttempt = _executionContext.Variables.System_JobAttempt ?? 0;

            //Temporary fix to support publish in RM scenarios where there might not be a valid Build ID associated.
            //TODO: Make a cleaner fix after TCM User Story 401703 is completed.
            if (buildId == 0)
            {
                _platform = _configuration = null;
            }

            if (!string.IsNullOrWhiteSpace(_executionContext.Variables.Release_ReleaseUri))
            {
                releaseUri = _executionContext.Variables.Release_ReleaseUri;
                releaseEnvironmentUri = _executionContext.Variables.Release_ReleaseEnvironmentUri;
            }

            // If runName is not provided by the task, then create runName from testRunner name and buildId.
            string runName = String.IsNullOrWhiteSpace(_runTitle)
                ? String.Format("{0}_TestResults_{1}", _testRunner, buildId)
                : _runTitle;

            StageReference stageReference = new StageReference() { StageName = stageName, Attempt = Convert.ToInt32(stageAttempt) };
            PhaseReference phaseReference = new PhaseReference() { PhaseName = phaseName, Attempt = Convert.ToInt32(phaseAttempt) };
            JobReference jobReference = new JobReference() { JobName = jobName, Attempt = Convert.ToInt32(jobAttempt) };
            PipelineReference pipelineReference = new PipelineReference()
            {
                PipelineId = buildId,
                StageReference = stageReference,
                PhaseReference = phaseReference,
                JobReference = jobReference
            };

            TestRunContext testRunContext = new TestRunContext(
                owner: owner,
                platform: _platform,
                configuration: _configuration,
                buildId: buildId,
                buildUri: buildUri,
                releaseUri: releaseUri,
                releaseEnvironmentUri: releaseEnvironmentUri,
                runName: runName,
                testRunSystem: _testRunSystem,
                buildAttachmentProcessor: new CodeCoverageBuildAttachmentProcessor(),
                targetBranchName: pullRequestTargetBranchName,
                pipelineReference: pipelineReference
            );
            return testRunContext;

        }

        private PublishOptions GetPublishOptions()
        {
            var publishOptions = new PublishOptions()
            {
                IsMergeTestResultsToSingleRun = _mergeResults,
                IsAddTestRunAttachments = _publishRunLevelAttachments
            };

            return publishOptions;
        }

        private async Task PublishTestRunData(ITestRunDataPublisher publisher, TestDataProvider testDataProvider, TestRunContext testRunContext)
        {
            try
            {
                var testRunData = testDataProvider.GetTestRunData();
                await publisher.PublishAsync(testRunContext, testRunData, GetPublishOptions(), _executionContext.CancellationToken);
                
                if (_failTaskOnFailedTests)
                {
                    _isTestRunOutcomeFailed = _isTestRunOutcomeFailed || GetTestRunOutcome(testRunData);
                }
            }
            catch (Exception ex)
            {
                _executionContext.Error("Could not publish test run level data."+ ex);
            }
        }

        private bool GetTestRunOutcome(IList<TestRunData> testRunDataList)
        {
            if (_failTaskOnFailedTests)
            {
                // Reads through each testCaseResult in testRunDataList 
                foreach (var testRunData in testRunDataList)
                {
                    foreach (var testCaseResult in testRunData.TestResults)
                    {
                        // Return true if outcome is failed or aborted
                        if (testCaseResult.Outcome == TestOutcome.Failed.ToString() || testCaseResult.Outcome == TestOutcome.Aborted.ToString())
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private bool IsFeatureFlagEnabled(VssConnection connection, string featureFlagName)
        {
            try
            {
                var publisher = new TestRunDataPublisher();
                var featureAvailabilityHttpClient = connection.GetClient<FeatureAvailabilityHttpClient>();

                FeatureFlag featureFlag = featureAvailabilityHttpClient.GetFeatureFlagByNameAsync(featureFlagName).Result;
                if (featureFlag != null && featureFlag.EffectiveState.Equals("On", StringComparison.OrdinalIgnoreCase))
                {
                    _executionContext.Debug($"{featureFlagName} feature flag is on");
                    return true;
                }

                _executionContext.Debug($"{featureFlagName} feature flag is off");
                return false;
            }
            catch
            {
                _executionContext.Debug("Exception while fetching feature flag value");
                return false;
            }
        }
    }

    internal static class WellKnownResultsCommand
    {
        public static readonly string PublishTestResults = "publish";
    }

    internal static class PublishTestResultsEventProperties
    {
        public static readonly string Type = "type";
        public static readonly string MergeResults = "mergeResults";
        public static readonly string Platform = "platform";
        public static readonly string Configuration = "config";
        public static readonly string RunTitle = "runTitle";
        public static readonly string PublishRunAttachments = "publishRunAttachments";
        public static readonly string ResultFiles = "resultFiles";
        public static readonly string TestRunSystem = "testRunSystem";
        public static readonly string FailTaskOnFailedTests = "failTaskOnFailedTests";
    }
}