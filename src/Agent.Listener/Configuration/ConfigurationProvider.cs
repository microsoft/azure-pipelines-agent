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
        string ConfigurationProviderType { get; }

        string GetServerUrl(CommandSettings command);

        Task TestConnectionAsync(string tfsUrl, VssCredentials creds);

        Task<AgentConfigSettings> GetAgentConfigSettings(CommandSettings command);

        string GetFailedToFindPoolErrorString();

        Task<TaskAgent> UpdateAgentAsync(AgentConfigSettings agentConfigSettings, TaskAgent agent, CommandSettings command);

        Task<TaskAgent> AddAgentAsync(AgentConfigSettings agentConfigSettings, TaskAgent agent, CommandSettings command);

        Task DeleteAgentAsync(AgentConfigSettings agentConfigSettings, AgentSettings settings, string currentAction);

        void UpdateAgentSetting(AgentConfigSettings agentConfigSettings, AgentSettings settings);

        Task<TaskAgent> GetAgentAsync(AgentConfigSettings agentConfigSettings, string agentName);

        AgentConfigSettings ReadSettingsAndGetAgentConfigSettings(AgentSettings settings);

        string GetAgentWithSameNameAlreadyExistErrorString(AgentConfigSettings agentConfigSettings, string agentName);
    }

    public sealed class BuildReleasesAgentConfigProvider : AgentService, IConfigurationProvider
    {
        public Type ExtensionType => typeof(IConfigurationProvider);
        private ITerminal _term;
        private IAgentServer _agentServer;

        public string ConfigurationProviderType
            => Constants.Agent.AgentConfigurationProvider.BuildReleasesAgentConfiguration;

        public override void Initialize(IHostContext hostContext)
        {
            base.Initialize(hostContext);
            _term = hostContext.GetService<ITerminal>();
            _agentServer = HostContext.GetService<IAgentServer>();
        }

        public AgentConfigSettings ReadSettingsAndGetAgentConfigSettings(AgentSettings settings)
        {
            return new AgentConfigSettings(settings.PoolId);
        }

        public void UpdateAgentSetting(AgentConfigSettings agentConfigSettings, AgentSettings settings)
        {
        }

        public string GetServerUrl(CommandSettings command)
        {
            return command.GetUrl();
        }

        public async Task<AgentConfigSettings> GetAgentConfigSettings(CommandSettings command)
        {
            int poolId = 0;
            string poolName;

            poolName = command.GetPool();
            poolId = await GetPoolIdAsync(poolName);
            Trace.Info($"PoolId for agent pool '{poolName}' is '{poolId}'.");

            return new AgentConfigSettings(poolId);
        }

        public string GetFailedToFindPoolErrorString() => StringUtil.Loc("FailedToFindPool");

        public string GetAgentWithSameNameAlreadyExistErrorString(AgentConfigSettings agentConfigSettings, string agentName)
        {
            return StringUtil.Loc("AgentWithSameNameAlreadyExistInPool", agentConfigSettings.PoolId, agentName);
        }

        public Task<TaskAgent> UpdateAgentAsync(AgentConfigSettings agentConfigSettings, TaskAgent agent, CommandSettings command)
        {
            return _agentServer.UpdateAgentAsync(agentConfigSettings.PoolId, agent);
        }

        public Task<TaskAgent> AddAgentAsync(AgentConfigSettings agentConfigSettings, TaskAgent agent, CommandSettings command)
        { 
            return _agentServer.AddAgentAsync(agentConfigSettings.PoolId, agent);
        }

        public async Task DeleteAgentAsync(AgentConfigSettings agentConfigSettings, AgentSettings settings, string currentAction)
        {
            TaskAgent agent = await GetAgentAsync(agentConfigSettings, settings.AgentName);
            if (agent == null)
            {
                _term.WriteLine(StringUtil.Loc("Skipping") + currentAction);
            }
            else
            {
                await _agentServer.DeleteAgentAsync(agentConfigSettings.PoolId, settings.AgentId);
                _term.WriteLine(StringUtil.Loc("Success") + currentAction);
            }
        }

        public async Task TestConnectionAsync(string url, VssCredentials creds)
        {
            _term.WriteLine(StringUtil.Loc("ConnectingToServer"));
            VssConnection connection = ApiUtil.CreateConnection(new Uri(url), creds);

            await _agentServer.ConnectAsync(connection);
        }

        public async Task<TaskAgent> GetAgentAsync(AgentConfigSettings agentConfigSettings, string agentName)
        {
            var agents = await _agentServer.GetAgentsAsync(agentConfigSettings.PoolId, agentName);
            Trace.Verbose("Returns {0} agents", agents.Count);
            return agents.FirstOrDefault();
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

    public sealed class DeploymentGroupAgentConfigProvider : AgentService, IConfigurationProvider
    {
        public Type ExtensionType => typeof(IConfigurationProvider);
        private ITerminal _term;
        private string _projectName = string.Empty;
        private string _collectionName;
        
        private string _serverUrl;
        private bool _isHosted = false;
        private IDeploymentGroupServer _deploymentGroupServer = null;

        public string ConfigurationProviderType
            => Constants.Agent.AgentConfigurationProvider.DeploymentAgentConfiguration;

        public override void Initialize(IHostContext hostContext)
        {
            base.Initialize(hostContext);
            _term = hostContext.GetService<ITerminal>();
            _deploymentGroupServer = HostContext.GetService<IDeploymentGroupServer>();
        }

        public AgentConfigSettings ReadSettingsAndGetAgentConfigSettings(AgentSettings settings)
        {
            _collectionName = settings.CollectionName;
            
            // project name back compat
            var projectId = settings.ProjectId;
            if(string.IsNullOrWhiteSpace(projectId))
            {
                projectId = settings.ProjectName;
            }
            return new DeploymentAgentConfigSettings(settings.PoolId, settings.DeploymentGroupId, projectId);
        }

        public string GetServerUrl(CommandSettings command)
        {
            _serverUrl =  command.GetUrl();
            Trace.Info("url - {0}", _serverUrl);

            _isHosted = UrlUtil.IsHosted(_serverUrl);

            // for onprem tfs, collection is required for deploymentGroup
            if (! _isHosted)
            {
                Trace.Info("Provided url is for onprem tfs, need collection name");
                _collectionName = command.GetCollectionName();
            }

            return _serverUrl;
        }

        public async Task<AgentConfigSettings> GetAgentConfigSettings(CommandSettings command)
        {
            _projectName = command.GetProjectName(_projectName);
            var deploymentGroupName = command.GetDeploymentGroupName();

            var deploymentGroup =  await GetDeploymentGroupAsync(_projectName, deploymentGroupName);
            Trace.Info($"PoolId for deployment group '{deploymentGroupName}' is '{deploymentGroup.Pool.Id}'.");
            Trace.Info($"Project id for deployment group '{deploymentGroupName}' is '{deploymentGroup.Project.Id.ToString()}'.");

            return new DeploymentAgentConfigSettings(deploymentGroup.Pool.Id, deploymentGroup.Id, deploymentGroup.Project.Id.ToString());
        }

        public string GetFailedToFindPoolErrorString() => StringUtil.Loc("FailedToFindDeploymentGroup");

        public string GetAgentWithSameNameAlreadyExistErrorString(AgentConfigSettings agentConfigSettings, string agentName)
        {
            return StringUtil.Loc("DeploymentMachineWithSameNameAlreadyExistInDeploymentGroup", ((DeploymentAgentConfigSettings)agentConfigSettings).DeploymentGroupId, agentName);
        }

        public async Task<TaskAgent> UpdateAgentAsync(AgentConfigSettings agentConfigSettings, TaskAgent agent, CommandSettings command)
        {
            var deploymentMachine = new DeploymentMachine() { Agent = agent };
            deploymentMachine = await _deploymentGroupServer.ReplaceDeploymentMachineAsync(new Guid(((DeploymentAgentConfigSettings)agentConfigSettings).ProjectId), ((DeploymentAgentConfigSettings)agentConfigSettings).DeploymentGroupId, agent.Id, deploymentMachine);

            await GetAndAddTags(agentConfigSettings, agent, deploymentMachine.Id, command);
            return deploymentMachine.Agent;
        }

        public async Task<TaskAgent> AddAgentAsync(AgentConfigSettings agentConfigSettings, TaskAgent agent, CommandSettings command)
        {
            var deploymentMachine = new DeploymentMachine(){ Agent = agent };
            deploymentMachine = await _deploymentGroupServer.AddDeploymentMachineAsync(new Guid(((DeploymentAgentConfigSettings)agentConfigSettings).ProjectId), ((DeploymentAgentConfigSettings)agentConfigSettings).DeploymentGroupId, deploymentMachine);
            
            await GetAndAddTags(agentConfigSettings, deploymentMachine.Agent, deploymentMachine.Id, command);

            return deploymentMachine.Agent;
        }

        public async Task DeleteAgentAsync(AgentConfigSettings agentConfigSettings, AgentSettings settings, string currentAction)
        {
            var machines = await GetDeploymentMachinesAsync(agentConfigSettings, settings.AgentName);
            Trace.Verbose("Returns {0} machines with name {1}", machines.Count, settings.AgentName);
            var machine = machines.FirstOrDefault();
            if (machine == null)
            {
                _term.WriteLine(StringUtil.Loc("Skipping") + currentAction);
            }
            else
            {
                Guid projectGuid;
                var isProjectIdAvailable = Guid.TryParseExact(((DeploymentAgentConfigSettings)agentConfigSettings).ProjectId, "D", out projectGuid);
                if (isProjectIdAvailable)
                {
                    await _deploymentGroupServer.DeleteDeploymentMachineAsync(projectGuid, ((DeploymentAgentConfigSettings)agentConfigSettings).DeploymentGroupId, machine.Id);
                }
                else
                {
                    await _deploymentGroupServer.DeleteDeploymentMachineAsync(((DeploymentAgentConfigSettings)agentConfigSettings).ProjectId, ((DeploymentAgentConfigSettings)agentConfigSettings).DeploymentGroupId, machine.Id);
                }
                _term.WriteLine(StringUtil.Loc("Success") + currentAction);
            }
        }

        public async Task TestConnectionAsync(string url, VssCredentials creds)
        {
            _term.WriteLine(StringUtil.Loc("ConnectingToServer"));
            VssConnection connection = ApiUtil.CreateConnection(new Uri(url), creds);

            // Create the connection for deployment group 
            Trace.Info("Test connection with deployment group");
            if (!_isHosted && !_collectionName.IsNullOrEmpty()) // For on-prm validate the collection by making the connection
            {
                UriBuilder uriBuilder = new UriBuilder(new Uri(url));
                uriBuilder.Path = uriBuilder.Path + "/" + _collectionName;
                Trace.Info("Tfs Collection level url to connect - {0}", uriBuilder.Uri.AbsoluteUri);
                url = uriBuilder.Uri.AbsoluteUri;
            }
            VssConnection deploymentGroupconnection = ApiUtil.CreateConnection(new Uri(url), creds);

            await _deploymentGroupServer.ConnectAsync(deploymentGroupconnection);
            Trace.Info("Connect complete for deployment group");
        }

        public void UpdateAgentSetting(AgentConfigSettings agentConfigSettings, AgentSettings settings)
        {
            settings.DeploymentGroupId = ((DeploymentAgentConfigSettings)agentConfigSettings).DeploymentGroupId;
            settings.ProjectId = ((DeploymentAgentConfigSettings)agentConfigSettings).ProjectId;
            settings.CollectionName = _collectionName;
        }

        public async Task<TaskAgent> GetAgentAsync(AgentConfigSettings agentConfigSettings, string agentName)
        {
            var machines = await GetDeploymentMachinesAsync(agentConfigSettings, agentName);
            Trace.Verbose("Returns {0} machines", machines.Count);
            var machine = machines.FirstOrDefault();
            if (machine != null)
            {
                return machine.Agent;
            }

            return null;
        }

        private async Task GetAndAddTags(AgentConfigSettings agentConfigSettings, TaskAgent agent, int machineId, CommandSettings command)
        {
            // Get and apply Tags in case agent is configured against Deployment Group
            bool needToAddTags = command.GetDeploymentGroupTagsRequired();
            while (needToAddTags)
            {
                try
                {
                    string tagString = command.GetDeploymentGroupTags();
                    Trace.Info("Given tags - {0} will be processed and added", tagString);

                    if (!string.IsNullOrWhiteSpace(tagString))
                    {
                        var tagsList =
                            tagString.Split(',').Where(s => !string.IsNullOrWhiteSpace(s))
                                .Select(s => s.Trim())
                                .Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();

                        if (tagsList.Any())
                        {
                            Trace.Info("Adding tags - {0}", string.Join(",", tagsList.ToArray()));

                            var deploymentMachine = new DeploymentMachine()
                            {
                                Agent = agent,
                                Tags = tagsList
                            };

                            await _deploymentGroupServer.UpdateDeploymentMachineAsync(new Guid(((DeploymentAgentConfigSettings)agentConfigSettings).ProjectId), ((DeploymentAgentConfigSettings)agentConfigSettings).DeploymentGroupId, machineId, deploymentMachine);

                            _term.WriteLine(StringUtil.Loc("DeploymentGroupTagsAddedMsg"));
                        }
                    }
                    break;
                }
                catch (Exception e) when (!command.Unattended)
                {
                    _term.WriteError(e);
                    _term.WriteError(StringUtil.Loc("FailedToAddTags"));
                }
            }
        }

        private async Task<DeploymentGroup> GetDeploymentGroupAsync(string projectName, string deploymentGroupName)
        {
            ArgUtil.NotNull(_deploymentGroupServer, nameof(_deploymentGroupServer));

            var deploymentGroup = (await _deploymentGroupServer.GetDeploymentGroupsAsync(projectName, deploymentGroupName)).FirstOrDefault();

            if (deploymentGroup == null)
            {
                throw new DeploymentGroupNotFoundException(StringUtil.Loc("DeploymentGroupNotFound", deploymentGroupName));
            }

            Trace.Info("Found deployment group {0} with id {1}", deploymentGroupName, deploymentGroup.Id);
            return deploymentGroup;
        }

        private async Task<List<DeploymentMachine>> GetDeploymentMachinesAsync(AgentConfigSettings agentConfigSettings, string agentName)
        {
            Guid projectGuid;
            List<DeploymentMachine> machines;
            var isProjectIdAvailable = Guid.TryParseExact(((DeploymentAgentConfigSettings)agentConfigSettings).ProjectId, "D", out projectGuid);
            if (isProjectIdAvailable)
            {
                machines = await _deploymentGroupServer.GetDeploymentMachinesAsync(projectGuid, ((DeploymentAgentConfigSettings)agentConfigSettings).DeploymentGroupId, agentName);
            }
            else
            {
                machines = await _deploymentGroupServer.GetDeploymentMachinesAsync(((DeploymentAgentConfigSettings)agentConfigSettings).ProjectId, ((DeploymentAgentConfigSettings)agentConfigSettings).DeploymentGroupId, agentName);
            }

            return machines;
        }
    }
}
