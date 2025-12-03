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
    public sealed class UnifiedNode16Strategy : IUnifiedNodeVersionStrategy
    {
        public string Name => "Node16";

        public bool CanHandle(UnifiedNodeContext context)
        {
            bool hasNode16Handler = context.HandlerData is Node16HandlerData;
            bool eolPolicyEnabled = AgentKnobs.EnableEOLNodeVersionPolicy.GetValue(context.ExecutionContext).AsBoolean();

            // ═══════════════════════════════════════════════════════════════════
            // RULE 1: Handler ownership (we always handle Node16HandlerData)
            // ═══════════════════════════════════════════════════════════════════
            if (hasNode16Handler)
            {
                context.ExecutionContext.Debug("[Node16Strategy] Node16HandlerData → Check EOL policy");
                return DetermineNodeVersionAndSetContext(context, eolPolicyEnabled, "Node16 handler");
            }

            // Cannot handle other handler types
            context.ExecutionContext.Debug("[Node16Strategy] Not Node16HandlerData → Cannot handle");
            return false;
        }

        /// <summary>
        /// Determine the actual node version to use and set context properties.
        /// Node16 is EOL, so we check EOL policy and either allow Node16 or throw exception.
        /// </summary>
        private bool DetermineNodeVersionAndSetContext(UnifiedNodeContext context, bool eolPolicyEnabled, string baseReason)
        {
            if (eolPolicyEnabled)
            {
                context.ExecutionContext.Debug("[Node16Strategy] Node16 is EOL and policy enabled → Throw exception");
                throw new NotSupportedException(StringUtil.Loc("NodeEOLPolicyBlocked", "Node16"));
            }

            // EOL policy disabled, allow Node16
            context.SelectedNodeVersion = "node16";
            context.SelectionReason = baseReason;
            context.SelectionWarning = StringUtil.Loc("NodeEOLWarning", "Node16");
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
