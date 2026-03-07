// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Agent.Sdk;
using Microsoft.VisualStudio.Services.Agent.Util;
using Newtonsoft.Json.Linq;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    /// <summary>
    /// Represents diagnostic information about why a handler is not available.
    /// </summary>
    public sealed class HandlerFilterInfo
    {
        public string HandlerName { get; set; }
        public bool IsDeclaredInTask { get; set; }
        public bool IsLoadedByAgent { get; set; }
        public bool IsFilteredByPlatform { get; set; }
        public string FilterReason { get; set; }
        public bool IsWindowsOnly { get; set; }
        public bool IsDeprecated { get; set; }
        public bool IsUnknownToAgent { get; set; }
    }

    /// <summary>
    /// Categorizes the reason why no handler could be selected.
    /// </summary>
    public enum HandlerIncompatibilityReason
    {
        /// <summary>No execution section defined in task.json</summary>
        NoExecutionSection,

        /// <summary>Task only declares handlers for a different OS (e.g., PowerShell handlers on Linux)</summary>
        OperatingSystemIncompatible,

        /// <summary>Task only declares deprecated handlers (Node 6, Node 10) that are blocked by EOL policy</summary>
        OnlyDeprecatedHandlers,

        /// <summary>Task declares handlers newer than this agent version supports (e.g., Node26)</summary>
        HandlersNewerThanAgent,

        /// <summary>Mixed reasons or unknown</summary>
        Unknown
    }

    /// <summary>
    /// Contains comprehensive diagnostic information about handler selection failure.
    /// </summary>
    public sealed class HandlerDiagnosticsResult
    {
        public HandlerIncompatibilityReason Reason { get; set; }
        public List<HandlerFilterInfo> DeclaredHandlers { get; set; } = new List<HandlerFilterInfo>();
        public List<string> LoadedHandlers { get; set; } = new List<string>();
        public List<string> SupportedHandlersByAgent { get; set; } = new List<string>();
        public List<string> SupportedOperatingSystems { get; set; } = new List<string>();
        public string TaskName { get; set; }
        public string TaskVersion { get; set; }
        public JobRunStage Stage { get; set; }
        public bool IsSelfHosted { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Provides diagnostic analysis for handler selection failures.
    /// </summary>
    public static class HandlerDiagnostics
    {
        // Known handler names that this agent version understands
        private static readonly HashSet<string> _knownHandlers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Node", "Node10", "Node16", "Node20_1", "Node24",
            "PowerShell3", "PowerShell", "PowerShellExe", "AzurePowerShell",
            "Process", "AgentPlugin"
        };

        // Deprecated Node handlers
        private static readonly HashSet<string> _deprecatedNodeHandlers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Node", "Node10", "Node16"
        };

        // Windows-only handlers
        private static readonly HashSet<string> _windowsOnlyHandlers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PowerShell3", "PowerShell", "PowerShellExe", "AzurePowerShell", "Process"
        };

        /// <summary>
        /// Analyzes why no suitable handler was found for a task by comparing the raw task.json
        /// against what was actually loaded by the ExecutionData class (which applies platform filters).
        /// </summary>
        /// <param name="taskDirectory">Directory containing the task.json</param>
        /// <param name="loadedExecution">The ExecutionData loaded by TaskManager (already filtered)</param>
        /// <param name="stage">The execution stage (PreJob, Main, PostJob)</param>
        /// <param name="taskName">Name of the task</param>
        /// <param name="taskVersion">Version of the task</param>
        /// <param name="isSelfHosted">True if the agent is self-hosted, false if Microsoft-hosted</param>
        /// <returns>Diagnostic result with detailed information</returns>
        public static HandlerDiagnosticsResult Analyze(
            string taskDirectory,
            ExecutionData loadedExecution,
            JobRunStage stage,
            string taskName,
            string taskVersion,
            bool isSelfHosted = true)
        {
            var result = new HandlerDiagnosticsResult
            {
                TaskName = taskName,
                TaskVersion = taskVersion,
                Stage = stage,
                IsSelfHosted = isSelfHosted,
                SupportedHandlersByAgent = GetAgentSupportedHandlers()
            };

            // Capture what was actually loaded (after platform filtering by ExecutionData)
            if (loadedExecution?.All != null)
            {
                result.LoadedHandlers = loadedExecution.All.Select(h => h.GetType().Name.Replace("HandlerData", "")).ToList();
            }

            try
            {
                // Read raw task.json to get all declared handlers (before filtering)
                string taskJsonPath = Path.Combine(taskDirectory, Constants.Path.TaskJsonFile);
                if (!File.Exists(taskJsonPath))
                {
                    result.Reason = HandlerIncompatibilityReason.NoExecutionSection;
                    result.ErrorMessage = $"Task definition file not found: {taskJsonPath}";
                    return result;
                }

                string taskJsonText = File.ReadAllText(taskJsonPath);
                JObject taskJson = JObject.Parse(taskJsonText);

                // Get the appropriate execution section based on stage
                string executionProperty = stage switch
                {
                    JobRunStage.PreJob => "prejobexecution",
                    JobRunStage.PostJob => "postjobexecution",
                    _ => "execution"
                };

                JObject executionSection = GetExecutionSection(taskJson, executionProperty);

                if (executionSection == null || !executionSection.Properties().Any())
                {
                    result.Reason = HandlerIncompatibilityReason.NoExecutionSection;
                    result.ErrorMessage = $"No '{executionProperty}' section found in task.json for task '{taskName}@{taskVersion}'.";
                    return result;
                }

                // Analyze each declared handler by comparing raw JSON to loaded handlers
                AnalyzeDeclaredHandlers(executionSection, result);

                // Determine the incompatibility reason
                DetermineIncompatibilityReason(result);

                // Build the error message
                result.ErrorMessage = BuildErrorMessage(result);
            }
            catch (Exception ex)
            {
                result.Reason = HandlerIncompatibilityReason.Unknown;
                result.ErrorMessage = $"Failed to analyze task handlers: {ex.Message}";
            }

            return result;
        }

        private static JObject GetExecutionSection(JObject taskJson, string propertyName)
        {
            // Try case-insensitive search
            foreach (var prop in taskJson.Properties())
            {
                if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    return prop.Value as JObject;
                }
            }
            return null;
        }

        private static void AnalyzeDeclaredHandlers(JObject executionSection, HandlerDiagnosticsResult result)
        {
            foreach (var prop in executionSection.Properties())
            {
                string handlerName = prop.Name;
                
                // Normalize handler name for comparison (ExecutionData uses property names like Node20_1)
                string normalizedName = NormalizeHandlerName(handlerName);
                
                var filterInfo = new HandlerFilterInfo
                {
                    HandlerName = handlerName,
                    IsDeclaredInTask = true,
                    IsLoadedByAgent = result.LoadedHandlers.Any(h => 
                        string.Equals(h, normalizedName, StringComparison.OrdinalIgnoreCase)),
                    IsWindowsOnly = _windowsOnlyHandlers.Contains(handlerName),
                    IsDeprecated = _deprecatedNodeHandlers.Contains(handlerName),
                    IsUnknownToAgent = !_knownHandlers.Contains(handlerName)
                };

                // Determine if filtered and why
                filterInfo.IsFilteredByPlatform = !filterInfo.IsLoadedByAgent && !filterInfo.IsUnknownToAgent;

                // Determine filter reason
                if (filterInfo.IsUnknownToAgent)
                {
                    filterInfo.FilterReason = $"Handler '{handlerName}' is not recognized by this agent version. This may require a newer agent.";
                }
                else if (!filterInfo.IsLoadedByAgent)
                {
                    if (filterInfo.IsWindowsOnly && !PlatformUtil.RunningOnWindows)
                    {
                        filterInfo.FilterReason = $"Handler '{handlerName}' is only available on Windows.";
                    }
                    else if (filterInfo.IsWindowsOnly && PlatformUtil.IsX86 && 
                             (handlerName.Equals("AzurePowerShell", StringComparison.OrdinalIgnoreCase) ||
                              handlerName.Equals("PowerShell", StringComparison.OrdinalIgnoreCase)))
                    {
                        filterInfo.FilterReason = $"Handler '{handlerName}' is not available on x86 Windows.";
                    }
                    else
                    {
                        filterInfo.FilterReason = "Filtered by platform during load.";
                    }
                }

                result.DeclaredHandlers.Add(filterInfo);
            }
        }

        /// <summary>
        /// Normalizes handler names from task.json to match the property names used in ExecutionData.
        /// For example, "Node20" in task.json maps to "Node20_1" in ExecutionData.
        /// </summary>
        private static string NormalizeHandlerName(string handlerName)
        {
            // Handle Node20 -> Node20_1 mapping (the underscore version is used internally)
            if (string.Equals(handlerName, "Node20", StringComparison.OrdinalIgnoreCase))
            {
                return "Node20_1";
            }
            return handlerName;
        }

        private static void DetermineIncompatibilityReason(HandlerDiagnosticsResult result)
        {
            var handlers = result.DeclaredHandlers;

            if (!handlers.Any())
            {
                result.Reason = HandlerIncompatibilityReason.NoExecutionSection;
                return;
            }

            // Check if all handlers are unknown (newer than agent supports)
            if (handlers.All(h => h.IsUnknownToAgent))
            {
                result.Reason = HandlerIncompatibilityReason.HandlersNewerThanAgent;
                return;
            }

            // Check if all known handlers are deprecated
            var knownHandlers = handlers.Where(h => !h.IsUnknownToAgent).ToList();
            if (knownHandlers.All(h => h.IsDeprecated))
            {
                result.Reason = HandlerIncompatibilityReason.OnlyDeprecatedHandlers;
                return;
            }

            // Check if all handlers are Windows-only (and we're not on Windows)
            if (!PlatformUtil.RunningOnWindows && handlers.All(h => h.IsWindowsOnly || h.IsUnknownToAgent))
            {
                result.Reason = HandlerIncompatibilityReason.OperatingSystemIncompatible;
                result.SupportedOperatingSystems.Add("Windows");
                return;
            }

            // Check if we're on Windows but all non-Windows handlers are deprecated/unknown
            if (PlatformUtil.RunningOnWindows)
            {
                var nonWindowsHandlers = handlers.Where(h => !h.IsWindowsOnly).ToList();
                if (nonWindowsHandlers.Any() && nonWindowsHandlers.All(h => h.IsDeprecated))
                {
                    // Windows has PowerShell options filtered out for other reasons, and Node handlers are deprecated
                    var windowsHandlers = handlers.Where(h => h.IsWindowsOnly).ToList();
                    if (windowsHandlers.All(h => !h.IsLoadedByAgent))
                    {
                        result.Reason = HandlerIncompatibilityReason.OnlyDeprecatedHandlers;
                        return;
                    }
                }
            }

            result.Reason = HandlerIncompatibilityReason.Unknown;
        }

        private static string BuildErrorMessage(HandlerDiagnosticsResult result)
        {
            string taskRef = $"{result.TaskName}@{result.TaskVersion}";
            string stageName = result.Stage.ToString();
            string platformInfo = $"{PlatformUtil.HostOS}({PlatformUtil.HostArchitecture})";

            switch (result.Reason)
            {
                case HandlerIncompatibilityReason.NoExecutionSection:
                    return StringUtil.Loc("TaskHandlerNoExecutionSection", taskRef, stageName);

                case HandlerIncompatibilityReason.OperatingSystemIncompatible:
                    var osHandlersList = FormatHandlerList(result.DeclaredHandlers, h =>
                    {
                        string status = h.IsWindowsOnly ? " (Windows only)" : "";
                        status += h.IsFilteredByPlatform ? " [filtered]" : "";
                        return $"{h.HandlerName}{status}";
                    });
                    var supportedOs = string.Join(", ", result.SupportedOperatingSystems);
                    return StringUtil.Loc("TaskHandlerOSIncompatible", taskRef, platformInfo, osHandlersList, supportedOs);

                case HandlerIncompatibilityReason.OnlyDeprecatedHandlers:
                    var deprecatedHandlersList = FormatHandlerList(
                        result.DeclaredHandlers.Where(h => !h.IsUnknownToAgent),
                        h => $"{h.HandlerName}{(h.IsDeprecated ? " (deprecated/EOL)" : "")}");
                    var deprecatedSolutions = FormatBulletList(
                        StringUtil.Loc("TaskHandlerSolutionUpdateTask"),
                        StringUtil.Loc("TaskHandlerSolutionNodeInstaller"),
                        StringUtil.Loc("TaskHandlerSolutionContactAuthor"));
                    return StringUtil.Loc("TaskHandlerOnlyDeprecated", taskRef, deprecatedHandlersList, deprecatedSolutions);

                case HandlerIncompatibilityReason.HandlersNewerThanAgent:
                    var newerHandlersList = FormatHandlerList(result.DeclaredHandlers, h => $"{h.HandlerName} (not recognized by this agent)");
                    var agentHandlersList = FormatBulletList(result.SupportedHandlersByAgent.ToArray());
                    var newerSolutionsList = new List<string>();
                    if (result.IsSelfHosted)
                    {
                        newerSolutionsList.Add(StringUtil.Loc("TaskHandlerSolutionUpdateAgent"));
                    }
                    newerSolutionsList.Add(StringUtil.Loc("TaskHandlerSolutionNodeInstaller"));
                    var newerSolutions = FormatBulletList(newerSolutionsList.ToArray());
                    return StringUtil.Loc("TaskHandlerNewerThanAgent", taskRef, newerHandlersList, agentHandlersList, newerSolutions);

                default:
                    var unknownHandlersList = FormatHandlerList(result.DeclaredHandlers, h =>
                    {
                        string status = h.IsLoadedByAgent ? "loaded" : (h.FilterReason ?? "filtered");
                        return $"{h.HandlerName}: {status}";
                    });
                    var supportedHandlersList = FormatBulletList(result.SupportedHandlersByAgent.ToArray());
                    return StringUtil.Loc("TaskHandlerUnknown", taskRef, unknownHandlersList, supportedHandlersList, platformInfo);
            }
        }

        private static string FormatHandlerList(IEnumerable<HandlerFilterInfo> handlers, Func<HandlerFilterInfo, string> formatter)
        {
            return string.Join(Environment.NewLine, handlers.Select(h => $"  - {formatter(h)}"));
        }

        private static string FormatBulletList(params string[] items)
        {
            return string.Join(Environment.NewLine, items.Select(item => $"  * {item}"));
        }

        private static List<string> GetAgentSupportedHandlers()
        {
            var handlers = new List<string>();

            // Node handlers are always potentially available (cross-platform)
            // Note: Node and Node10 are deprecated but still recognized
            handlers.Add("Node (deprecated)");
            handlers.Add("Node10 (deprecated)");
            handlers.Add("Node16");
            handlers.Add("Node20_1");
            handlers.Add("Node24");

            // Windows-only handlers
            if (PlatformUtil.RunningOnWindows)
            {
                handlers.Add("PowerShell3");
                handlers.Add("PowerShellExe");
                handlers.Add("Process");
                
                if (!PlatformUtil.IsX86)
                {
                    handlers.Add("PowerShell");
                    handlers.Add("AzurePowerShell");
                }
            }

            // AgentPlugin is cross-platform
            handlers.Add("AgentPlugin");

            return handlers;
        }
    }
}
