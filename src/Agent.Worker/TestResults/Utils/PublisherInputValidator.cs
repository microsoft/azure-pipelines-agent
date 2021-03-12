using System;
using System.Collections.Generic;
using Microsoft.TeamFoundation.TestClient.PublishTestResults;
using Microsoft.TeamFoundation.TestManagement.WebApi;

namespace Microsoft.VisualStudio.Services.Agent.Worker.TestResults.Utils
{
    internal class PublisherInputValidator : InputValidator
    {
        #region Public Methods
        public PublisherInputValidator(IExecutionContext executionContext) : base(executionContext)
        {

        }

        public void CheckTestManagementHttpClient(ITestResultsHttpClient testClient)
        {
            if (testClient == null)
            {
                ExecutionContext.Debug("Passed object of TestManagementHttpClient is null ");
                throw new ArgumentNullException(nameof(testClient));
            }
        }

        public void CheckTestRunId(int testRunId)
        {
            if (testRunId < 0)
            {
                ExecutionContext.Debug("TestRunId passed is invalid (negative).");
                throw new ArgumentOutOfRangeException(nameof(testRunId));
            }
        }

        public void CheckTestRunDataList(IList<TestRunData> testRuns)
        {
            if (!CheckIList(testRuns))
            {
                ExecutionContext.Debug("Either List of TestRunData or its member is null");
                throw new ArgumentNullException(nameof(testRuns));
            }
        }

        public bool ValidateTestLogInput(
            TestLogScope context,
            int buildId,
            int testRunId,
            int testResultId,
            int testSubResultId)
        {
            if (context == TestLogScope.Run)
            {
                if (!ValidateRunInput(testRunId, testResultId, testSubResultId))
                {
                    return false;
                }
            }
            else if (context == TestLogScope.Build)
            {
                // TODO check if we need to allow only codecoverage logs at build level
                if (buildId < 1)
                {
                    return false;
                }
            }
            return true;
        }

        public bool ValidateRunInput(
            int testRunId,
            int testResultId,
            int testSubResultId)
        {
            if (testRunId < 1)
            {
                return false;
            }
            if (testResultId < 0)
            {
                return false;
            }
            if (testSubResultId < 0)
            {
                return false;
            }
            if (testResultId == 0 && testSubResultId > 0)
            {
                return false;
            }
            return true;
        }
        #endregion
    }
}
