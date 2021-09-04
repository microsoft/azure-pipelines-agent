// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Services.WebApi;

namespace Microsoft.VisualStudio.Services.Agent.Worker.Build
{
    public sealed class BuildCommandExtension : BaseWorkerCommandExtension
    {
        public BuildCommandExtension()
        {
            CommandArea = "build";
            SupportedHostTypes = HostTypes.All;
            InstallWorkerCommand(new BuildUploadLogCommand());
            InstallWorkerCommand(new BuildUploadSummaryCommand());
            InstallWorkerCommand(new BuildUpdateBuildNumberCommand());
            InstallWorkerCommand(new BuildAddBuildTagCommand());
        }
    }

    public sealed class BuildUploadLogCommand : IWorkerCommand
    {
        public string Name => "uploadlog";
        public List<string> Aliases => null;
        public void Execute(IExecutionContext context, Command command)
        {
            ArgUtil.NotNull(context, nameof(context));
            ArgUtil.NotNull(command, nameof(command));

            var data = command.Data;
            // Translate file path back from container path
            data = context.TranslateToHostPath(data);
            if (!string.IsNullOrEmpty(data) && File.Exists(data))
            {
                context.QueueAttachFile(CoreAttachmentType.Log, "CustomToolLog", data);
            }
            else
            {
                throw new Exception(StringUtil.Loc("CustomLogDoesNotExist", data ?? string.Empty));
            }
        }
    }

    // ##VSO[build.uploadsummary] command has been deprecated
    // Leave the implementation on agent for back compat
    public sealed class BuildUploadSummaryCommand : IWorkerCommand
    {
        public string Name => "uploadsummary";
        public List<string> Aliases => null;
        public void Execute(IExecutionContext context, Command command)
        {
            ArgUtil.NotNull(context, nameof(context));
            ArgUtil.NotNull(command, nameof(command));

            var data = command.Data;
            // Translate file path back from container path
            data = context.TranslateToHostPath(data);
            if (!string.IsNullOrEmpty(data) && File.Exists(data))
            {
                var fileName = Path.GetFileName(data);
                context.QueueAttachFile(CoreAttachmentType.Summary, StringUtil.Format($"CustomMarkDownSummary-{fileName}"), data);
            }
            else
            {
                throw new Exception(StringUtil.Loc("CustomMarkDownSummaryDoesNotExist", data ?? string.Empty));
            }
        }
    }

    public sealed class BuildUpdateBuildNumberCommand : IWorkerCommand
    {
        public string Name => "updatebuildnumber";
        public List<string> Aliases => null;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA2000:Dispose objects before losing scope", MessageId = "GetVssConnection")]
        public void Execute(IExecutionContext context, Command command)
        {
            ArgUtil.NotNull(context, nameof(context));
            ArgUtil.NotNull(context.Endpoints, nameof(context.Endpoints));
            ArgUtil.NotNull(command, nameof(command));

            string data = command.Data;

            Guid projectId = context.Variables.System_TeamProjectId ?? Guid.Empty;
            ArgUtil.NotEmpty(projectId, nameof(projectId));

            int? buildId = context.Variables.Build_BuildId;
            ArgUtil.NotNull(buildId, nameof(buildId));

            if (!String.IsNullOrEmpty(data))
            {
                // update build number within Context.
                context.Variables.Set(BuildVariables.BuildNumber, data);

                // queue async command task to update build number.
                context.Debug($"Update build number for build: {buildId.Value} to: {data} at backend.");
                var commandContext = context.GetHostContext().CreateService<IAsyncCommandContext>();
                commandContext.InitializeCommandContext(context, StringUtil.Loc("UpdateBuildNumber"));
                commandContext.Task = UpdateBuildNumberAsync(commandContext,
                                                             WorkerUtilities.GetVssConnection(context),
                                                             projectId,
                                                             buildId.Value,
                                                             data,
                                                             context.CancellationToken);

                context.AsyncCommands.Add(commandContext);
            }
            else
            {
                throw new Exception(StringUtil.Loc("BuildNumberRequired"));
            }
        }

        private async Task UpdateBuildNumberAsync(
            IAsyncCommandContext context,
            VssConnection connection,
            Guid projectId,
            int buildId,
            string buildNumber,
            CancellationToken cancellationToken)
        {
            var buildServer = context.GetHostContext().GetService<IBuildServer>();
            await buildServer.ConnectAsync(connection);
            var build = await buildServer.UpdateBuildNumber(buildId, projectId, buildNumber, cancellationToken);
            context.Output(StringUtil.Loc("UpdateBuildNumberForBuild", build.BuildNumber, build.Id));
        }
    }

    public sealed class BuildAddBuildTagCommand : IWorkerCommand
    {
        public string Name => "addbuildtag";
        public List<string> Aliases => null;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA2000:Dispose objects before losing scope", MessageId = "GetVssConnection")]
        public void Execute(IExecutionContext context, Command command)
        {
            ArgUtil.NotNull(context, nameof(context));
            ArgUtil.NotNull(context.Endpoints, nameof(context.Endpoints));
            ArgUtil.NotNull(command, nameof(command));

            string data = command.Data;

            Guid projectId = context.Variables.System_TeamProjectId ?? Guid.Empty;
            ArgUtil.NotEmpty(projectId, nameof(projectId));

            int? buildId = context.Variables.Build_BuildId;
            ArgUtil.NotNull(buildId, nameof(buildId));

            if (!string.IsNullOrEmpty(data))
            {
                // queue async command task to associate artifact.
                context.Debug($"Add build tag: {data} to build: {buildId.Value} at backend.");
                var commandContext = context.GetHostContext().CreateService<IAsyncCommandContext>();
                commandContext.InitializeCommandContext(context, StringUtil.Loc("AddBuildTag"));
                commandContext.Task = AddBuildTagAsync(commandContext,
                                                       WorkerUtilities.GetVssConnection(context),
                                                       projectId,
                                                       buildId.Value,
                                                       data,
                                                       context.CancellationToken);
                context.AsyncCommands.Add(commandContext);
            }
            else
            {
                throw new Exception(StringUtil.Loc("BuildTagRequired"));
            }
        }

        private async Task AddBuildTagAsync(
            IAsyncCommandContext context,
            VssConnection connection,
            Guid projectId,
            int buildId,
            string buildTag,
            CancellationToken cancellationToken)
        {
            var buildServer = context.GetHostContext().GetService<IBuildServer>();
            await buildServer.ConnectAsync(connection);
            var tags = await buildServer.AddBuildTag(buildId, projectId, buildTag, cancellationToken);

            if (tags == null || !tags.Any(t => t.Equals(buildTag, StringComparison.OrdinalIgnoreCase)))
            {
                throw new Exception(StringUtil.Loc("BuildTagAddFailed", buildTag));
            }
            else
            {
                context.Output(StringUtil.Loc("BuildTagsForBuild", buildId, String.Join(", ", tags)));
            }
        }
    }
}