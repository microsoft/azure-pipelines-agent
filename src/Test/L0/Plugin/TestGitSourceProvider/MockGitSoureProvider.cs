using Agent.Plugins.Repository;
using System.Collections.Generic;
using System.Threading;
using Agent.Sdk;
using Microsoft.TeamFoundation.DistributedTask.Pipelines;
using Microsoft.TeamFoundation.DistributedTask.WebApi;

namespace Test.L0.Plugin.TestGitSourceProvider
{
    public class MockGitSoureProvider : GitSourceProvider
    {
        protected override GitCliManager GetCliManager(Dictionary<string, string> gitEnv = null)
        {
            return new MockGitCliManager();
        }

        protected override string GetWISCToken(ServiceEndpoint endpoint, AgentTaskPluginExecutionContext executionContext, CancellationToken cancellationToken)
        {
            return "WSICToken";
        }
        public override bool GitLfsSupportUseAuthHeader(AgentTaskPluginExecutionContext executionContext, GitCliManager gitCommandManager)
        {
            return false;
        }

        public override bool GitSupportsConfigEnv(AgentTaskPluginExecutionContext executionContext, GitCliManager gitCommandManager)
        {
            return false;
        }

        public override bool GitSupportsFetchingCommitBySha1Hash(GitCliManager gitCommandManager)
        {
            return false;
        }

        public override bool GitSupportUseAuthHeader(AgentTaskPluginExecutionContext executionContext, GitCliManager gitCommandManager)
        {
            return false;
        }

        public override void RequirementCheck(AgentTaskPluginExecutionContext executionContext, RepositoryResource repository, GitCliManager gitCommandManager){ }
    }
}
