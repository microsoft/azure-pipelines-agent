using Microsoft.TeamFoundation.TestClient.PublishTestResults;

namespace Agent.Plugins.TestFilePublisher
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

        public TestRunContext Build()
        {
            return new TestRunContext(owner: "NoConfigRun", platform: string.Empty, configuration: string.Empty, buildId: _buildId, buildUri: _buildUri, releaseUri: null,
                releaseEnvironmentUri: null, runName: _testRunName, testRunSystem: "NoConfigRun", buildAttachmentProcessor: null, targetBranchName: null);
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
