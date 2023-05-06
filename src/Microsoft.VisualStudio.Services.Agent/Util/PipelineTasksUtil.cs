// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using System.Collections.Generic;
using System;

namespace Microsoft.VisualStudio.Services.Agent.Util
{
    public static class PipelineTasksUtil
    {
        public static readonly Dictionary<string, WellKnownScriptShell> ScriptShellsPerTaskNames = new Dictionary<string, WellKnownScriptShell>(StringComparer.OrdinalIgnoreCase)
        {
            ["PowerShell"] = WellKnownScriptShell.PowerShell,
            ["Bash"] = WellKnownScriptShell.Bash
        };

        public static WellKnownScriptShell GetShellByTaskName(string taskName, ExecutionTargetInfo target)
        {
            if (target is ContainerInfo targetContainer)
            {
                return GetShellByTaskName(taskName, targetContainer);
            }

            return GetShellByTaskName(taskName);
        }

        public static WellKnownScriptShell GetShellByTaskName(string taskName, ContainerInfo targetContainer)
        {
            ArgUtil.NotNull(targetContainer, nameof(targetContainer));

            // CmdLine works like cmd on Windows and bash on Linux or MacOs.
            // TODO: Make it determine more generic way.
            if (taskName == "CmdLine")
            {
                if (targetContainer.ImageOS == PlatformUtil.OS.Windows)
                {
                    return WellKnownScriptShell.Cmd;
                }
                else
                {
                    return WellKnownScriptShell.Bash;
                }
            }

            return GetShellByTaskName(taskName);
        }

        public static WellKnownScriptShell GetShellByTaskName(string taskName)
        {
            if (string.IsNullOrEmpty(taskName))
            {
                return WellKnownScriptShell.Unknown;
            }

            // CmdLine works like cmd on Windows and bash on Linux or MacOs.
            // TODO: Make it determine more generic way.
            if (taskName == "CmdLine")
            {
                if (PlatformUtil.RunningOnWindows)
                {
                    return WellKnownScriptShell.Cmd;
                }
                else
                {
                    return WellKnownScriptShell.Bash;
                }
            }
            else
            {
                var isTaskForShell = ScriptShellsPerTaskNames.TryGetValue(taskName, out var shell);
                if (isTaskForShell)
                {
                    return shell;
                }
            }

            return WellKnownScriptShell.Unknown;
        }
    }
}
