using Agent.Sdk.Knob;
using Microsoft.TeamFoundation.Common;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker.Telemetry;
using Microsoft.VisualStudio.Services.WebPlatform;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    [ServiceLocator(Default = typeof(TaskRestrictionsChecker))]
    public interface ITaskRestrictionsChecker : IAgentService
    {
        bool CheckCommand(IExecutionContext context, IWorkerCommand workerCommand, Command command);
        bool CheckSettableVariable(IExecutionContext context, string variable);
    }

    public sealed class TaskRestrictionsChecker : AgentService, ITaskRestrictionsChecker
    {
        public bool CheckCommand(IExecutionContext context, IWorkerCommand workerCommand, Command command)
        {
            ArgUtil.NotNull(context, nameof(context));
            ArgUtil.NotNull(workerCommand, nameof(workerCommand));
            ArgUtil.NotNull(command, nameof(command));

            return Check(
                context,
                (TaskRestrictions restrictions) => restrictions.IsCommandAllowed(workerCommand),
                () => context.Warning(StringUtil.Loc("CommandNotAllowed", command.Area, command.Event)));
        }

        public bool CheckSettableVariable(IExecutionContext context, string variable)
        {
            ArgUtil.NotNull(context, nameof(context));
            ArgUtil.NotNull(variable, nameof(variable));

            return Check(
                context,
                (TaskRestrictions restrictions) => restrictions.IsSetVariableAllowed(variable),
                () => context.Warning(StringUtil.Loc("SetVariableNotAllowed", variable)));
        }

        private bool Check(
            IExecutionContext context,
            Func<TaskRestrictions, bool> predicate,
            Action warn)
        {
            ArgUtil.NotNull(context, nameof(context));
            ArgUtil.NotNull(predicate, nameof(predicate));
            ArgUtil.NotNull(warn, nameof(warn));

            var failedMatches = context.Restrictions?.Where(restrictions => !predicate(restrictions));

            if (failedMatches.IsNullOrEmpty())
            {
                return true;
            }
            else
            {
                warn();
                return false;
            }
        }
    }
}
