// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.IdentityModel.Tokens;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.TeamFoundation.TestClient.PublishTestResults;
using Microsoft.TeamFoundation.TestManagement.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker.Telemetry;
using Microsoft.VisualStudio.Services.Agent.Worker.LegacyTestResults;
using Microsoft.VisualStudio.Services.Agent.Worker.TestResults.Utils;
using Microsoft.VisualStudio.Services.Agent.Worker.CodeCoverage;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.WebPlatform;

namespace Microsoft.VisualStudio.Services.Agent.Worker.TestResults
{
    public sealed class ResultsCommandExtension : BaseWorkerCommandExtension
    {
        public ResultsCommandExtension()
        {
            CommandArea = "results";
            SupportedHostTypes = HostTypes.All;
            InstallWorkerCommand(new PublishTestResultsCommand());
            InstallWorkerCommand(new PublishToEvidenceStoreCommand());
        }
    }

    public sealed class PublishTestResultsCommand : IWorkerCommand
    {
        public string Name => "publish";
        public List<string> Aliases => null;

        //telemetry constants
        private const string _telemetryFeature = "PublishTestResultsCommand";
        private const string _telemetryArea = "TestResults";

        /// <summary>
        /// Bundles all per-invocation state so that concurrent Execute() calls
        /// cannot interfere with each other through shared instance fields.
        /// </summary>
        private sealed class PublishTestResultsInput
        {
            public IExecutionContext ExecutionContext { get; init; }
            public List<string> TestResultFiles { get; init; }
            public string TestRunner { get; init; }
            public bool MergeResults { get; init; }
            public string Platform { get; init; }
            public string Configuration { get; init; }
            public string RunTitle { get; init; }
            public bool PublishRunLevelAttachments { get; init; }
            public TestCaseResult[] TestCaseResults { get; init; }
            public string TestPlanId { get; init; }
            public bool PublishTestResultsLibFeatureState { get; init; }
            public bool TriggerCoverageMergeJobFeatureState { get; init; }
            public bool FailTaskOnFailedTests { get; init; }
            public bool IsDetectTestRunRetry { get; init; }
            public string TestRunSystem { get; init; }
            public Dictionary<string, object> TelemetryProperties { get; init; }
        }

        public void Execute(IExecutionContext context, Command command)
        {
            ArgUtil.NotNull(context, nameof(context));
            ArgUtil.NotNull(command, nameof(command));
            var data = command.Data;
            var eventProperties = command.Properties;

            var telemetryProperties = PopulateTelemetryData(context);

            PublishTestResultsInput input = LoadPublishTestResultsInputs(
                context, eventProperties, data,
                telemetryProperties);

            string teamProject = context.Variables.System_TeamProject;

            TestRunContext runContext = CreateTestRunContext(input);

            var commandContext = context.GetHostContext().CreateService<IAsyncCommandContext>();
            commandContext.InitializeCommandContext(context, StringUtil.Loc("PublishTestResults"));
            commandContext.Task = PublishTestRunDataAsync(input, teamProject, runContext);
            input.ExecutionContext.AsyncCommands.Add(commandContext);
        }

        private PublishTestResultsInput LoadPublishTestResultsInputs(
            IExecutionContext context,
            Dictionary<string, string> eventProperties,
            string data,
            Dictionary<string, object> telemetryProperties)
        {
            // Load feature flag state
            bool publishTestResultsLibFeatureState;
            bool triggerCoverageMergeJobFeatureState;
            using (var connection = WorkerUtilities.GetVssConnection(context))
            {
                var featureFlagService = context.GetHostContext().CreateService<IFeatureFlagService>();
                featureFlagService.InitializeFeatureService(context, connection);
                publishTestResultsLibFeatureState = featureFlagService.GetFeatureFlagState(TestResultsConstants.UsePublishTestResultsLibFeatureFlag, TestResultsConstants.TFSServiceInstanceGuid);
                triggerCoverageMergeJobFeatureState = featureFlagService.GetFeatureFlagState(CodeCoverageConstants.TriggerCoverageMergeJobFF, TestResultsConstants.TFSServiceInstanceGuid);
            }

            // Validate input test results files
            List<string> testResultFiles;
            string resultFilesInput;
            eventProperties.TryGetValue(PublishTestResultsEventProperties.ResultFiles, out resultFilesInput);
            // To support compat we parse data first. If data is empty parse 'TestResults' parameter
            if (!string.IsNullOrWhiteSpace(data) && data.Split(',').Count() != 0)
            {
                testResultFiles = data.Split(',').Select(x => context.TranslateToHostPath(x)).ToList();
            }
            else
            {
                if (string.IsNullOrEmpty(resultFilesInput) || resultFilesInput.Split(',').Count() == 0)
                {
                    throw new ArgumentException(StringUtil.Loc("ArgumentNeeded", "TestResults"));
                }

                testResultFiles = resultFilesInput.Split(',').Select(x => context.TranslateToHostPath(x)).ToList();
            }

            //validate testrunner input
            string testRunner;
            eventProperties.TryGetValue(PublishTestResultsEventProperties.Type, out testRunner);
            if (string.IsNullOrEmpty(testRunner))
            {
                throw new ArgumentException(StringUtil.Loc("ArgumentNeeded", "Testrunner"));
            }

            bool mergeResults;
            string mergeResultsInput;
            eventProperties.TryGetValue(PublishTestResultsEventProperties.MergeResults, out mergeResultsInput);
            if (string.IsNullOrEmpty(mergeResultsInput) || !bool.TryParse(mergeResultsInput, out mergeResults))
            {
                // if no proper input is provided by default we merge test results
                mergeResults = true;
            }

            string platform;
            eventProperties.TryGetValue(PublishTestResultsEventProperties.Platform, out platform);
            if (platform == null)
            {
                platform = string.Empty;
            }

            string configuration;
            eventProperties.TryGetValue(PublishTestResultsEventProperties.Configuration, out configuration);
            if (configuration == null)
            {
                configuration = string.Empty;
            }

            string runTitle;
            eventProperties.TryGetValue(PublishTestResultsEventProperties.RunTitle, out runTitle);
            if (runTitle == null)
            {
                runTitle = string.Empty;
            }

            string testRunSystem;
            eventProperties.TryGetValue(PublishTestResultsEventProperties.TestRunSystem, out testRunSystem);
            if (testRunSystem == null)
            {
                testRunSystem = string.Empty;
            }

            bool failTaskOnFailedTests;
            string failTaskInput;
            eventProperties.TryGetValue(PublishTestResultsEventProperties.FailTaskOnFailedTests, out failTaskInput);
            if (string.IsNullOrEmpty(failTaskInput) || !bool.TryParse(failTaskInput, out failTaskOnFailedTests))
            {
                // if no proper input is provided by default fail task is false
                failTaskOnFailedTests = false;
            }

            bool publishRunLevelAttachments;
            string publishRunAttachmentsInput;
            eventProperties.TryGetValue(PublishTestResultsEventProperties.PublishRunAttachments, out publishRunAttachmentsInput);
            if (string.IsNullOrEmpty(publishRunAttachmentsInput) || !bool.TryParse(publishRunAttachmentsInput, out publishRunLevelAttachments))
            {
                // if no proper input is provided by default we publish attachments.
                publishRunLevelAttachments = true;
            }

            TestCaseResult[] testCaseResults = null;
            string jsonString;
            eventProperties.TryGetValue(PublishTestResultsEventProperties.ListOfAutomatedTestPoints, out jsonString);
            if (!string.IsNullOrEmpty(jsonString))
            {
                testCaseResults = Newtonsoft.Json.JsonConvert.DeserializeObject<TestCaseResult[]>(jsonString);
            }

            string testPlanId = null;
            string testPlanIdInput;
            eventProperties.TryGetValue(PublishTestResultsEventProperties.TestPlanId, out testPlanIdInput);
            if (!string.IsNullOrEmpty(testPlanIdInput))
            {
                testPlanId = testPlanIdInput;
            }

            bool isDetectTestRunRetry;
            string isDetectTestRunRetryInput;
            eventProperties.TryGetValue(PublishTestResultsEventProperties.IsDetectTestRunRetry, out isDetectTestRunRetryInput);
            if (string.IsNullOrEmpty(isDetectTestRunRetryInput) || !bool.TryParse(isDetectTestRunRetryInput, out isDetectTestRunRetry))
            {
                // if no proper input is provided by default we do not detect test run retry.
                isDetectTestRunRetry = false;
            }

            return new PublishTestResultsInput
            {
                ExecutionContext = context,
                TestResultFiles = testResultFiles,
                TestRunner = testRunner,
                MergeResults = mergeResults,
                Platform = platform,
                Configuration = configuration,
                RunTitle = runTitle,
                PublishRunLevelAttachments = publishRunLevelAttachments,
                TestCaseResults = testCaseResults,
                TestPlanId = testPlanId,
                PublishTestResultsLibFeatureState = publishTestResultsLibFeatureState,
                TriggerCoverageMergeJobFeatureState = triggerCoverageMergeJobFeatureState,
                FailTaskOnFailedTests = failTaskOnFailedTests,
                IsDetectTestRunRetry = isDetectTestRunRetry,
                TestRunSystem = testRunSystem,
                TelemetryProperties = telemetryProperties
            };
        }

        private static void LogPublishTestResultsFailureWarning(IExecutionContext executionContext, Exception ex)
        {
            string message = ex.Message;
            if (ex.InnerException != null)
            {
                message += Environment.NewLine;
                message += ex.InnerException.Message;
            }
            executionContext.Warning(StringUtil.Loc("FailedToPublishTestResults", message));
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

        private static TestRunContext CreateTestRunContext(PublishTestResultsInput input)
        {
            string releaseUri = null;
            string releaseEnvironmentUri = null;

            var executionContext = input.ExecutionContext;
            var platform = input.Platform;
            var configuration = input.Configuration;

            string teamProject = executionContext.Variables.System_TeamProject;
            string owner = executionContext.Variables.Build_RequestedFor;
            string buildUri = executionContext.Variables.Build_BuildUri;
            int buildId = executionContext.Variables.Build_BuildId ?? 0;
            string pullRequestTargetBranchName = executionContext.Variables.System_PullRequest_TargetBranch;
            string stageName = executionContext.Variables.System_StageName;
            string phaseName = executionContext.Variables.System_PhaseName;
            string jobName = executionContext.Variables.System_JobName;
            int stageAttempt = executionContext.Variables.System_StageAttempt ?? 0;
            int phaseAttempt = executionContext.Variables.System_PhaseAttempt ?? 0;
            int jobAttempt = executionContext.Variables.System_JobAttempt ?? 0;

            //Temporary fix to support publish in RM scenarios where there might not be a valid Build ID associated.
            //TODO: Make a cleaner fix after TCM User Story 401703 is completed.
            if (buildId == 0)
            {
                platform = configuration = null;
            }

            if (!string.IsNullOrWhiteSpace(executionContext.Variables.Release_ReleaseUri))
            {
                releaseUri = executionContext.Variables.Release_ReleaseUri;
                releaseEnvironmentUri = executionContext.Variables.Release_ReleaseEnvironmentUri;
            }

            // If runName is not provided by the task, then create runName from testRunner name and buildId.
            string runName = String.IsNullOrWhiteSpace(input.RunTitle)
                ? String.Format("{0}_TestResults_{1}", input.TestRunner, buildId)
                : input.RunTitle;

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

            TestRunContext testRunContext;

            if (!string.IsNullOrEmpty(input.TestPlanId))
            {
                ShallowReference testPlanObject = new() { Id = input.TestPlanId };

                testRunContext = new(
                owner: owner,
                platform: platform,
                configuration: configuration,
                buildId: buildId,
                buildUri: buildUri,
                releaseUri: releaseUri,
                releaseEnvironmentUri: releaseEnvironmentUri,
                runName: runName,
                testRunSystem: input.TestRunSystem,
                buildAttachmentProcessor: new CodeCoverageBuildAttachmentProcessor(),
                targetBranchName: pullRequestTargetBranchName,
                pipelineReference: pipelineReference,
                testPlan: testPlanObject);

                return testRunContext;
            }

            testRunContext = new TestRunContext(
                owner: owner,
                platform: platform,
                configuration: configuration,
                buildId: buildId,
                buildUri: buildUri,
                releaseUri: releaseUri,
                releaseEnvironmentUri: releaseEnvironmentUri,
                runName: runName,
                testRunSystem: input.TestRunSystem,
                buildAttachmentProcessor: new CodeCoverageBuildAttachmentProcessor(),
                targetBranchName: pullRequestTargetBranchName,
                pipelineReference: pipelineReference);

            return testRunContext;

        }

        private static PublishOptions GetPublishOptions(PublishTestResultsInput input)
        {
            var publishOptions = new PublishOptions()
            {
                IsMergeTestResultsToSingleRun = input.MergeResults,
                IsAddTestRunAttachments = input.PublishRunLevelAttachments,
                IsDetectTestRunRetry = input.IsDetectTestRunRetry
            };

            return publishOptions;
        }

        private async Task PublishTestRunDataAsync(
            PublishTestResultsInput input, string teamProject, TestRunContext testRunContext)
        {
            bool isTestRunOutcomeFailed = false;
            var executionContext = input.ExecutionContext;
            var telemetryProperties = input.TelemetryProperties;

            telemetryProperties.Add("UsePublishTestResultsLib", input.PublishTestResultsLibFeatureState);
            using (var connection = WorkerUtilities.GetVssConnection(executionContext))
            {

                //This check is to determine to use "Microsoft.TeamFoundation.PublishTestResults" Library or the agent code to parse and publish the test results.
                if (input.PublishTestResultsLibFeatureState)
                {
                    var publisher = executionContext.GetHostContext().CreateService<ITestDataPublisher>();
                    publisher.InitializePublisher(executionContext, teamProject, connection, input.TestRunner);

                    var publishOptions = GetPublishOptions(input);
                    if (!input.TestCaseResults.IsNullOrEmpty() && !input.TestPlanId.IsNullOrEmpty())
                    {
                        isTestRunOutcomeFailed = await publisher.PublishAsync(testRunContext, input.TestResultFiles, input.TestCaseResults, publishOptions, executionContext.CancellationToken);
                    }
                    else
                    {
                        isTestRunOutcomeFailed = await publisher.PublishAsync(testRunContext, input.TestResultFiles, publishOptions, executionContext.CancellationToken);
                    }
                }
                else
                {
                    var publisher = executionContext.GetHostContext().CreateService<ILegacyTestRunDataPublisher>();
                    publisher.InitializePublisher(executionContext, teamProject, connection, input.TestRunner, input.PublishRunLevelAttachments);

                    isTestRunOutcomeFailed = await publisher.PublishAsync(testRunContext, input.TestResultFiles, input.RunTitle, executionContext.Variables.Build_BuildId, input.MergeResults);
                }

                if (isTestRunOutcomeFailed && input.FailTaskOnFailedTests)
                {
                    executionContext.Result = TaskResult.Failed;
                    executionContext.Error(StringUtil.Loc("FailedTestsInResults"));
                }

                await PublishEventsAsync(connection, executionContext, telemetryProperties);
                if (input.TriggerCoverageMergeJobFeatureState)
                {
                    await TriggerCoverageMergeJobAsync(input.TestResultFiles, executionContext);
                }
            }
        }

        // Queue code coverage merge job if code coverage attachments are published to avoid BQC timeout.
        private async Task TriggerCoverageMergeJobAsync(List<string> resultFilesInput, IExecutionContext context)
        {
            try
            {
                ITestResultsServer _testResultsServer = context.GetHostContext().CreateService<ITestResultsServer>();
                using (var connection = WorkerUtilities.GetVssConnection(context))
                {
                    foreach (var resultFile in resultFilesInput)
                    {
                        string text = File.ReadAllText(resultFile);
                        XmlDocument xdoc = new XmlDocument();
                        xdoc.LoadXml(text);
                        XmlNodeList nodes = xdoc.GetElementsByTagName("A");

                        foreach (XmlNode attachmentNode in nodes)
                        {
                            var file = attachmentNode.Attributes?["href"]?.Value;
                            if (!string.IsNullOrEmpty(file))
                            {
                                if (
                                    Path.GetExtension(file).Equals(".covx", StringComparison.OrdinalIgnoreCase) ||
                                    Path.GetExtension(file).Equals(".covb", StringComparison.OrdinalIgnoreCase) ||
                                    Path.GetExtension(file).Equals(".coverage", StringComparison.OrdinalIgnoreCase)
                                    )
                                {
                                    _testResultsServer.InitializeServer(connection, context);
                                    try
                                    {
                                        await _testResultsServer.UpdateCodeCoverageSummaryAsync(connection, context.Variables.System_TeamProjectId.ToString(), context.Variables.Build_BuildId.GetValueOrDefault());
                                    }
                                    catch (Exception e)
                                    {
                                        context.Section($"Could not queue code coverage merge:{e}");
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                context.Debug($"Exception in Method:{e.Message}");
            }
        }

        private async Task PublishEventsAsync(VssConnection connection, IExecutionContext executionContext, Dictionary<string, object> telemetryProperties)
        {
            try
            {
                CustomerIntelligenceEvent ciEvent = new CustomerIntelligenceEvent()
                {
                    Area = _telemetryArea,
                    Feature = _telemetryFeature,
                    Properties = telemetryProperties
                };

                var ciService = executionContext.GetHostContext().CreateService<ICustomerIntelligenceServer>();
                ciService.Initialize(connection);
                await ciService.PublishEventsAsync(new CustomerIntelligenceEvent[] { ciEvent });
            }
            catch (Exception ex)
            {
                executionContext.Debug(StringUtil.Loc("TelemetryCommandFailed", ex.Message));
            }
        }

        private static Dictionary<string, object> PopulateTelemetryData(IExecutionContext executionContext)
        {
            var telemetryProperties = new Dictionary<string, object>();
            telemetryProperties.Add("ExecutionId", executionContext.Id);
            telemetryProperties.Add("BuildId", executionContext.Variables.Build_BuildId);
            telemetryProperties.Add("BuildUri", executionContext.Variables.Build_BuildUri);
            telemetryProperties.Add("Attempt", executionContext.Variables.System_JobAttempt);
            telemetryProperties.Add("ProjectId", executionContext.Variables.System_TeamProjectId);
            telemetryProperties.Add("ProjectName", executionContext.Variables.System_TeamProject);

            if (!string.IsNullOrWhiteSpace(executionContext.Variables.Release_ReleaseUri))
            {
                telemetryProperties.Add("ReleaseUri", executionContext.Variables.Release_ReleaseUri);
                telemetryProperties.Add("ReleaseId", executionContext.Variables.Release_ReleaseId);
            }

            return telemetryProperties;
        }
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
        public static readonly string ListOfAutomatedTestPoints = "listOfAutomatedTestPoints";
        public static readonly string TestPlanId = "testPlanId";
        public static readonly string IsDetectTestRunRetry = "isDetectTestRunRetry";
    }
}
