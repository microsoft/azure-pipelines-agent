// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Agent.Sdk;
using Agent.Sdk.Knob;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker.Handlers;

namespace Microsoft.VisualStudio.Services.Agent.Worker.NodeVersionStrategies
{
    public sealed class Node10Strategy : INodeVersionStrategy
    {
        private readonly INodeHandlerHelper _nodeHandlerHelper;

        public Node10Strategy() : this(new NodeHandlerHelper())
        {
        }

        public Node10Strategy(INodeHandlerHelper nodeHandlerHelper)
        {
            _nodeHandlerHelper = nodeHandlerHelper ?? throw new ArgumentNullException(nameof(nodeHandlerHelper));
        }

        public NodeRunnerInfo CanHandle(TaskContext context, IExecutionContext executionContext, GlibcCompatibilityInfo glibcInfo)
        {
            if (context.EffectiveMaxVersion < 10)
            {
                executionContext.Debug($"[Node10Strategy] EffectiveMaxVersion={context.EffectiveMaxVersion} < 10, skipping");
                return null;
            }

            if (AgentKnobs.EnableEOLNodeVersionPolicy.GetValue(executionContext).AsBoolean())
            {
                throw new NotSupportedException(StringUtil.Loc("NodeEOLPolicyBlocked", "Node10"));
            }

            // Legacy runners are only available when installed separately.
            var hostContext = executionContext.GetHostContext();
            string node10Folder = NodeVersionHelper.GetFolderName(NodeVersion.Node10);
            if (!_nodeHandlerHelper.IsNodeFolderExist(node10Folder, hostContext))
            {
                executionContext.Debug("[Node10Strategy] Node10 binary not found on disk, skipping to allow fallback to next strategy");
                return null;
            }

            if (context.HandlerData is Node10HandlerData)
            {
                return new NodeRunnerInfo
                {
                    NodePath = null,
                    NodeVersion = NodeVersion.Node10,
                    Reason = "Selected for Node10 task handler"
                };
            }

            bool isAlpine = PlatformUtil.RunningOnAlpine;
            if (isAlpine)
            {
                return new NodeRunnerInfo
                {
                    NodePath = null,
                    NodeVersion = NodeVersion.Node10,
                    Reason = "Selected for Alpine Linux compatibility (Node6 incompatible)"
                };
            }

            return null;
        }
        
    }
}
