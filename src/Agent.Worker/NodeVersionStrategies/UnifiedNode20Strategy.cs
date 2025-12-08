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

            if (useNode20Globally)
            {
                return DetermineNodeVersionAndSetContext(context, eolPolicyEnabled, "Selected via global AGENT_USE_NODE20_1 override");
            }
            
            if (hasNode20Handler)
            {
                return DetermineNodeVersionAndSetContext(context, eolPolicyEnabled, "Selected for Node20 task handler");
            }
            
            if (eolPolicyEnabled)
            {
                return DetermineNodeVersionAndSetContext(context, eolPolicyEnabled, "Upgraded from end-of-life Node version due to EOL policy");
            }
            
            return false;
        }

        private bool DetermineNodeVersionAndSetContext(UnifiedNodeContext context, bool eolPolicyEnabled, string baseReason)
        {
            if (!context.Node20HasGlibcError)
            {
                context.SelectedNodeVersion = "node20_1";
                context.SelectionReason = baseReason;
                context.SelectionWarning = null;
                return true;
            }

            if (eolPolicyEnabled)
            {
                throw new NotSupportedException(StringUtil.Loc("NodeEOLFallbackBlocked", "Node20", "Node16"));
            }

            string systemType = context.IsContainer ? "container" : "agent";
            context.SelectedNodeVersion = "node16";
            context.SelectionReason = $"{baseReason}, fallback to Node16 due to Node20 glibc compatibility issue";
            context.SelectionWarning = StringUtil.Loc("NodeGlibcFallbackWarning", systemType, "Node20", "Node16");
            return true;
        }

        public NodePathResult GetNodePath(UnifiedNodeContext context)
        {
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
