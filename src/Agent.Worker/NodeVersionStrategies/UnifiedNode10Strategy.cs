// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using Agent.Sdk.Knob;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Microsoft.VisualStudio.Services.Agent.Worker.NodeVersionStrategies
{
    public sealed class UnifiedNode10Strategy : IUnifiedNodeVersionStrategy
    {
        public string Name => "Node10";

        public bool CanHandle(UnifiedNodeContext context)
        {

            bool hasNode10Handler = context.HandlerData is Node10HandlerData;
            bool eolPolicyEnabled = AgentKnobs.EnableEOLNodeVersionPolicy.GetValue(context.ExecutionContext).AsBoolean();
            
            
            // NODE 10 HANDLER CHECK
            if (hasNode10Handler)
            {
                // EOL CHECK
                if (eolPolicyEnabled) 
                {
                    context.ExecutionContext.Debug("[Node10Strategy] Node10HandlerData + EOL policy enabled → Throw exception");
                    throw new NotSupportedException(
                        "Task requires Node10 which has reached End-of-Life. " +
                        "This is blocked by organization policy. " +
                        "Please upgrade task to Node20 or Node24. " +
                        "To temporarily disable this check: Set AGENT_ENABLE_EOL_NODE_VERSION_POLICY=false");
                }
                // EOL off - use with warning
                context.ExecutionContext.Debug("[Node10Strategy] Node10HandlerData + EOL policy disabled → Handle with warning");
                return true;
            }

            // Alpine Fallback (Only if EOL Policy Disabled)
            bool isAlpine = context.IsAlpine;
            if (isAlpine)
            {
                context.ExecutionContext.Warning(
                    "Using Node10 on Alpine Linux because Node6 is not compatible. " +
                    "Node10 has reached End-of-Life. Please upgrade to Node20 or Node24 for continued support.");
                
                context.ExecutionContext.Debug("[Node10Strategy] EnableEOLNodeVersionPolicy=false + IsAlpine=true → Handle (Node6 doesn't work on Alpine)");
                return true;
            }

            // Cannot handle other handler types
            context.ExecutionContext.Debug("[Node10Strategy] Not Node10HandlerData → Cannot handle");
            return false;
        }

        /// <summary>
        /// Gets the Node10 path.
        /// ⭐ DEFENSE IN DEPTH: Double-checks EOL policy and throws if violated ⭐
        /// </summary>
        public NodePathResult GetNodePath(UnifiedNodeContext context)
        {
            // ⭐ DEFENSE IN DEPTH: Double-check EOL policy ⭐
            bool eolPolicyEnabled = AgentKnobs.EnableEOLNodeVersionPolicy.GetValue(context.ExecutionContext).AsBoolean();
            
            if (eolPolicyEnabled)
            {
                // This should not happen (CanHandle should have rejected), but check anyway
                string systemType = context.IsContainer ? "container" : "agent";
                throw new NotSupportedException(
                    $"Task requires Node10 (End-of-Life) on {systemType}. " +
                    $"This is blocked by organization policy. " +
                    $"Please upgrade task to Node20 or Node24. " +
                    $"To temporarily disable this check: Set AGENT_ENABLE_EOL_NODE_VERSION_POLICY=false");
            }

            // EOL policy is disabled - proceed with Node10
            string nodeFolder = "node10";

            // Get path based on environment (HOST vs CONTAINER)
            string nodePath = GetNodePathForEnvironment(context, nodeFolder);

            return new NodePathResult
            {
                NodePath = nodePath,
                NodeVersion = nodeFolder,
                Reason = "Task explicitly requests Node10 (EOL, policy disabled)",
                Warning = "Node10 has reached End-of-Life. Please upgrade to Node20 or Node24."
            };
        }

        /// <summary>
        /// Gets the appropriate path based on environment (host vs container).
        /// ⭐ THIS IS THE KEY METHOD - Shows host/container distinction ⭐
        /// </summary>
        private string GetNodePathForEnvironment(UnifiedNodeContext context, string nodeFolder)
        {
            // Build HOST path first (always same logic)
            string externalsPath = context.HostContext.GetDirectory(WellKnownDirectory.Externals);
            string hostNodePath = Path.Combine(externalsPath, nodeFolder, "bin", $"node{IOUtil.ExeExtension}");

            // ⭐ HOST vs CONTAINER DISTINCTION HERE ⭐
            if (context.IsContainer && context.Container != null)
            {
                // CONTAINER: Translate host path to container path
                // Example: C:\agent\_work\_tool\node10\bin\node.exe → /azp/node10/bin/node
                string containerPath = context.Container.TranslateToContainerPath(hostNodePath);
                context.ExecutionContext.Debug($"[Node10Strategy] Container path translation: {hostNodePath} → {containerPath}");
                return containerPath;
            }
            else
            {
                // HOST: Return host path directly
                context.ExecutionContext.Debug($"[Node10Strategy] Using host path: {hostNodePath}");
                return hostNodePath;
            }
        }
    }
}
