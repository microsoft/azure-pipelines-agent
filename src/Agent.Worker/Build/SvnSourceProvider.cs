using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.Agent;
using System;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Services.Agent.Util;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;

namespace Microsoft.VisualStudio.Services.Agent.Worker.Build
{
    public sealed class SvnSourceProvider : SourceProvider, ISourceProvider
    {
        public override string RepositoryType => WellKnownRepositoryTypes.Svn;

        public async Task GetSourceAsync(IExecutionContext executionContext, ServiceEndpoint endpoint, CancellationToken cancellationToken)
        {
            executionContext.Debug("Entering SvnSourceProvider.GetSourceAsync");

            // Validate args.
            ArgUtil.NotNull(executionContext, nameof(executionContext));
            ArgUtil.NotNull(endpoint, nameof(endpoint));

            ISvnCommandManager svn = HostContext.CreateService<ISvnCommandManager>();
            svn.Init(executionContext, endpoint, cancellationToken);

            // Determine the sources directory.
            string sourcesDirectory = executionContext.Variables.Build_SourcesDirectory;
            ArgUtil.NotNullOrEmpty(sourcesDirectory, nameof(sourcesDirectory));

            string sourceBranch = executionContext.Variables.Build_SourceBranch;
            string revision = executionContext.Variables.Build_SourceVersion;

            bool clean = endpoint.Data.ContainsKey(WellKnownEndpointData.Clean) &&
                StringUtil.ConvertToBoolean(endpoint.Data[WellKnownEndpointData.Clean], defaultValue: false);

            // Get the definition mappings.
            List<SvnMappingDetails> allMappings = JsonConvert.DeserializeObject<SvnWorkspace>(endpoint.Data[WellKnownEndpointData.SvnWorkspaceMapping]).Mappings;

            Dictionary<string, SvnMappingDetails> normalizedMappings = svn.NormalizeMappings(allMappings);
            string normalizedBranch = svn.NormalizeRelativePath(sourceBranch, '/', '\\');

            executionContext.Output(StringUtil.Loc("SvnSyncingRepo", endpoint.Name));

            string effectiveRevision = await svn.UpdateWorkspace(
                sourcesDirectory,
                normalizedMappings,
                clean,
                normalizedBranch,
                revision);

            executionContext.Output(StringUtil.Loc("SvnBranchCheckedOut", normalizedBranch, endpoint.Name, effectiveRevision));
            executionContext.Debug("Leaving SvnSourceProvider.GetSourceAsync");
        }

        public Task PostJobCleanupAsync(IExecutionContext executionContext, ServiceEndpoint endpoint)
        {
            return Task.CompletedTask;
        }
    }
}