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
                return DetermineNodeVersionAndSetContext(context, eolPolicyEnabled, "Global Node20 enabled");
            }
            
            // RULE 2: Handler data check
            if (hasNode20Handler)
            {
                context.ExecutionContext.Debug("[Node20Strategy] Node20_1HandlerData → Handle with glibc validation");
                return DetermineNodeVersionAndSetContext(context, eolPolicyEnabled, "Node20 handler");
            }
            
            // RULE 3: EOL policy upgrade (only for EOL handlers)
            if (eolPolicyEnabled)
            {
                context.ExecutionContext.Debug("[Node20Strategy] EOL policy enabled + EOL handler → Try upgrade");
                return DetermineNodeVersionAndSetContext(context, eolPolicyEnabled, "EOL policy upgrade");
            }
            
            // RULE 4: Cannot handle
            context.ExecutionContext.Debug("[Node20Strategy] Cannot handle → Continue to next strategy");
            return false;
        }

        /// <summary>
        /// Determine the actual node version to use and set context properties.
        /// This is the single place where node version decision happens.
        /// </summary>
        private bool DetermineNodeVersionAndSetContext(UnifiedNodeContext context, bool eolPolicyEnabled, string baseReason)
        {
            // Start with Node20
            if (!context.Node20HasGlibcError)
            {
                // Node20 works fine
                context.SelectedNodeVersion = "node20_1";
                context.SelectionReason = baseReason;
                context.SelectionWarning = null;
                return true;
            }

            // Node20 has glibc error - would need Node16 (EOL)
            if (eolPolicyEnabled)
            {
                context.ExecutionContext.Debug("[Node20Strategy] Would need Node16 but EOL policy enabled → Throw exception");
                throw new NotSupportedException(StringUtil.Loc("NodeEOLFallbackBlocked", "Node20", "Node16"));
            }

            // EOL policy disabled, allow Node16 fallback
            string systemType = context.IsContainer ? "container" : "agent";
            context.SelectedNodeVersion = "node16";
            context.SelectionReason = $"{baseReason} → Node20 glibc error → Node16 fallback";
            context.SelectionWarning = StringUtil.Loc("NodeGlibcFallbackWarning", systemType, "Node20", "Node16");
            return true;
        }

        /// <summary>
        /// Build node path using the decision made in CanHandle().
        /// No complex logic here - just path building.
        /// </summary>
        public NodePathResult GetNodePath(UnifiedNodeContext context)
        {
            // All decisions already made in CanHandle() - just build the path
            string externalsPath = context.HostContext.GetDirectory(WellKnownDirectory.Externals);
            string hostPath = Path.Combine(externalsPath, context.SelectedNodeVersion, "bin", $"node{IOUtil.ExeExtension}");
            string finalPath = context.IsContainer && context.Container != null ? 
                              context.Container.TranslateToContainerPath(hostPath) : hostPath;

            return new NodePathResult
            {
                NodePath = finalPath,
                NodeVersion = context.SelectedNodeVersion,
                Reason = context.SelectionReason,
                Warning = context.SelectionWarning
            };
        }
    }
}
