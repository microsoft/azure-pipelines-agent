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
    public sealed class Node20Strategy : INodeVersionStrategy
    {
        public NodeRunnerInfo CanHandle(TaskContext context, IExecutionContext executionContext, GlibcCompatibilityInfo glibcInfo)
        {
            bool useNode20Globally = AgentKnobs.UseNode20_1.GetValue(executionContext).AsBoolean();
            bool hasNode20Handler = context.HandlerData is Node20_1HandlerData;
            bool eolPolicyEnabled = AgentKnobs.EnableEOLNodeVersionPolicy.GetValue(executionContext).AsBoolean();
            
            if (useNode20Globally)
            {
                return DetermineNodeVersionSelection(context, eolPolicyEnabled, "Selected via global AGENT_USE_NODE20_1 override", glibcInfo);
            }
            
            if (hasNode20Handler)
            {
                return DetermineNodeVersionSelection(context, eolPolicyEnabled, "Selected for Node20 task handler", glibcInfo);
            }
            
            if (eolPolicyEnabled)
            {
                return DetermineNodeVersionSelection(context, eolPolicyEnabled, "Upgraded from end-of-life Node version due to EOL policy", glibcInfo);
            }
            
            return null;
        }

        private NodeRunnerInfo DetermineNodeVersionSelection(TaskContext context, bool eolPolicyEnabled, string baseReason, GlibcCompatibilityInfo glibcInfo)
        {
            if (!glibcInfo.Node20HasGlibcError)
            {
                return new NodeRunnerInfo
                {
                    NodePath = null,
                    NodeVersion = NodeVersion.Node20,
                    Reason = baseReason,
                    Warning = null
                };
            }

            if (eolPolicyEnabled)
            {
                throw new NotSupportedException(StringUtil.Loc("NodeEOLFallbackBlocked", "Node20", "Node16"));
            }
            
            string systemType = context.Container != null ? "container" : "agent";
            return new NodeRunnerInfo
            {
                NodePath = null,
                NodeVersion = NodeVersion.Node16,
                Reason = $"{baseReason}, fallback to Node16 due to Node20 glibc compatibility issue",
                Warning = StringUtil.Loc("NodeGlibcFallbackWarning", systemType, "Node20", "Node16")
            };
        }
    }
}
