using Microsoft.TeamFoundation.TestClient.PublishTestResults;
using Microsoft.TeamFoundation.TestManagement.WebApi;
using Microsoft.VisualStudio.Services.WebApi;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.VisualStudio.Services.Agent.Worker.TestResults
{
    [ServiceLocator(Default = typeof(TestRunDataPublisher))]
    public interface ITestRunDataPublisher : IAgentService
    {
        void InitializePublisher(IExecutionContext executionContext, string projectName, VssConnection connection, string testRunner);

        Task<bool> PublishAsync(TestRunContext runContext, List<string> testResultFiles, PublishOptions publishOptions, CancellationToken cancellationToken = default(CancellationToken));
    }

    public class TestRunDataPublisher : AgentService, ITestRunDataPublisher
    {
        private IExecutionContext _executionContext;
        private string _projectName;
        private ITestRunPublisher _testRunPublisher;
        private IParser _parser;

        public void InitializePublisher(IExecutionContext context, string projectName, VssConnection connection, string testRunner)
        {
            Trace.Entering();
            _executionContext = context;
            _projectName = projectName;
            _testRunPublisher = new TestRunPublisher(connection, new CommandTraceListener(context));

            var extensionManager = HostContext.GetService<IExtensionManager>();
            _parser = (extensionManager.GetExtensions<IParser>()).FirstOrDefault(x => testRunner.Equals(x.Name, StringComparison.OrdinalIgnoreCase));
            Trace.Leaving();
        }

        public async Task<bool> PublishAsync(TestRunContext runContext, List<string> testResultFiles, PublishOptions publishOptions, CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                TestDataProvider testDataProvider = ParseTestResultsFile(runContext, testResultFiles);
                var testRunData = testDataProvider.GetTestRunData();
                await _testRunPublisher.PublishTestRunDataAsync(runContext, _projectName, testRunData, publishOptions, cancellationToken);
                return GetTestRunOutcome(testRunData);
            }
            catch (Exception ex)
            {
                _executionContext.Warning("Failed to publish test run data: "+ ex.ToString());
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

        private bool GetTestRunOutcome(IList<TestRunData> testRunDataList)
        {
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

            return false;
        }
    }
}
