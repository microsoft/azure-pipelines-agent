using Microsoft.TeamFoundation.TestManagement.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.FeatureAvailability.WebApi;
using Microsoft.VisualStudio.Services.TestResults.WebApi;
using Microsoft.VisualStudio.Services.WebApi;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.VisualStudio.Services.Agent.Worker.TestResults
{
    [ServiceLocator(Default = typeof(TestResultsServer))]
    public interface ITestResultsServer : IAgentService
    {
        void InitializeServer(VssConnection connection);
        Task<List<TestCaseResult>> AddTestResultsToTestRunAsync(TestCaseResult[] currentBatch, string projectName, int testRunId, CancellationToken cancellationToken = default(CancellationToken));
        Task<TestRun> CreateTestRunAsync(string projectName, RunCreateModel testRunData, CancellationToken cancellationToken = default(CancellationToken));
        Task<TestRun> UpdateTestRunAsync(string projectName, int testRunId, RunUpdateModel updateModel, CancellationToken cancellationToken = default(CancellationToken));
        Task<TestAttachmentReference> CreateTestRunAttachmentAsync(TestAttachmentRequestModel reqModel, string projectName, int testRunId, CancellationToken cancellationToken = default(CancellationToken));
        Task<TestAttachmentReference> CreateTestResultAttachmentAsync(TestAttachmentRequestModel reqModel, string projectName, int testRunId, int testCaseResultId, CancellationToken cancellationToken = default(CancellationToken));
        Task<TestAttachmentReference> CreateTestSubResultAttachmentAsync(TestAttachmentRequestModel reqModel, string projectName, int testRunId, int testCaseResultId, int testSubResultId, CancellationToken cancellationToken = default(CancellationToken));
    }

    public class TestResultsServer : AgentService, ITestResultsServer
    {
        private VssConnection _connection;

        private ITestResultsHttpClient TestHttpClient { get; set; }

        public void InitializeServer(VssConnection connection)
        {
            ArgUtil.NotNull(connection, nameof(connection));
            _connection = connection;
            FeatureAvailabilityHttpClient featureAvailabilityHttpClient = connection.GetClient<FeatureAvailabilityHttpClient>();
            if (GetFeatureFlagState(featureAvailabilityHttpClient, EnablePublishToTcmServiceDirectlyFromTaskFF))
            {
                TestHttpClient = connection.GetClient<TestResultsHttpClient>();
            }
            else
            {
                TestHttpClient = connection.GetClient<TestManagementHttpClient>();
            }
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

        private const string EnablePublishToTcmServiceDirectlyFromTaskFF = "TestManagement.Server.EnablePublishToTcmServiceDirectlyFromTask";

        private static bool GetFeatureFlagState(FeatureAvailabilityHttpClient featureAvailabilityHttpClient, string FFName)
        {
            try
            {
                var featureFlag = featureAvailabilityHttpClient?.GetFeatureFlagByNameAsync(FFName).Result;
                if (featureFlag != null && featureFlag.EffectiveState.Equals("Off", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }
            return true;
        }
    }
}
