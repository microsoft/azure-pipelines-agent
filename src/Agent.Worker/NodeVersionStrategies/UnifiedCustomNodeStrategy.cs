// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Microsoft.VisualStudio.Services.Agent.Worker.NodeVersionStrategies
{
    /// <summary>
    /// Strategy for custom Node.js paths specified by user.
    /// Priority: 0 (HIGHEST - always checked first)
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
    public sealed class UnifiedCustomNodeStrategy : IUnifiedNodeVersionStrategy
    {
        public string Name => "CustomNode";

        /// <summary>
        /// Checks if a custom node path is specified.
        /// 
        /// ⭐ EVALUATION LOGIC:
        /// - Host: Check ctx.StepTarget?.CustomNodePath
        /// - Container: Check ctx.Container?.CustomNodePath
        /// - If either is set, return true
        /// - No other conditions checked (bypasses knobs, EOL, glibc)
        /// </summary>
        public bool CanHandle(UnifiedNodeContext context)
        {
            // Host: Check StepTarget for custom path
            if (!context.IsContainer && context.StepTarget != null)
            {
                string customPath = context.StepTarget.CustomNodePath;
                if (!string.IsNullOrEmpty(customPath))
                {
                    context.ExecutionContext.Debug($"[CustomNodeStrategy] Found custom node path in StepTarget: {customPath}");
                    return true;
                }
            }

            // Container: Check Container for custom path
            if (context.IsContainer && context.Container != null)
            {
                string customPath = context.Container.CustomNodePath;
                if (!string.IsNullOrEmpty(customPath))
                {
                    context.ExecutionContext.Debug($"[CustomNodeStrategy] Found custom node path in Container: {customPath}");
                    return true;
                }
            }

            context.ExecutionContext.Debug("[CustomNodeStrategy] No custom node path found");
            return false;
        }

        /// <summary>
        /// Returns the custom node path directly.
        /// No translation needed - path is already in correct format.
        /// </summary>
        public NodePathResult GetNodePath(UnifiedNodeContext context)
        {
            string customPath = null;
            string source = null;

            // Get custom path from appropriate source
            if (!context.IsContainer && context.StepTarget != null)
            {
                customPath = context.StepTarget.CustomNodePath;
                source = "StepTarget.CustomNodePath";
            }
            else if (context.IsContainer && context.Container != null)
            {
                customPath = context.Container.CustomNodePath;
                source = "Container.CustomNodePath";
            }

            // if (string.IsNullOrEmpty(customPath))
            // {
            //     throw new InvalidOperationException(
            //         "CustomNodeStrategy.GetNodePath() called but no custom path found. " +
            //         "This should not happen - CanHandle() should have returned false.");
            // }

            context.ExecutionContext.Debug($"[CustomNodeStrategy] Using custom node path from {source}: {customPath}");

            // Translate to container path if running in container
            string finalPath = context.IsContainer && context.Container != null ? 
                              context.Container.TranslateToContainerPath(customPath) : customPath;

            // Extract version from path for logging (best effort)
            string nodeVersion = ExtractNodeVersionFromPath(customPath);

            return new NodePathResult
            {
                NodePath = finalPath,
                NodeVersion = nodeVersion ?? "custom",
                Reason = $"Custom Node.js path specified by user ({source})",
                Warning = null // No warnings for custom paths
            };
        }

        /// <summary>
        /// Attempts to extract node version from path.
        /// Example: "/usr/local/node20/bin/node" → "node20"
        /// </summary>
        private string ExtractNodeVersionFromPath(string nodePath)
        {
            try
            {
                // Look for "node" followed by digits in the path
                string[] parts = nodePath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string part in parts)
                {
                    if (part.StartsWith("node", StringComparison.OrdinalIgnoreCase) && 
                        part.Length > 4 && 
                        char.IsDigit(part[4]))
                    {
                        return part.ToLowerInvariant();
                    }
                }
            }
            catch
            {
                // Best effort - if extraction fails, return null
            }

            return null;
        }
    }
}
