using Microsoft.TeamFoundation.TestClient.PublishTestResults;
using Microsoft.TeamFoundation.TestManagement.WebApi;
using Microsoft.VisualStudio.Services.WebApi;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.VisualStudio.Services.Agent.Worker.TestResults
{
    [ServiceLocator(Default = typeof(TestRunDataPublisher))]
    public interface ITestRunDataPublisher : IAgentService
    {
        void InitializePublisher(IExecutionContext executionContext, string projectName, VssConnection connection);

        Task<IList<TestRun>> PublishAsync(TestRunContext runContext, List<TestRunData> testRunData, PublishOptions publishOptions, CancellationToken cancellationToken = default(CancellationToken));
    }

    public class TestRunDataPublisher : AgentService, ITestRunDataPublisher
    {
        private IExecutionContext _executionContext;
        private string _projectName;
        private ITestRunPublisher _testRunPublisher;

        public void InitializePublisher(IExecutionContext context, string projectName, VssConnection connection)
        {
            Trace.Entering();
            _executionContext = context;
            _projectName = projectName;
            _testRunPublisher = new TestRunPublisher(connection, new CommandTraceListener(context));
            Trace.Leaving();
        }

        public async Task<IList<TestRun>> PublishAsync(TestRunContext runContext, List<TestRunData> testRunData, PublishOptions publishOptions, CancellationToken cancellationToken = default(CancellationToken))
        {
            IList<TestRun> publishedTestResults = null;
            try
            {
                publishedTestResults = await _testRunPublisher.PublishTestRunDataAsync(runContext, _projectName, testRunData, publishOptions, cancellationToken);
            }
            catch (Exception ex)
            {
                _executionContext.Warning("Failed to publish test run data: "+ ex.ToString());
            }
            return publishedTestResults;
        }
    }
}
