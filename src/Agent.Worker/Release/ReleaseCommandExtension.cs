// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Services.WebApi;

namespace Microsoft.VisualStudio.Services.Agent.Worker.Release
{
    public sealed class ReleaseCommandExtension : BaseWorkerCommandExtension
    {
        public ReleaseCommandExtension()
        {
            CommandArea = "release";
            SupportedHostTypes = HostTypes.Release | HostTypes.Deployment;
            InstallWorkerCommand(new ReleaseUpdateReleaseNameCommand());
        }

        private class ReleaseUpdateReleaseNameCommand : IWorkerCommand
        {
            public string Name => "updatereleasename";
            public List<string> Aliases => null;

            [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA2000:Dispose objects before losing scope", MessageId = "GetVssConnection")]
            public void Execute(IExecutionContext context, Command command)
            {
                var data = command.Data;
                ArgUtil.NotNull(context, nameof(context));
                ArgUtil.NotNull(context.Endpoints, nameof(context.Endpoints));

                Guid projectId = context.Variables.System_TeamProjectId ?? Guid.Empty;
                ArgUtil.NotEmpty(projectId, nameof(projectId));

                string releaseId = context.Variables.Release_ReleaseId;
                ArgUtil.NotNull(releaseId, nameof(releaseId));

                if (!String.IsNullOrEmpty(data))
                {
                    // queue async command task to update release name.
                    context.Debug($"Update release name for release: {releaseId} to: {data} at backend.");
                    var commandContext = context.GetHostContext().CreateService<IAsyncCommandContext>();
                    commandContext.InitializeCommandContext(context, StringUtil.Loc("RMUpdateReleaseName"));
                    commandContext.Task = UpdateReleaseNameAsync(commandContext,
                                                                context,
                                                                WorkerUtilities.GetVssConnection(context),
                                                                projectId,
                                                                releaseId,
                                                                data,
                                                                context.CancellationToken);
                    context.AsyncCommands.Add(commandContext);
                }
                else
                {
                    throw new ArgumentNullException(StringUtil.Loc("RMReleaseNameRequired"));
                }
            }

            private async Task UpdateReleaseNameAsync(
                IAsyncCommandContext commandContext,
                IExecutionContext context,
                VssConnection connection,
                Guid projectId,
                string releaseId,
                string releaseName,
                CancellationToken cancellationToken)
            {
                var releaseServer = context.GetHostContext().GetService<IReleaseServer>();
                await releaseServer.ConnectAsync(connection);
                var release = await releaseServer.UpdateReleaseName(releaseId, projectId, releaseName, cancellationToken);
                commandContext.Output(StringUtil.Loc("RMUpdateReleaseNameForRelease", release.Name, release.Id));
                context.Variables.Set("release.releaseName", release.Name);
            }
        }
    }

}