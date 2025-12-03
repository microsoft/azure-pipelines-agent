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
    public sealed class UnifiedNode24Strategy : IUnifiedNodeVersionStrategy
    {
        public string Name => "Node24";

        public bool CanHandle(UnifiedNodeContext context)
        {
            bool useNode24Globally = AgentKnobs.UseNode24.GetValue(context.ExecutionContext).AsBoolean();
            bool hasNode24Handler = context.HandlerData is Node24HandlerData;
            bool useNode24WithHandlerData = AgentKnobs.UseNode24withHandlerData.GetValue(context.ExecutionContext).AsBoolean();
            bool eolPolicyEnabled = AgentKnobs.EnableEOLNodeVersionPolicy.GetValue(context.ExecutionContext).AsBoolean();

            // ═══════════════════════════════════════════════════════════════════
            // RULE 1: Global override (HIGHEST priority)
            // ═══════════════════════════════════════════════════════════════════
            if (useNode24Globally)
            {
                context.ExecutionContext.Debug("[Node24Strategy] AGENT_USE_NODE24=true → Global override");
                return DetermineNodeVersionAndSetContext(context, eolPolicyEnabled, "Global Node24 enabled");
            }
            
            // ═══════════════════════════════════════════════════════════════════
            // RULE 2: Handler data + handler knob
            // ═══════════════════════════════════════════════════════════════════
            if (hasNode24Handler)
            {
                if (useNode24WithHandlerData)
                {
                    context.ExecutionContext.Debug("[Node24Strategy] Node24HandlerData + knob enabled");
                    return DetermineNodeVersionAndSetContext(context, eolPolicyEnabled, "Node24 handler with knob enabled");
                }
                else
                {
                    context.ExecutionContext.Debug("[Node24Strategy] Node24HandlerData + knob disabled → Always handle (will use Node20)");
                    // Special case: Node24HandlerData but knob disabled → use Node20 directly
                    context.SelectedNodeVersion = "node20_1";
                    context.SelectionReason = "Node24 handler with knob disabled → Node20 fallback";
                    context.SelectionWarning = null;
                    return true;
                }
            }
            
            // ═══════════════════════════════════════════════════════════════════
            // RULE 3: EOL policy upgrade (lowest priority)
            // ═══════════════════════════════════════════════════════════════════
            if (eolPolicyEnabled)
            {
                context.ExecutionContext.Debug("[Node24Strategy] EOL policy enabled → Try upgrade");
                return DetermineNodeVersionAndSetContext(context, eolPolicyEnabled, "EOL policy upgrade");
            }
            
            // Cannot handle
            return false;
        }

        /// <summary>
        /// Determine the actual node version to use and set context properties.
        /// This is the single place where node version decision happens.
        /// </summary>
        private bool DetermineNodeVersionAndSetContext(UnifiedNodeContext context, bool eolPolicyEnabled, string baseReason)
        {
            string systemType = context.IsContainer ? "container" : "agent";
            
            // Start with Node24
            if (!context.Node24HasGlibcError)
            {
                // Node24 works fine
                context.SelectedNodeVersion = "node24";
                context.SelectionReason = baseReason;
                context.SelectionWarning = null;
                return true;
            }

            // Node24 has glibc error - try Node20
            if (!context.Node20HasGlibcError)
            {
                // Fallback to Node20
                context.SelectedNodeVersion = "node20_1";
                context.SelectionReason = $"{baseReason} → Node24 glibc error → Node20 fallback";
                context.SelectionWarning = StringUtil.Loc("NodeGlibcFallbackWarning", systemType, "Node24", "Node20");
                return true;
            }

            // Both Node24 and Node20 have glibc errors - would need Node16 (EOL)
            if (eolPolicyEnabled)
            {
                context.ExecutionContext.Debug("[Node24Strategy] Would need Node16 but EOL policy enabled → Throw exception");
                throw new NotSupportedException(StringUtil.Loc("NodeVersionNotAvailable", "Node24HandlerData"));
            }

            // EOL policy disabled, allow Node16
            context.SelectedNodeVersion = "node16";
            context.SelectionReason = $"{baseReason} → Both Node24/Node20 glibc errors → Node16 fallback";
            context.SelectionWarning = StringUtil.Loc("NodeGlibcFallbackWarning", systemType, "Node24 or Node20", "Node16");
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
