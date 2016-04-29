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
using System.Linq;

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
            executionContext.Debug($"sourcesDirectory={sourcesDirectory}");
            ArgUtil.NotNullOrEmpty(sourcesDirectory, nameof(sourcesDirectory));

            string sourceBranch = executionContext.Variables.Build_SourceBranch;
            executionContext.Debug($"sourceBranch={sourceBranch}");

            string revision = executionContext.Variables.Build_SourceVersion;
            if (string.IsNullOrWhiteSpace(revision))
            {
                revision = "HEAD";
            }
            executionContext.Debug($"revision={revision}");

            bool clean = endpoint.Data.ContainsKey(WellKnownEndpointData.Clean) &&
                StringUtil.ConvertToBoolean(endpoint.Data[WellKnownEndpointData.Clean], defaultValue: false);

            // Get the definition mappings.
            List<SvnMappingDetails> allMappings = JsonConvert.DeserializeObject<SvnWorkspace>(endpoint.Data[WellKnownEndpointData.SvnWorkspaceMapping]).Mappings;
            executionContext.Debug($"clean={clean}");

            allMappings.ForEach(m => executionContext.Debug($"ServerPath: {m.ServerPath}, LocalPath: {m.LocalPath}, Depth: {m.Depth}, Revision: {m.Revision}, IgnoreExternals: {m.IgnoreExternals}"));

            Dictionary<string, SvnMappingDetails> normalizedMappings = svn.NormalizeMappings(allMappings);
            normalizedMappings.ToList().ForEach(p => executionContext.Debug($"    [{p.Key}] ServerPath: {p.Value.ServerPath}, LocalPath: {p.Value.LocalPath}, Depth: {p.Value.Depth}, Revision: {p.Value.Revision}, IgnoreExternals: {p.Value.IgnoreExternals}"));

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

        public override string GetLocalPath(IExecutionContext executionContext, ServiceEndpoint endpoint, string path)
        {
            ISvnCommandManager svn = HostContext.CreateService<ISvnCommandManager>();
            svn.Init(executionContext, endpoint, CancellationToken.None);

            // We assume that this is a server path first.
            string serverPath = svn.NormalizeRelativePath(path, '/', '\\').Trim();

            if (serverPath.StartsWith("^/"))
            {
                //Convert the server path to the relative one using SVN work copy mappings
                string localPath = serverPath;

                //TODO: do the conversion.
                return localPath;
            }
            else
            {
                // normalize the path back to the local file system one.
                return svn.NormalizeRelativePath(serverPath, Path.DirectorySeparatorChar, '/');
            }
        }

        public Task PostJobCleanupAsync(IExecutionContext executionContext, ServiceEndpoint endpoint)
        {
            return Task.CompletedTask;
        }
    }
}