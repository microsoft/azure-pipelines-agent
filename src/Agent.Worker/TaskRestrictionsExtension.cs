// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Minimatch;
using System;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class CommandRestrictionAttribute : Attribute
    {
        public bool AllowedInRestrictedMode { get; set; }
    }

    public static class TaskRestrictionExtension
    {
        public static Boolean IsCommandAllowed(this TaskRestrictions restrictions, IWorkerCommand command)
        {
            ArgUtil.NotNull(command, nameof(command));

            if (restrictions.Commands?.Mode == TaskCommandMode.Restricted)
            {
                foreach (var attr in command.GetType().GetCustomAttributes(typeof(CommandRestrictionAttribute), false))
                {
                    var cra = attr as CommandRestrictionAttribute;
                    if (cra.AllowedInRestrictedMode)
                    {
                        return true;
                    }
                }

                return false;
            }
            else
            {
                return true;
            }
        }

        public static Boolean IsSetVariableAllowed(this TaskRestrictions restrictions, String variable)
        {
            ArgUtil.NotNull(variable, nameof(variable));

            var allowedList = restrictions.SettableVariables?.Allowed;
            if (allowedList == null)
            {
                return true;
            }

            var opts = new Options() { IgnoreCase = true };
            foreach (String pattern in allowedList)
            {
                if (Minimatcher.Check(variable, pattern, opts))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public sealed class TaskDefinitionRestrictions : TaskRestrictions
    {
        public TaskDefinitionRestrictions(DefinitionData definition) : base()
        {
            Definition = definition;
            Commands = definition.Restrictions?.Commands;
            SettableVariables = definition.Restrictions?.SettableVariables;
        }

        public DefinitionData Definition { get; }
    }
}
