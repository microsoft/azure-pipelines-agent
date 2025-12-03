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
                context.ExecutionContext.Debug("[Node10Strategy] Node10HandlerData → Check EOL policy");
                return DetermineNodeVersionAndSetContext(context, eolPolicyEnabled, "Node10 handler");
            }

            // Alpine Fallback (Only if EOL Policy Disabled)
            bool isAlpine = context.IsAlpine;
            if (isAlpine)
            {
                context.ExecutionContext.Warning(
                    "Using Node10 on Alpine Linux because Node6 is not compatible. " +
                    "Node10 has reached End-of-Life. Please upgrade to Node20 or Node24 for continued support.");
                
                context.ExecutionContext.Debug("[Node10Strategy] EnableEOLNodeVersionPolicy=false + IsAlpine=true → Handle (Node6 doesn't work on Alpine)");
                return DetermineNodeVersionAndSetContext(context, eolPolicyEnabled, "Alpine fallback from Node6");
            }

            // Cannot handle other handler types
            context.ExecutionContext.Debug("[Node10Strategy] Not Node10HandlerData → Cannot handle");
            return false;
        }

        /// <summary>
        /// Determine the actual node version to use and set context properties.
        /// Node10 is EOL, so we check EOL policy and either allow Node10 or throw exception.
        /// </summary>
        private bool DetermineNodeVersionAndSetContext(UnifiedNodeContext context, bool eolPolicyEnabled, string baseReason)
        {
            if (eolPolicyEnabled)
            {
                context.ExecutionContext.Debug("[Node10Strategy] Node10 is EOL and policy enabled → Throw exception");
                throw new NotSupportedException(StringUtil.Loc("NodeEOLPolicyBlocked", "Node10"));
            }

            // EOL policy disabled, allow Node10
            context.SelectedNodeVersion = "node10";
            context.SelectionReason = baseReason;
            context.SelectionWarning = StringUtil.Loc("NodeEOLWarning", "Node10");
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
