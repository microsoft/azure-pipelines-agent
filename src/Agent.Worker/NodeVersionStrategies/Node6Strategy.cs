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

    public sealed class Node6Strategy : INodeVersionStrategy
    {
        public string Name => "Node6";

        public NodeRunnerInfo CanHandle(NodeContext context)
        {
            bool eolPolicyEnabled = AgentKnobs.EnableEOLNodeVersionPolicy.GetValue(context.ExecutionContext).AsBoolean();
            
            bool hasNode6Handler = context.HandlerData != null && context.HandlerData.GetType() == typeof(NodeHandlerData);

            if (hasNode6Handler)
            {
                return DetermineNodeVersionSelection(context, eolPolicyEnabled, "Selected for Node6 task handler");
            }
            
            return null;
        }

        private NodeRunnerInfo DetermineNodeVersionSelection(NodeContext context, bool eolPolicyEnabled, string baseReason)
        {
            if (eolPolicyEnabled)
            {
                throw new NotSupportedException(StringUtil.Loc("NodeEOLPolicyBlocked", "Node6"));
            }

            return new NodeRunnerInfo
            {
                NodePath = null,
                NodeVersion = "node",
                Reason = baseReason,
                Warning = StringUtil.Loc("NodeEOLWarning", "Node6")
            };
        }
    }
}
