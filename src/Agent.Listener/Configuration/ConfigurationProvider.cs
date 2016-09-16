using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.Common;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

namespace Microsoft.VisualStudio.Services.Agent.Listener.Configuration
{
    public interface IConfigurationProvider : IExtension, IAgentService
    {
        void InitializeServerConnection();

        string ConfigurationProviderType { get; }

        string GetServerUrl(CommandSettings command);

        Task<IAgentServer> TestConnectAsync(string tfsUrl, VssCredentials creds);

        Task<int> GetPoolId(CommandSettings command);

        Task<TaskAgent> UpdateAgentAsync(int poolId, TaskAgent agent);

        Task<TaskAgent> AddAgentAsync(int poolId, TaskAgent agent);

        Task DeleteAgentAsync(int agentPoolId, int agentId);

        void UpdateAgentSetting(AgentSettings settings);
    }

    public abstract class ConfigurationProvider : AgentService
    {
        public Type ExtensionType => typeof(IConfigurationProvider);
        protected ITerminal _term;
        protected IAgentServer _agentServer;

        public override void Initialize(IHostContext hostContext)
        {
            base.Initialize(hostContext);
            _term = hostContext.GetService<ITerminal>();
        }

        public virtual void InitializeServerConnection()
        {
            _agentServer = HostContext.GetService<IAgentServer>();
        }
        
        protected Task<TaskAgent> UpdateAgent(int poolId, TaskAgent agent)
        {
           return _agentServer.UpdateAgentAsync(poolId, agent);
        }

        protected Task<TaskAgent> AddAgent(int poolId, TaskAgent agent)
        {
            return _agentServer.AddAgentAsync(poolId, agent);
        }

        protected Task DeleteAgent(int poolId, int agentId)
        {
            return _agentServer.DeleteAgentAsync(poolId, agentId);
        }

        protected async Task TestConnectionAsync(string url, VssCredentials creds)
        {
            _term.WriteLine(StringUtil.Loc("ConnectingToServer"));
            VssConnection connection = ApiUtil.CreateConnection(new Uri(url), creds);

            _agentServer = HostContext.CreateService<IAgentServer>();
            await _agentServer.ConnectAsync(connection);
        }
    }

    public sealed class BuildReleasesAgentConfigProvider : ConfigurationProvider, IConfigurationProvider
    {
        public string ConfigurationProviderType
            => Constants.Agent.AgentConfigurationProvider.BuildReleasesAgentConfiguration;

        public void UpdateAgentSetting(AgentSettings settings)
        {
            // No implementation required
        }

        public string GetServerUrl(CommandSettings command)
        {
            return command.GetUrl(false);
        }

        public async Task<int> GetPoolId(CommandSettings command)
        {
            int poolId = 0;
            string poolName;
            while (true)
            {
                poolName = command.GetPool();
                try
                {
                    poolId = await GetPoolIdAsync(poolName);
                    Trace.Info($"PoolId for agent pool '{poolName}' is '{poolId}'.");
                    break;
                }
                catch (Exception e) when (!command.Unattended)
                {
                    _term.WriteError(e);
                    _term.WriteError(StringUtil.Loc("FailedToFindPool"));
                }
            }

            return poolId;
        }

        public Task<TaskAgent> UpdateAgentAsync(int poolId, TaskAgent agent)
        {
            return UpdateAgent(poolId, agent);
        }

        public Task<TaskAgent> AddAgentAsync(int poolId, TaskAgent agent)
        {
            return AddAgent(poolId, agent);
        }

        public Task DeleteAgentAsync(int agentPoolId, int agentId)
        {
            return DeleteAgent(agentPoolId,agentId);
        }

        public async Task<IAgentServer> TestConnectAsync(string url, VssCredentials creds)
        {
            await TestConnectionAsync(url, creds);
            return _agentServer;
        }

        private async Task<int> GetPoolIdAsync(string poolName)
        {
            TaskAgentPool agentPool = (await _agentServer.GetAgentPoolsAsync(poolName)).FirstOrDefault();
            if (agentPool == null)
            {
                throw new TaskAgentPoolNotFoundException(StringUtil.Loc("PoolNotFound", poolName));
            }
            else
            {
                Trace.Info("Found pool {0} with id {1}", poolName, agentPool.Id);
                return agentPool.Id;
            }
        }

    }

    public sealed class MachineGroupAgentConfigProvider : ConfigurationProvider, IConfigurationProvider
    {
        private string _projectName;
        private string _collectionName;
        private string _machineGroupName;
        private string _serverUrl;
        private bool _isHosted = false;
        private IMachineGroupServer _machineGroupServer = null;

        public string ConfigurationProviderType
            => Constants.Agent.AgentConfigurationProvider.DeploymentAgentConfiguration;

        public string GetServerUrl(CommandSettings command)
        {
            _serverUrl =  command.GetUrl(true);
            Trace.Info("url - {0}", _serverUrl);

            string baseUrl = _serverUrl;
            _isHosted = UrlUtil.IsHosted(_serverUrl);

            // VSTS account url - Do validation of server Url includes project name 
            // On-prem tfs Url - Do validation of tfs Url includes collection and project name 

            Uri uri = new Uri(_serverUrl);                                   //e.g On-prem => http://myonpremtfs:8080/tfs/defaultcollection/myproject
                                                                             //e.g VSTS => https://myvstsaccount.visualstudio.com/myproject

            string urlAbsolutePath = uri.AbsolutePath;                       //e.g tfs/defaultcollection/myproject
                                                                             //e.g myproject
            string[] urlTokenParts = urlAbsolutePath.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);      //e.g tfs,defaultcollection,myproject
            int tokenCount = urlTokenParts.Length;

            if (tokenCount == 0)
            {
                if (! _isHosted)
                {
                    ThrowExceptionForOnPremUrl();
                }
                else
                {
                    ThrowExceptionForVSTSUrl();
                }
            }
            
            // for onprem ensure collection/project is format
            if (! _isHosted)
            {
                Trace.Info("Provided url is for onprem tfs");
                
                if (tokenCount <= 1)
                {
                    ThrowExceptionForOnPremUrl();
                }
                _collectionName = urlTokenParts[tokenCount-2];
                _projectName = urlTokenParts[tokenCount-1];
                Trace.Info("collectionName - {0}", _collectionName);

                baseUrl = _serverUrl.Replace(_projectName, "").Replace(_collectionName, "").TrimEnd(new char[] { '/'});
            }
            else
            {
                Trace.Info("Provided url is for vsts account");
                _projectName = urlTokenParts.Last();

                baseUrl = new Uri(_serverUrl).GetLeftPart(UriPartial.Authority);
            }

            Trace.Info("projectName - {0}", _projectName);

            return baseUrl;
        }

        public async Task<IAgentServer> TestConnectAsync(string url, VssCredentials creds)
        {
            await TestMachineGroupConnection(url, creds);

            await TestConnectionAsync(url, creds);

            return _agentServer;
        }

        public async Task<int> GetPoolId(CommandSettings command)
        {
            int poolId;
            while (true)
            {
                _machineGroupName = command.GetMachineGroupName();
                try
                {
                    poolId =  await GetPoolIdAsync(_projectName, _machineGroupName);
                    Trace.Info($"PoolId for machine group '{_machineGroupName}' is '{poolId}'.");
                    break;
                }
                catch (Exception e) when (!command.Unattended)
                {
                    _term.WriteError(e);
                }

                _term.WriteError(StringUtil.Loc("FailedToFindMachineGroup"));

                // In case of failure ensure to get the project name again
                _projectName = command.GetProjectName(_projectName);
            }
            
            return poolId;
        }

        public Task<TaskAgent> UpdateAgentAsync(int poolId, TaskAgent agent)
        {
            return UpdateAgent(poolId, agent);
            // this may have additional calls related to Machine Group
        }

        public Task<TaskAgent> AddAgentAsync(int poolId, TaskAgent agent)
        {
            return AddAgent(poolId, agent);
            // this may have additional calls related to Machine Group
        }

        public Task DeleteAgentAsync(int agentPoolId, int agentId)
        {
            return DeleteAgent(agentPoolId, agentId);
        }

        public void UpdateAgentSetting(AgentSettings settings)
        {
            settings.MachineGroupName = _machineGroupName;
            settings.ProjectName = _projectName;
        }

        private async Task<int> GetPoolIdAsync(string projectName, string machineGroupName)
        {
            int poolId = 0;

            ArgUtil.NotNull(_machineGroupServer, nameof(_machineGroupServer));

            DeploymentMachineGroup machineGroup = (await _machineGroupServer.GetDeploymentMachineGroupsAsync(projectName, machineGroupName)).FirstOrDefault();

            if (machineGroup == null)
            {
                throw new DeploymentMachineGroupNotFoundException(StringUtil.Loc("MachineGroupNotFound", machineGroupName));
            }

            int machineGroupId = machineGroup.Id;
            Trace.Info("Found machine group {0} with id {1}", machineGroupName, machineGroupId);
            poolId = machineGroup.Pool.Id;
            Trace.Info("Found poolId {0} for machine group {1}", poolId, machineGroupName);

            return poolId;
        }

        private async Task TestCollectionConnectionAsync(string url, VssCredentials creds)
        {
            VssConnection connection = ApiUtil.CreateConnection(new Uri(url), creds);

            _machineGroupServer = HostContext.CreateService<IMachineGroupServer>();
            await _machineGroupServer.ConnectAsync(connection);
        }

        private async Task TestMachineGroupConnection(string tfsUrl, VssCredentials creds)
        {
            Trace.Info("Test connection with machine group");
            var url = tfsUrl;

            if (!_isHosted && !_collectionName.IsNullOrEmpty()) // For on-prm validate the collection by making the connection
            {
                UriBuilder uriBuilder = new UriBuilder(new Uri(tfsUrl));
                uriBuilder.Path = uriBuilder.Path + "/" + _collectionName;
                Trace.Info("Tfs Collection level url to connect - {0}", uriBuilder.Uri.AbsoluteUri);
                url = uriBuilder.Uri.AbsoluteUri;
            }

            // Validate can connect.
            await TestCollectionConnectionAsync(url, creds);
            Trace.Info("Connect complete for machine group");
        }

        private void ThrowExceptionForOnPremUrl()
        {
            throw new Exception(StringUtil.Loc("UrlValidationFailedForOnPremTfs"));
        }

        private void ThrowExceptionForVSTSUrl()
        {
            throw new Exception(StringUtil.Loc("UrlValidationFailedForVSTSAccount"));
        }

    }

}
