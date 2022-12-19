// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

﻿using Microsoft.TeamFoundation.TestManagement.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.FeatureAvailability.WebApi;
using Microsoft.VisualStudio.Services.TestResults.WebApi;
using Microsoft.VisualStudio.Services.WebApi;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.VisualStudio.Services.Agent.Worker.LegacyTestResults
{
    [ServiceLocator(Default = typeof(TestResultsServer))]
    public interface ITestResultsServer : IAgentService
    {
        void InitializeServer(VssConnection connection, IExecutionContext executionContext);
        Task<List<TestCaseResult>> AddTestResultsToTestRunAsync(TestCaseResult[] currentBatch, string projectName, int testRunId, CancellationToken cancellationToken = default(CancellationToken));
        Task<TestRun> CreateTestRunAsync(string projectName, RunCreateModel testRunData, CancellationToken cancellationToken = default(CancellationToken));
        Task<TestRun> UpdateTestRunAsync(string projectName, int testRunId, RunUpdateModel updateModel, CancellationToken cancellationToken = default(CancellationToken));
        Task<TestAttachmentReference> CreateTestRunAttachmentAsync(TestAttachmentRequestModel reqModel, string projectName, int testRunId, CancellationToken cancellationToken = default(CancellationToken));
        Task<TestAttachmentReference> CreateTestResultAttachmentAsync(TestAttachmentRequestModel reqModel, string projectName, int testRunId, int testCaseResultId, CancellationToken cancellationToken = default(CancellationToken));
        Task<TestAttachmentReference> CreateTestSubResultAttachmentAsync(TestAttachmentRequestModel reqModel, string projectName, int testRunId, int testCaseResultId, int testSubResultId, CancellationToken cancellationToken = default(CancellationToken));
        Task<TestResultsSettings> GetTestResultsSettingsAsync(string project, TestResultsSettingsType? settingsType = null, object userState = null, CancellationToken cancellationToken = default(CancellationToken));
        Task<TestRunStatistic> GetTestRunStatisticsAsync(string project,int runId,object userState = null,
            CancellationToken cancellationToken = default(CancellationToken));
        Task<TestRunStatistic> GetTestRunSummaryByOutcomeAsync(string project,int runId,object userState = null,
            CancellationToken cancellationToken = default);
        Task<CodeCoverageSummary> UpdateCodeCoverageSummaryAsync(VssConnection connection, string project, int buildId);
    }

    public class TestResultsServer : AgentService, ITestResultsServer
    {
        private VssConnection _connection;

        private ITestResultsHttpClient TestHttpClient { get; set; }

        public void InitializeServer(VssConnection connection, IExecutionContext executionContext)
        {
            ArgUtil.NotNull(connection, nameof(connection));
            _connection = connection;

            if (GetFeatureFlagState(executionContext, connection, EnablePublishToTcmServiceDirectlyFromTaskFF))
            {
                TestHttpClient = connection.GetClient<TestResultsHttpClient>();
            }
            else
            {
                TestHttpClient = connection.GetClient<TestManagementHttpClient>();
            }
        }
        
        public async Task<CodeCoverageSummary> UpdateCodeCoverageSummaryAsync(VssConnection connection ,string project, int buildId)
        {
            TestResultsHttpClient tcmClient = connection.GetClient<TestResultsHttpClient>();
            return await tcmClient.UpdateCodeCoverageSummaryAsync(project, buildId);     
        }

        public async Task<List<TestCaseResult>> AddTestResultsToTestRunAsync(
            TestCaseResult[] currentBatch,
            string projectName,
            int testRunId,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return await TestHttpClient.AddTestResultsToTestRunAsync(currentBatch, projectName, testRunId, cancellationToken);
        }

        public async Task<TestRun> CreateTestRunAsync(
           string projectName,
           RunCreateModel testRunData,
           CancellationToken cancellationToken = default(CancellationToken))
        {
            return await TestHttpClient.CreateTestRunAsync(testRunData, projectName, cancellationToken);
        }

        public async Task<TestRun> UpdateTestRunAsync(
         string projectName,
         int testRunId,
         RunUpdateModel updateModel,
         CancellationToken cancellationToken = default(CancellationToken))
        {
            return await TestHttpClient.UpdateTestRunAsync(updateModel, projectName, testRunId, cancellationToken);
        }

        public async Task<TestAttachmentReference> CreateTestRunAttachmentAsync(
            TestAttachmentRequestModel reqModel,
            string projectName,
            int testRunId,
         CancellationToken cancellationToken = default(CancellationToken))
        {
            return await TestHttpClient.CreateTestRunAttachmentAsync(reqModel, projectName, testRunId, cancellationToken);
        }

        public async Task<TestAttachmentReference> CreateTestResultAttachmentAsync(
            TestAttachmentRequestModel reqModel,
            string projectName,
            int testRunId,
            int testCaseResultId,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return await TestHttpClient.CreateTestResultAttachmentAsync(reqModel, projectName, testRunId, testCaseResultId, cancellationToken);
        }

        public async Task<TestAttachmentReference> CreateTestSubResultAttachmentAsync(
            TestAttachmentRequestModel reqModel,
            string projectName,
            int testRunId,
            int testCaseResultId,
            int testSubResultId,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return await TestHttpClient.CreateTestSubResultAttachmentAsync(reqModel, projectName, testRunId, testCaseResultId, testSubResultId, cancellationToken);
        }

        public async Task<TestResultsSettings> GetTestResultsSettingsAsync(string project, TestResultsSettingsType? settingsType = null, object userState = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            return await TestHttpClient.GetTestResultsSettingsAsync(project, settingsType);
        }

        public async Task<TestRunStatistic> GetTestRunStatisticsAsync(
            string project,
            int runId,
            object userState = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return await TestHttpClient.GetTestRunStatisticsAsync(project, runId, cancellationToken);
        }

        public async Task<TestRunStatistic> GetTestRunSummaryByOutcomeAsync(string project, int runId, object userState = null,
            CancellationToken cancellationToken = default)
        {
            return await TestHttpClient.GetTestRunSummaryByOutcomeAsync(project, runId, cancellationToken: cancellationToken);
        }

        private static bool GetFeatureFlagState(IExecutionContext executionContext, VssConnection connection, string FFName)
        {
            try
            {
                FeatureAvailabilityHttpClient featureAvailabilityHttpClient = connection.GetClient<FeatureAvailabilityHttpClient>(TFSServiceInstanceGuid);
                var featureFlag = featureAvailabilityHttpClient?.GetFeatureFlagByNameAsync(FFName).Result;
                if (featureFlag != null && featureFlag.EffectiveState.Equals("On", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch(Exception ex)
            {
                executionContext.Output("Unable to get the FF: " + EnablePublishToTcmServiceDirectlyFromTaskFF + ". Reason: " + ex.Message);
            }

            return false;
        }

        private static Guid TFSServiceInstanceGuid = new Guid("00025394-6065-48CA-87D9-7F5672854EF7");
        private const string EnablePublishToTcmServiceDirectlyFromTaskFF = "TestManagement.Server.EnablePublishToTcmServiceDirectlyFromTask";
    }
}
