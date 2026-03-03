// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using Agent.Sdk.Knob;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker;

namespace Microsoft.VisualStudio.Services.Agent.Worker.NodeVersionStrategies
{
    public sealed class Node6Strategy : INodeVersionStrategy
    {
        public NodeRunnerInfo CanHandle(TaskContext context, IExecutionContext executionContext, GlibcCompatibilityInfo glibcInfo)
        {
            bool eolPolicyEnabled = AgentKnobs.EnableEOLNodeVersionPolicy.GetValue(executionContext).AsBoolean();
            
            bool hasNode6Handler = context.HandlerData != null && context.HandlerData.GetType() == typeof(NodeHandlerData);

            if (hasNode6Handler)
            {
                return DetermineNodeVersionSelection(context, eolPolicyEnabled, "Selected for Node6 task handler", executionContext);
            }
            
            return null;
        }

        private NodeRunnerInfo DetermineNodeVersionSelection(TaskContext context, bool eolPolicyEnabled, string baseReason, IExecutionContext executionContext)
        {
            string taskName = executionContext.Variables.Get(Constants.Variables.Task.DisplayName) ?? "Unknown Task";
            if (eolPolicyEnabled)
            {
                throw new NotSupportedException(StringUtil.Loc("NodeEOLPolicyBlocked", "Node6"));
            }
            
            return new NodeRunnerInfo
            {
                NodePath = null,
                NodeVersion = NodeVersion.Node6,
                Reason = baseReason,
                Warning = StringUtil.Loc("NodeEOLRetirementWarning", taskName)
            };
        }
    }
}
