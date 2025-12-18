// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using Agent.Sdk.Knob;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker;

namespace Microsoft.VisualStudio.Services.Agent.Worker.NodeVersionStrategies
{
    /// <summary>
    /// Strategy for custom Node.js paths specified by user.
    /// Priority: HIGHEST (always checked first)
    /// 
    /// ⭐ EVALUATION RULES:
    /// 1. Check if custom node path exists (StepTarget.CustomNodePath or Container.CustomNodePath)
    /// 2. If exists, use it - NO other checks needed
    /// 3. NO knob required - custom path overrides everything
    /// 4. NO EOL policy check - user explicitly specified path
    /// 5. NO glibc fallback - use exact path user provided
    /// 
    /// This strategy bypasses all other logic and gives user full control.
    /// </summary>
    public sealed class CustomNodeStrategy : INodeVersionStrategy
    {
        /// <summary>
        /// Returns custom node path if available, otherwise null.
        /// 
        /// ⭐ EVALUATION LOGIC:
        /// - Host: Check ctx.StepTarget?.CustomNodePath
        /// - Container: Check ctx.Container?.CustomNodePath
        /// - If either is set, return NodeRunnerInfo with custom path
        /// - No other conditions checked (bypasses knobs, EOL, glibc)
        /// </summary>
        public NodeRunnerInfo CanHandle(TaskContext context, IExecutionContext executionContext, GlibcCompatibilityInfo glibcInfo)
        {
            string customPath = null;
            string source = null;

            // Host: Check StepTarget for custom path
            if (context.Container == null && context.StepTarget != null)
            {
                customPath = context.StepTarget.CustomNodePath;
                source = "StepTarget.CustomNodePath";
            }
            // Container: Check Container for custom path  
            else if (context.Container != null)
            {
                customPath = context.Container.CustomNodePath;
                source = "Container.CustomNodePath";
            }

            // Return null if no custom path found
            if (string.IsNullOrWhiteSpace(customPath))
            {
                executionContext.Debug("[CustomNodeStrategy] No custom node path found");
                return null;
            }

            executionContext.Debug($"[CustomNodeStrategy] Found custom node path in {source}: {customPath}");

            // For custom paths, use them directly as provided (matching original NodeHandler behavior)
            // Custom paths are expected to be appropriate for the execution environment
            return new NodeRunnerInfo
            {
                NodePath = customPath,
                NodeVersion = NodeVersion.Custom,
                Reason = $"Custom Node.js path specified by user ({source})",
                Warning = null
            };
        }
    }
}