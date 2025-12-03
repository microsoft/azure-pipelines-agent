// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using Agent.Sdk.Knob;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Microsoft.VisualStudio.Services.Agent.Worker.NodeVersionStrategies
{
    public sealed class UnifiedNode20Strategy : IUnifiedNodeVersionStrategy
    {
        public string Name => "Node20";

        public bool CanHandle(UnifiedNodeContext context)
        {
            bool useNode20Globally = AgentKnobs.UseNode20_1.GetValue(context.ExecutionContext).AsBoolean();
            bool hasNode20Handler = context.HandlerData is Node20_1HandlerData;
            bool eolPolicyEnabled = AgentKnobs.EnableEOLNodeVersionPolicy.GetValue(context.ExecutionContext).AsBoolean();

            // RULE 1: Global override (HIGHEST priority)
            if (useNode20Globally)
            {
                context.ExecutionContext.Debug("[Node20Strategy] AGENT_USE_NODE20_1=true → Global override");
                return CanUseNodeVersion(context, eolPolicyEnabled);
            }
            
            // RULE 2: Handler data check
            if (hasNode20Handler)
            {
                context.ExecutionContext.Debug("[Node20Strategy] Node20_1HandlerData → Handle with glibc validation");
                return CanUseNodeVersion(context, eolPolicyEnabled);
            }
            
            // RULE 3: EOL policy upgrade (only for EOL handlers)
            if (eolPolicyEnabled)
            {
                context.ExecutionContext.Debug("[Node20Strategy] EOL policy enabled + EOL handler → Try upgrade");
                return CanUseNodeVersion(context, eolPolicyEnabled);
            }
            
            // RULE 4: Cannot handle
            context.ExecutionContext.Debug("[Node20Strategy] Cannot handle → Continue to next strategy");
            return false;
        }

        private bool CanUseNodeVersion(UnifiedNodeContext context, bool eolPolicyEnabled)
        {
            // If Node20 works, we're good
            if (!context.Node20HasGlibcError)
                return true;

            // Node20 has glibc error - would need Node16 (EOL)
            if (eolPolicyEnabled)
            {
                context.ExecutionContext.Debug("[Node20Strategy] Would need Node16 but EOL policy enabled → Throw exception");
                throw new NotSupportedException("would fallback to Node16 (EOL) but EOL policy is enabled");
            }

            return true; // EOL policy disabled, allow Node16 fallback
        }

        /// <summary>
        /// Gets the Node20 path with glibc fallback.
        /// ⭐ Simple path building - ALL validation already done in CanHandle ⭐
        /// </summary>
        public NodePathResult GetNodePath(UnifiedNodeContext context)
        {
            bool useNode20Globally = AgentKnobs.UseNode20_1.GetValue(context.ExecutionContext).AsBoolean();
            bool hasNode20Handler = context.HandlerData is Node20_1HandlerData;
            bool eolPolicyEnabled = AgentKnobs.EnableEOLNodeVersionPolicy.GetValue(context.ExecutionContext).AsBoolean();

            string nodeFolder;
            string warning = null;
            string reason;

            // Determine node version based on conditions (no validation - already done in CanHandle)
            if (context.Node20HasGlibcError)
            {
                // Node20 has glibc error - fallback to Node16
                // Note: EOL policy validation already done in CanHandle, so we can safely use Node16 here
                nodeFolder = "node16";
                string systemType = context.IsContainer ? "container" : "agent";
                warning = $"The {systemType} operating system doesn't support Node20. Using Node16 instead. " +
                         "Please upgrade the operating system to remain compatible with future updates.";
                reason = "Node20 glibc error → Node16 fallback";
            }
            else
            {
                // Normal Node20 usage
                nodeFolder = "node20_1";
                if (useNode20Globally)
                {
                    reason = "Global AGENT_USE_NODE20_1=true";
                }
                else if (hasNode20Handler)
                {
                    reason = "Node20 handler ownership";
                }
                else
                {
                    reason = "EOL policy upgrade to Node20";
                }
            }

            // Build path
            string externalsPath = context.HostContext.GetDirectory(WellKnownDirectory.Externals);
            string hostPath = Path.Combine(externalsPath, nodeFolder, "bin", $"node{IOUtil.ExeExtension}");
            string finalPath = context.IsContainer && context.Container != null ? 
                              context.Container.TranslateToContainerPath(hostPath) : hostPath;

            return new NodePathResult
            {
                NodePath = finalPath,
                NodeVersion = nodeFolder,
                Reason = reason,
                Warning = warning
            };
        }
    }
}
