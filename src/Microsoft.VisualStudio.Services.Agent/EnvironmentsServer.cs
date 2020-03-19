// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.Pipelines.Environments.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Microsoft.VisualStudio.Services.Agent
{
    [ServiceLocator(Default = typeof(EnvironmentsServer))]
    public interface IEnvironmentsServer : IAgentService
    {
        Task ConnectAsync(VssConnection agentConnection);

        // Configuration
        Task<List<EnvironmentInstance>> GetEnvironmentsAsync(string projectName, string environmentName);

        // Update Machine Group ( Used for adding tags)
        Task<VirtualMachineResource> UpdateEnvironmentVMsAsync(Guid projectId, int environmentId, VirtualMachineResource virtualMachineResource);

        // Add Deployment Machine
        Task<VirtualMachineResource> AddEnvironmentVMAsync(Guid projectId, int environmentId, VirtualMachineResource virtualMachineResource);

        // Replace Deployment Machine
        Task<VirtualMachineResource> ReplaceEnvironmentVMAsync(Guid projectId, int environmentId, VirtualMachineResource virtualMachineResource);

        // Delete Deployment Machine
        Task DeleteEnvironmentVMAsync(string projectName, int environmentId, int virtualMachineId);

        Task DeleteEnvironmentVMAsync(Guid projectId, int environmentId, int virtualMachineId);

        Task<List<VirtualMachineResource>> GetEnvironmentVMsAsync(string projectName, int environmentId, string virtualMachine);

        Task<List<VirtualMachineResource>> GetEnvironmentVMsAsync(Guid projectGuid, int environmentId, string virtualMachine);

        Task<TaskAgentPoolReference> GetEnvironmentPoolAsync(Guid projectGuid, int environmentId);
    }

    public sealed class EnvironmentsServer : AgentService, IEnvironmentsServer
    {
        private bool _hasConnection;
        private VssConnection _connection;
        private EnvironmentsHttpClient _environmentsHttpClient;

        public async Task ConnectAsync(VssConnection agentConnection)
        {
            ArgUtil.NotNull(agentConnection, nameof(agentConnection));
            _connection = agentConnection;
            if (!_connection.HasAuthenticated)
            {
                await _connection.ConnectAsync();
            }

            _environmentsHttpClient = _connection.GetClient<EnvironmentsHttpClient>();
            _hasConnection = true;
        }

        public Task<List<EnvironmentInstance>> GetEnvironmentsAsync(string projectName, string environmentName)
        {
            CheckConnection();
            return _environmentsHttpClient.GetEnvironmentsAsync(projectName, environmentName);
        }

        public Task<VirtualMachineResource> UpdateEnvironmentVMsAsync(Guid projectId, int environmentId, VirtualMachineResource virtualMachineResource)
        {
            CheckConnection();
            return _environmentsHttpClient.UpdateVirtualMachineResourceAsync(projectId, environmentId, virtualMachineResource);
        }

        public Task<VirtualMachineResource> AddEnvironmentVMAsync(Guid projectId, int environmentId, VirtualMachineResource virtualMachineResource)
        {
            CheckConnection();
            var virtualMachineResourceCreateParameters = new VirtualMachineResourceCreateParameters();
            virtualMachineResourceCreateParameters.virtualMachineResource = virtualMachineResource;
            return _environmentsHttpClient.AddVirtualMachineResourceAsync(projectId.ToString(), environmentId, virtualMachineResourceCreateParameters);
        }

        public Task<VirtualMachineResource> ReplaceEnvironmentVMAsync(Guid projectId, int environmentId, VirtualMachineResource virtualMachineResource)
        {
            CheckConnection();
            return _environmentsHttpClient.ReplaceVirtualMachineResourceAsync(projectId, environmentId, virtualMachineResource);
        }

        public Task DeleteEnvironmentVMAsync(string projectName, int environmentId, int virtualMachineId)
        {
            CheckConnection();
            return _environmentsHttpClient.DeleteVirtualMachineResourceAsync(projectName, environmentId, virtualMachineId);
        }

        public Task DeleteEnvironmentVMAsync(Guid projectId, int environmentId, int virtualMachineId)
        {
            CheckConnection();
            return _environmentsHttpClient.DeleteVirtualMachineResourceAsync(projectId, environmentId, virtualMachineId);
        }

        public Task<List<VirtualMachineResource>> GetEnvironmentVMsAsync(string projectName, int environmentId, string virtualMachine)
        {
            CheckConnection();
            return _environmentsHttpClient.GetVirtualMachineResourcesAsync(projectName, environmentId, virtualMachine);
        }

        public Task<List<VirtualMachineResource>> GetEnvironmentVMsAsync(Guid projectGuid, int environmentId, string virtualMachine)
        {
            CheckConnection();
            return _environmentsHttpClient.GetVirtualMachineResourcesAsync(projectGuid, environmentId, virtualMachine);
        }

        public Task<TaskAgentPoolReference> GetEnvironmentPoolAsync(Guid projectGuid, int environmentId)
        {
            CheckConnection();
            return _environmentsHttpClient.GetLinkedPoolAsync(projectGuid, environmentId);
        }


        private void CheckConnection()
        {
            if (!_hasConnection)
            {
                throw new InvalidOperationException("SetConnection");
            }
        }
    }
}