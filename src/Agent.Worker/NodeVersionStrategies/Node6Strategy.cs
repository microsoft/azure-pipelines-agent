// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Agent.Sdk.Knob;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker.Handlers;

namespace Microsoft.VisualStudio.Services.Agent.Worker.NodeVersionStrategies
{
    public sealed class Node6Strategy : INodeVersionStrategy
    {
        private readonly INodeHandlerHelper _nodeHandlerHelper;

        public Node6Strategy() : this(new NodeHandlerHelper())
        {
        }

        public Node6Strategy(INodeHandlerHelper nodeHandlerHelper)
        {
            _nodeHandlerHelper = nodeHandlerHelper ?? throw new ArgumentNullException(nameof(nodeHandlerHelper));
        }

        public NodeRunnerInfo CanHandle(TaskContext context, IExecutionContext executionContext, GlibcCompatibilityInfo glibcInfo)
        {
            if (AgentKnobs.EnableEOLNodeVersionPolicy.GetValue(executionContext).AsBoolean())
            {
                throw new NotSupportedException(StringUtil.Loc("NodeEOLPolicyBlocked", "Node6"));
            }

            bool hasNode6Handler = context.HandlerData != null && context.HandlerData.GetType() == typeof(NodeHandlerData);

            if (hasNode6Handler)
            {
                // Legacy runners are only available when installed separately.
                var hostContext = executionContext.GetHostContext();
                string node6Folder = NodeVersionHelper.GetFolderName(NodeVersion.Node6);
                if (!_nodeHandlerHelper.IsNodeFolderExist(node6Folder, hostContext))
                {
                    executionContext.Debug("[Node6Strategy] Node6 binary not found on disk, skipping to allow fallback to next strategy");
                    return null;
                }

                return new NodeRunnerInfo
                {
                    NodePath = null,
                    NodeVersion = NodeVersion.Node6,
                    Reason = "Selected for Node6 task handler",
                    Warning = StringUtil.Loc(
                        "NodeEOLRetirementWarning",
                        executionContext.Variables.Get(Constants.Variables.Task.DisplayName) ?? "Unknown Task")
                };
            }

            return null;
        }
    }
}
