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

    public sealed class UnifiedNode6Strategy : IUnifiedNodeVersionStrategy
    {
        public string Name => "Node6";

        public bool CanHandle(UnifiedNodeContext context)
        {
            bool eolPolicyEnabled = AgentKnobs.EnableEOLNodeVersionPolicy.GetValue(context.ExecutionContext).AsBoolean();
            
            // Note: NodeHandlerData (without version suffix) represents Node6
            bool hasNode6Handler = context.HandlerData != null && context.HandlerData.GetType() == typeof(NodeHandlerData);

            // ═══════════════════════════════════════════════════════════════════
            // RULE 1: Handler ownership (we always handle NodeHandlerData)
            // ═══════════════════════════════════════════════════════════════════
            if (hasNode6Handler)
            {
                context.ExecutionContext.Debug("[Node6Strategy] NodeHandlerData (Node6) → Check EOL policy");
                return DetermineNodeVersionAndSetContext(context, eolPolicyEnabled, "Node6 handler");
            }
            
            // Cannot handle other handler types
            context.ExecutionContext.Debug("[Node6Strategy] Not NodeHandlerData (Node6) → Cannot handle");
            return false;
        }

        /// <summary>
        /// Determine the actual node version to use and set context properties.
        /// Node6 is EOL, so we check EOL policy and either allow Node6 or throw exception.
        /// </summary>
        private bool DetermineNodeVersionAndSetContext(UnifiedNodeContext context, bool eolPolicyEnabled, string baseReason)
        {
            if (eolPolicyEnabled)
            {
                context.ExecutionContext.Debug("[Node6Strategy] Node6 is EOL and policy enabled → Throw exception");
                throw new NotSupportedException(StringUtil.Loc("NodeEOLPolicyBlocked", "Node6"));
            }

            // EOL policy disabled, allow Node6
            // Note: Node6 folder is named "node" (without version suffix)
            context.SelectedNodeVersion = "node";
            context.SelectionReason = baseReason;
            context.SelectionWarning = StringUtil.Loc("NodeEOLWarning", "Node6");
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
