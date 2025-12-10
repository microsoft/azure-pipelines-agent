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
    public sealed class Node24Strategy : INodeVersionStrategy
    {
        public string Name => "Node24";

        public NodeRunnerInfo CanHandle(NodeContext context)
        {
            bool useNode24Globally = AgentKnobs.UseNode24.GetValue(context.ExecutionContext).AsBoolean();
            bool hasNode24Handler = context.HandlerData is Node24HandlerData;
            bool useNode24WithHandlerData = AgentKnobs.UseNode24withHandlerData.GetValue(context.ExecutionContext).AsBoolean();
            bool eolPolicyEnabled = AgentKnobs.EnableEOLNodeVersionPolicy.GetValue(context.ExecutionContext).AsBoolean();

            if (useNode24Globally)
            {
                context.ExecutionContext.Debug("[Node24Strategy] AGENT_USE_NODE24=true → Global override");
                return DetermineNodeVersionSelection(context, eolPolicyEnabled, "Selected via global AGENT_USE_NODE24 override");
            }
            
            if (hasNode24Handler)
            {
                if (useNode24WithHandlerData)
                {
                    return DetermineNodeVersionSelection(context, eolPolicyEnabled, "Selected for Node24 task with handler knob enabled");
                }
                else
                {
                    return new NodeRunnerInfo
                    {
                        NodePath = null,
                        NodeVersion = "node20_1",
                        Reason = "Node24 task detected but handler knob disabled, falling back to Node20",
                        Warning = null
                    };
                }
            }
            
            if (eolPolicyEnabled)
            {
                return DetermineNodeVersionSelection(context, eolPolicyEnabled, "Upgraded from end-of-life Node version due to EOL policy");
            }
            
            return null;
        }

        private NodeRunnerInfo DetermineNodeVersionSelection(NodeContext context, bool eolPolicyEnabled, string baseReason)
        {
            string systemType = context.IsContainer ? "container" : "agent";
            
            if (!context.Node24HasGlibcError)
            {
                return new NodeRunnerInfo
                {
                    NodePath = null,
                    NodeVersion = "node24",
                    Reason = baseReason,
                    Warning = null
                };
            }

            if (!context.Node20HasGlibcError)
            {
                return new NodeRunnerInfo
                {
                    NodePath = null,
                    NodeVersion = "node20_1",
                    Reason = $"{baseReason}, fallback to Node20 due to Node24 glibc compatibility issue",
                    Warning = StringUtil.Loc("NodeGlibcFallbackWarning", systemType, "Node24", "Node20")
                };
            }

            if (eolPolicyEnabled)
            {
                context.ExecutionContext.Debug("[Node24Strategy] Would need Node16 but EOL policy being enabled this is not supported");
                string handlerType = context.HandlerData != null ? context.HandlerData.GetType().Name : "UnknownHandlerData";
                throw new NotSupportedException($"No compatible Node.js version available for host execution. Handler type: {handlerType}. This may occur if all available versions are blocked by EOL policy. Please update your pipeline to use Node20 or Node24 tasks. To temporarily disable EOL policy: Set AGENT_ENABLE_EOL_NODE_VERSION_POLICY=false");
            }

            return new NodeRunnerInfo
            {
                NodePath = null,
                NodeVersion = "node16",
                Reason = $"{baseReason}, fallback to Node16 due to both Node24 and Node20 glibc compatibility issues",
                Warning = StringUtil.Loc("NodeGlibcFallbackWarning", systemType, "Node24 or Node20", "Node16")
            };
        }
    }
}