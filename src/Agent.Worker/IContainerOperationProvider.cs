// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using System.Threading.Tasks;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    [ServiceLocator(Default = typeof(ContainerOperationProviderEnhanced))]
    public interface IContainerOperationProvider : IAgentService
    {
        Task StartContainersAsync(IExecutionContext executionContext, object data);
        Task StopContainersAsync(IExecutionContext executionContext, object data);
    }
}
