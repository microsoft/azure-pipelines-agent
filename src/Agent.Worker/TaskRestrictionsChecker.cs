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
                () => context.Warning(StringUtil.Loc("CommandNotAllowed", command.Area, command.Event)),
                () => context.Warning(StringUtil.Loc("CommandNotAllowedWarnOnly", command.Area, command.Event)),
                (TaskDefinitionRestrictions restrictions) => PublishCommandTelemetry(context, restrictions, command));
        }

        public bool CheckSettableVariable(IExecutionContext context, string variable)
        {
            ArgUtil.NotNull(context, nameof(context));
            ArgUtil.NotNull(variable, nameof(variable));

            return Check(
                context,
                (TaskRestrictions restrictions) => restrictions.IsSetVariableAllowed(variable),
                () => context.Warning(StringUtil.Loc("SetVariableNotAllowed", variable)),
                () => context.Warning(StringUtil.Loc("SetVariableNotAllowedWarnOnly", variable)),
                (TaskDefinitionRestrictions restrictions) => PublishVariableTelemetry(context, restrictions, variable));
        }

        private bool Check(
            IExecutionContext context,
            Func<TaskRestrictions, bool> predicate,
            Action enforcedWarn,
            Action unenforcedWarn,
            Action<TaskDefinitionRestrictions> publishTelemetry)
        {
            ArgUtil.NotNull(context, nameof(context));
            ArgUtil.NotNull(predicate, nameof(predicate));
            ArgUtil.NotNull(enforcedWarn, nameof(enforcedWarn));
            ArgUtil.NotNull(unenforcedWarn, nameof(unenforcedWarn));
            ArgUtil.NotNull(publishTelemetry, nameof(publishTelemetry));

            var failedMatches = context.Restrictions?.Where(restrictions => !predicate(restrictions));

            if (failedMatches.IsNullOrEmpty())
            {
                return true;
            }
            else
            {
                var taskMatches = failedMatches.Where(restrictions => restrictions is TaskDefinitionRestrictions).Cast<TaskDefinitionRestrictions>();

                if(AgentKnobs.EnableTaskRestrictionsTelemetry.GetValue(context).AsBoolean())
                {
                    foreach(var match in taskMatches)
                    {
                        publishTelemetry(match);
                    }
                }

                string mode = AgentKnobs.TaskRestrictionsEnforcementMode.GetValue(context).AsString();

                if (String.Equals(mode, "Enabled", StringComparison.OrdinalIgnoreCase) || taskMatches.Count() != failedMatches.Count())
                {
                    // we are enforcing restrictions from tasks, or we matched restrictions from the pipeline, which we always enforce
                    enforcedWarn();
                    return false;
                }
                else
                {
                    if (!String.Equals(mode, "Disabled", StringComparison.OrdinalIgnoreCase))
                    {
                        unenforcedWarn();
                    }
                    return true;
                }
            }
        }

        private void PublishCommandTelemetry(IExecutionContext context, TaskDefinitionRestrictions restrictions, Command command)
        {
            ArgUtil.NotNull(context, nameof(context));
            ArgUtil.NotNull(restrictions, nameof(restrictions));
            ArgUtil.NotNull(command, nameof(command));

            var data = new Dictionary<string, object>()
            {
                { "Command", $"{command.Area}.{command.Event}" }
            };
            PublishTelemetry(context, restrictions, "TaskRestrictions_Command", data);
        }

        private void PublishVariableTelemetry(IExecutionContext context, TaskDefinitionRestrictions restrictions, string variable)
        {
            ArgUtil.NotNull(context, nameof(context));
            ArgUtil.NotNull(restrictions, nameof(restrictions));
            ArgUtil.NotNull(variable, nameof(variable));

            var data = new Dictionary<string, object>()
            {
                { "Variable", variable }
            };
            PublishTelemetry(context, restrictions, "TaskRestrictions_SetVariable", data);
        }

        private void PublishTelemetry(IExecutionContext context, TaskDefinitionRestrictions restrictions, string feature, Dictionary<string, object> data)
        {
            ArgUtil.NotNull(context, nameof(context));
            ArgUtil.NotNull(restrictions, nameof(restrictions));
            ArgUtil.NotNull(feature, nameof(feature));
            ArgUtil.NotNull(data, nameof(data));

            data.Add("TaskName", restrictions.Definition.Name);
            data.Add("TaskVersion", restrictions.Definition.Version);

            CustomerIntelligenceEvent ciEvent = new CustomerIntelligenceEvent()
            {
                Area = "AzurePipelinesAgent",
                Feature = feature,
                Properties = data
            };

            var publishCommand = new PublishTelemetryCommand();
            publishCommand.PublishEvent(context, ciEvent);
        }
    }
}
