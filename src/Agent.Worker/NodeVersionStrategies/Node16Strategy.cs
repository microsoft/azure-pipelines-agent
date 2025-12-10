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
    public sealed class Node16Strategy : INodeVersionStrategy
    {
        public string Name => "Node16";

        public NodeRunnerInfo CanHandle(NodeContext context)
        {
            bool hasNode16Handler = context.HandlerData is Node16HandlerData;
            bool eolPolicyEnabled = AgentKnobs.EnableEOLNodeVersionPolicy.GetValue(context.ExecutionContext).AsBoolean();

            if (hasNode16Handler)
            {
                return DetermineNodeVersionSelection(context, eolPolicyEnabled, "Selected for Node16 task handler");
            }

            return null;
        }

        private NodeRunnerInfo DetermineNodeVersionSelection(NodeContext context, bool eolPolicyEnabled, string baseReason)
        {
            if (eolPolicyEnabled)
            {
                throw new NotSupportedException(StringUtil.Loc("NodeEOLPolicyBlocked", "Node16"));
            }

            return new NodeRunnerInfo
            {
                NodePath = null,
                NodeVersion = "node16",
                Reason = baseReason,
                Warning = StringUtil.Loc("NodeEOLWarning", "Node16")
            };
        }
    }
}
