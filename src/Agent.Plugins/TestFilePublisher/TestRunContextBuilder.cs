using Microsoft.TeamFoundation.TestClient.PublishTestResults;
using Microsoft.TeamFoundation.TestManagement.WebApi;

namespace Agent.Plugins.Log.TestFilePublisher
{
    public interface ITestRunContextBuilder
    {
        TestRunContextBuilder WithBuildId(int buildId);
        TestRunContextBuilder WithBuildUri(string buildUri);
    }

    public class TestRunContextBuilder : ITestRunContextBuilder
    {
        private int _buildId;
        private string _buildUri;
        private readonly string _testRunName;

        public TestRunContextBuilder(string testRunName)
        {
            _testRunName = testRunName;
        }

        public TestRunContext Build(PipelineConfig pipelineConfig)
        {
            TestRunContext testRunContext = new TestRunContext(owner: string.Empty, platform: string.Empty, configuration: string.Empty, buildId: _buildId, buildUri: _buildUri, releaseUri: null,
                releaseEnvironmentUri: null, runName: _testRunName, testRunSystem: "NoConfigRun", buildAttachmentProcessor: null, targetBranchName: null);
            testRunContext.PipelineReference = new PipelineReference()
            {
                PipelineId = pipelineConfig.BuildId,
                StageReference = new StageReference() { StageName = pipelineConfig.StageName, Attempt = pipelineConfig.StageAttempt },
                PhaseReference = new PhaseReference() { PhaseName = pipelineConfig.PhaseName, Attempt = pipelineConfig.PhaseAttempt },
                JobReference = new JobReference() { JobName = pipelineConfig.JobName, Attempt = pipelineConfig.JobAttempt }
            };
            return testRunContext;
        }

        public TestRunContextBuilder WithBuildId(int buildId)
        {
            _buildId = buildId;
            return this;
        }

        public TestRunContextBuilder WithBuildUri(string buildUri)
        {
            _buildUri = buildUri;
            return this;
        }
    }
}
