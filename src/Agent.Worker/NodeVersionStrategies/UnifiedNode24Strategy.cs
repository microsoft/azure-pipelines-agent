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

            if (useNode24Globally)
            {
                context.ExecutionContext.Debug("[Node24Strategy] AGENT_USE_NODE24=true → Global override");
                return DetermineNodeVersionAndSetContext(context, eolPolicyEnabled, "Selected via global AGENT_USE_NODE24 override");
            }
            
            if (hasNode24Handler)
            {
                if (useNode24WithHandlerData)
                {
                    return DetermineNodeVersionAndSetContext(context, eolPolicyEnabled, "Selected for Node24 task with handler knob enabled");
                }
                else
                {
                    context.SelectedNodeVersion = "node20_1";
                    context.SelectionReason = "Node24 task detected but handler knob disabled, falling back to Node20";
                    context.SelectionWarning = null;
                    return true;
                }
            }
            
            if (eolPolicyEnabled)
            {
                return DetermineNodeVersionAndSetContext(context, eolPolicyEnabled, "Upgraded from end-of-life Node version due to EOL policy");
            }
            
            return false;
        }

        private bool DetermineNodeVersionAndSetContext(UnifiedNodeContext context, bool eolPolicyEnabled, string baseReason)
        {
            string systemType = context.IsContainer ? "container" : "agent";
            
            if (!context.Node24HasGlibcError)
            {
                context.SelectedNodeVersion = "node24";
                context.SelectionReason = baseReason;
                context.SelectionWarning = null;
                return true;
            }

            if (!context.Node20HasGlibcError)
            {
                context.SelectedNodeVersion = "node20_1";
                context.SelectionReason = $"{baseReason}, fallback to Node20 due to Node24 glibc compatibility issue";
                context.SelectionWarning = StringUtil.Loc("NodeGlibcFallbackWarning", systemType, "Node24", "Node20");
                return true;
            }

            if (eolPolicyEnabled)
            {
                context.ExecutionContext.Debug("[Node24Strategy] Would need Node16 but EOL policy being enabled this is not supported");
                string handlerType = context.HandlerData != null ? context.HandlerData.GetType().Name : "UnknownHandlerData";
                throw new NotSupportedException($"No compatible Node.js version available for host execution. Handler type: {handlerType}. This may occur if all available versions are blocked by EOL policy. Please update your pipeline to use Node20 or Node24 tasks. To temporarily disable EOL policy: Set AGENT_ENABLE_EOL_NODE_VERSION_POLICY=false");
            }

            context.SelectedNodeVersion = "node16";
            context.SelectionReason = $"{baseReason}, fallback to Node16 due to both Node24 and Node20 glibc compatibility issues";
            context.SelectionWarning = StringUtil.Loc("NodeGlibcFallbackWarning", systemType, "Node24 or Node20", "Node16");
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
