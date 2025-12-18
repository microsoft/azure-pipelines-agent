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
        public NodeRunnerInfo CanHandle(TaskContext context, IExecutionContext executionContext, GlibcCompatibilityInfo glibcInfo)
        {
            bool hasNode16Handler = context.HandlerData is Node16HandlerData;
            bool eolPolicyEnabled = AgentKnobs.EnableEOLNodeVersionPolicy.GetValue(executionContext).AsBoolean();

            if (hasNode16Handler)
            {
                return DetermineNodeVersionSelection(context, eolPolicyEnabled, "Selected for Node16 task handler");
            }

            return null;
        }

        private NodeRunnerInfo DetermineNodeVersionSelection(TaskContext context, bool eolPolicyEnabled, string baseReason)
        {
            if (eolPolicyEnabled)
            {
                throw new NotSupportedException(StringUtil.Loc("NodeEOLPolicyBlocked", "Node16"));
            }

            return new NodeRunnerInfo
            {
                NodePath = null,
                NodeVersion = NodeVersion.Node16,
                Reason = baseReason,
                Warning = StringUtil.Loc("NodeEOLWarning", "Node16")
            };
        }
    }
}
