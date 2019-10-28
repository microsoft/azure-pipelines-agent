// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
        private string _stageName;
        private int _stageAttempt;
        private string _phaseName;
        private int _phaseAttempt;
        private string _jobName;
        private int _jobAttempt;

        public TestRunContextBuilder(string testRunName)
        {
            _testRunName = testRunName;
        }

        public TestRunContext Build()
        {
            TestRunContext testRunContext = new TestRunContext(owner: string.Empty, platform: string.Empty, configuration: string.Empty, buildId: _buildId, buildUri: _buildUri, releaseUri: null,
                releaseEnvironmentUri: null, runName: _testRunName, testRunSystem: "NoConfigRun", buildAttachmentProcessor: null, targetBranchName: null);

            testRunContext.PipelineReference = new PipelineReference()
            {
                PipelineId = _buildId,
                StageReference = new StageReference() { StageName = _stageName, Attempt = _stageAttempt },
                PhaseReference = new PhaseReference() { PhaseName = _phaseName, Attempt = _phaseAttempt },
                JobReference = new JobReference() { JobName = _jobName, Attempt = _jobAttempt }
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

        public TestRunContextBuilder WithStageName(string stageName)
        {
            _stageName = stageName;
            return this;
        }

        public TestRunContextBuilder WithStageAttempt(int stageAttempt)
        {
            _stageAttempt = stageAttempt;
            return this;
        }

        public TestRunContextBuilder WithPhaseName(string phaseName)
        {
            _phaseName = phaseName;
            return this;
        }

        public TestRunContextBuilder WithPhaseAttempt(int phaseAttempt)
        {
            _phaseAttempt = phaseAttempt;
            return this;
        }

        public TestRunContextBuilder WithJobName(string jobName)
        {
            _jobName = jobName;
            return this;
        }

        public TestRunContextBuilder WithJobAttempt(int jobAttempt)
        {
            _jobAttempt = jobAttempt;
            return this;
        }
    }
}
