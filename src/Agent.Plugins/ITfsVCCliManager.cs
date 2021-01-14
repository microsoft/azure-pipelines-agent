// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.DistributedTask.WebApi;
using System.Threading;
using System.Threading.Tasks;
using Agent.Sdk;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;

namespace Agent.Plugins.Repository
{
    public interface ITfsVCCliManager
    {
        CancellationToken CancellationToken { set; }
        Pipelines.RepositoryResource Repository { set; }
        AgentTaskPluginExecutionContext ExecutionContext { set; }
        ServiceEndpoint Endpoint { set; }
        TfsVCFeatures Features { get; }
        string FilePath { get; }
        void SetupProxy(string proxyUrl, string proxyUsername, string proxyPassword);
        void SetupClientCertificate(string clientCert, string clientCertKey, string clientCertArchive, string clientCertPassword);
        bool TestEulaAccepted();
        Task EulaAsync();
        Task<ITfsVCWorkspace[]> WorkspacesAsync(bool matchWorkspaceNameOnAnyComputer = false);
        Task<ITfsVCStatus> StatusAsync(string localPath);
        Task UndoAsync(string localPath);
        Task ScorchAsync();
        Task<bool> TryWorkspaceDeleteAsync(ITfsVCWorkspace workspace);
        Task WorkspaceDeleteAsync(ITfsVCWorkspace workspace);
        Task WorkspacesRemoveAsync(ITfsVCWorkspace workspace);
        Task WorkspaceNewAsync();
        Task WorkfoldUnmapAsync(string serverPath);
        Task WorkfoldMapAsync(string serverPath, string localPath);
        Task WorkfoldCloakAsync(string serverPath);
        Task GetAsync(string localPath, bool quiet = false);
        Task AddAsync(string localPath);
        Task ShelveAsync(string shelveset, string commentFile, bool move);
        Task<ITfsVCShelveset> ShelvesetsAsync(string shelveset);
        Task UnshelveAsync(string shelveset, bool failOnNonZeroExitCode);
        void CleanupProxySetting();
    }
}