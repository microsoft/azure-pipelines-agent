using Microsoft.TeamFoundation.TestManagement.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker.Release.ContainerProvider.Helpers;
using Microsoft.VisualStudio.Services.WebApi;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StringUtil = Microsoft.VisualStudio.Services.Agent.Util.StringUtil;

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
        private readonly object _sync = new object();
        private AsyncLazy<TestRun> testRunInitializationAsyncLazy;
        private ManualResetEventSlim resultsExist = new ManualResetEventSlim(false);
        private ManualResetEventSlim finishedParsing = new ManualResetEventSlim(false);
        private List<Task> publishTasks = new List<Task>();
        private ConcurrentQueue<TestCaseResultData[]> resultsQueue = new ConcurrentQueue<TestCaseResultData[]>();
        private SemaphoreSlim publishTaskThrottler = new SemaphoreSlim(10);

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

            LoadPublishTestResultsInputs(eventProperties, data);

            string teamProject = context.Variables.System_TeamProject;
            string owner = context.Variables.Build_RequestedFor;
            string buildUri = context.Variables.Build_BuildUri;
            int buildId = context.Variables.Build_BuildId ?? 0;

            //Temporary fix to support publish in RM scenarios where there might not be a valid Build ID associated.
            //TODO: Make a cleaner fix after TCM User Story 401703 is completed.
            if (buildId == 0)
            {
                _platform = _configuration = null;
            }

            string releaseUri = null;
            string releaseEnvironmentUri = null;

            // Check to identify if we are in the Release management flow; if not, then release fields will be kept null while publishing to TCM 
            if (!string.IsNullOrWhiteSpace(context.Variables.Release_ReleaseUri))
            {
                releaseUri = context.Variables.Release_ReleaseUri;
                releaseEnvironmentUri = context.Variables.Release_ReleaseEnvironmentUri;
            }

            IResultReader resultReader = GetTestResultReader(_testRunner);
            TestRunContext runContext = new TestRunContext(owner, _platform, _configuration, buildId, buildUri, releaseUri, releaseEnvironmentUri);
            VssConnection connection = WorkerUtilities.GetVssConnection(_executionContext);

            var publisher = HostContext.GetService<ITestRunPublisher>();
            publisher.InitializePublisher(context, connection, teamProject, resultReader);

            var commandContext = HostContext.CreateService<IAsyncCommandContext>();
            commandContext.InitializeCommandContext(context, StringUtil.Loc("PublishTestResults"));

            commandContext.Task = _mergeResults
                ? PublishAllTestResultsToSingleTestRunAsync(_testResultFiles, publisher, buildId, runContext,
                    resultReader.Name, context.CancellationToken)
                : PublishToNewTestRunPerTestResultFileAsync(_testResultFiles, publisher, runContext, resultReader.Name,
                    PublishBatchSize, context.CancellationToken);
            _executionContext.AsyncCommands.Add(commandContext);
        }

        /// <summary>
        /// Publish single test run
        /// </summary>
        private async Task PublishAllTestResultsToSingleTestRunAsync(IList<string> resultFiles, ITestRunPublisher publisher, int buildId, TestRunContext runContext, string resultReader, CancellationToken cancellationToken)
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
                InitializeTestRunAsyncLazy(publisher, buildId, runContext);

                if (resultFiles.Count == 0)
                {
                    return;
                }

                // Starting a Task which will publish the results from the resultsQueue.
                var publishingTask = Task.Run(() => PublishResults(publisher, cancellationToken));

                // read results from each file
                foreach (string resultFile in resultFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    //test case results
                    _executionContext.Debug(StringUtil.Format("Reading test results from file '{0}'", resultFile));
                    TestRunData resultFileRunData = publisher.ReadResultsFromFile(runContext, resultFile);

                    if (resultFileRunData?.Results != null && resultFileRunData.Results.Length > 0)
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
                        foreach (var tcResult in resultFileRunData.Results)
                        {
                            var durationInMs = Convert.ToInt32(tcResult.DurationInMs);
                            totalTestCaseDuration = totalTestCaseDuration.Add(TimeSpan.FromMilliseconds(durationInMs));
                        }

                        if (resultFileRunData.Results.Length > 0)
                        {
                            resultsExist.Set();
                            resultsQueue.Enqueue(resultFileRunData.Results);
                        }

                        //run attachments
                        if (resultFileRunData.Attachments != null)
                        {
                            runAttachments.AddRange(resultFileRunData.Attachments);
                        }
                    }
                    else
                    {
                        _executionContext.Warning(StringUtil.Loc("InvalidResultFiles", resultFile, resultReader));
                    }
                }

                finishedParsing.Set();

                if(resultsExist.IsSet)
                {
                    // Waiting for the publishing task to complete.
                    publishingTask.Wait(cancellationToken);

                    if (DateTime.Compare(minStartDate, maxCompleteDate) > 0)
                    {
                        _executionContext.Warning(StringUtil.Loc("InvalidCompletedDate", maxCompleteDate, minStartDate));
                        dateFormatError = true;
                    }

                    minStartDate = DateTime.Equals(minStartDate, DateTime.MaxValue) ? presentTime : minStartDate;
                    maxCompleteDate = dateFormatError || DateTime.Equals(maxCompleteDate, DateTime.MinValue)
                        ? minStartDate.Add(totalTestCaseDuration)
                        : maxCompleteDate;


                    var testRunDataEnd =
                        new TestRunData(startedDate: minStartDate.ToString("o"),
                            completedDate: maxCompleteDate.ToString("o")) {Attachments = runAttachments.ToArray()};
                    await publisher.EndTestRunAsync(testRunDataEnd, testRunInitializationAsyncLazy.Value.Result.Id, true, _executionContext.CancellationToken);
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                finishedParsing.Set();

                //Do not fail the task.
                LogPublishTestResultsFailureWarning(ex);
            }
        }

        private async Task PublishResults(ITestRunPublisher publisher, CancellationToken cancellationToken)
        {
            while (resultsQueue.Count > 0 || !finishedParsing.IsSet)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (resultsQueue.TryDequeue(out var results))
                {
                    await publishTaskThrottler.WaitAsync(cancellationToken);

                    var pTask = Task.Run(async () =>
                    {
                        try
                        {
                            await publisher.AddResultsAsync(testRunInitializationAsyncLazy.Value.Result, results,
                                _executionContext.CancellationToken);
                        }
                        finally
                        {
                            publishTaskThrottler.Release();
                        }
                    });
                    publishTasks.Add(pTask);
                }
            }
            // Waiting for all the publishing tasks to complete
            Task.WaitAll(publishTasks.ToArray());
        }

        private void InitializeTestRunAsyncLazy(ITestRunPublisher publisher, int buildId, TestRunContext runContext)
        {
            string runName = string.IsNullOrWhiteSpace(_runTitle)
                ? StringUtil.Format("{0}_TestResults_{1}", _testRunner, buildId)
                : _runTitle;

            // create test run
            TestRunData testRunData = new TestRunData(
                name: runName,
                state: "InProgress",
                isAutomated: true,
                buildId: runContext?.BuildId ?? 0,
                buildFlavor: runContext?.Configuration ?? string.Empty,
                buildPlatform: runContext?.Platform ?? string.Empty,
                releaseUri: runContext?.ReleaseUri,
                releaseEnvironmentUri: runContext?.ReleaseEnvironmentUri
            );

            testRunInitializationAsyncLazy = new AsyncLazy<TestRun>(async () =>
                await publisher.StartTestRunAsync(testRunData, _executionContext.CancellationToken));
        }

        /// <summary>
        /// Publish separate test run for each result file that has results.
        /// </summary>
        private async Task PublishToNewTestRunPerTestResultFileAsync(List<string> resultFiles,
            ITestRunPublisher publisher,
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

                foreach (var files in groupedFiles)
                {
                    // Publish separate test run for each result file that has results.
                    var publishTasks = files.Select(async resultFile =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string runName = null;
                        if (!string.IsNullOrWhiteSpace(_runTitle))
                        {
                            runName = GetRunTitle();
                        }

                        _executionContext.Debug(StringUtil.Format("Reading test results from file '{0}'", resultFile));
                        TestRunData testRunData = publisher.ReadResultsFromFile(runContext, resultFile, runName);

                        cancellationToken.ThrowIfCancellationRequested();

                        if (testRunData != null && testRunData.Results != null && testRunData.Results.Length > 0)
                        {
                            TestRun testRun = await publisher.StartTestRunAsync(testRunData, _executionContext.CancellationToken);
                            await publisher.AddResultsAsync(testRun, testRunData.Results, _executionContext.CancellationToken);
                            await publisher.EndTestRunAsync(testRunData, testRun.Id, cancellationToken: _executionContext.CancellationToken);
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

        private void LoadPublishTestResultsInputs(Dictionary<string, string> eventProperties, string data)
        {
            // Validate input test results files
            string resultFilesInput;
            eventProperties.TryGetValue(PublishTestResultsEventProperties.ResultFiles, out resultFilesInput);
            // To support compat we parse data first. If data is empty parse 'TestResults' parameter
            if (!string.IsNullOrWhiteSpace(data) && data.Split(',').Count() != 0)
            {
                _testResultFiles = data.Split(',').ToList();
            }
            else
            {
                if (string.IsNullOrEmpty(resultFilesInput) || resultFilesInput.Split(',').Count() == 0)
                {
                    throw new ArgumentException(StringUtil.Loc("ArgumentNeeded", "TestResults"));
                }
                _testResultFiles = resultFilesInput.Split(',').ToList();
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
    }
}