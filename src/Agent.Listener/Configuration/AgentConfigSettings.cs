
namespace Microsoft.VisualStudio.Services.Agent.Listener.Configuration
{

    public class AgentConfigSettings
    {
        public int PoolId;

        public AgentConfigSettings(int poolId)
        {
            this.PoolId = poolId;
        }
    }

    public class DeploymentAgentConfigSettings : AgentConfigSettings
    {
        public int DeploymentGroupId;

        public string ProjectId;

        public DeploymentAgentConfigSettings(int poolId, int deploymentGroupID, string projectId) : base(poolId)
        {
            this.DeploymentGroupId = deploymentGroupID;
            this.ProjectId = projectId;
        }
    }
}
