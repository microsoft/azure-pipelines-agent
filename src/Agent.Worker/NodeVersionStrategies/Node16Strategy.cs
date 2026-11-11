// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Agent.Sdk.Knob;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker.Container;
using Microsoft.VisualStudio.Services.Agent.Worker.Handlers;

namespace Microsoft.VisualStudio.Services.Agent.Worker.NodeVersionStrategies
{
    public sealed class Node16Strategy : INodeVersionStrategy
    {
        private readonly INodeHandlerHelper _nodeHandlerHelper;

        public Node16Strategy() : this(new NodeHandlerHelper())
        {
        }

        public Node16Strategy(INodeHandlerHelper nodeHandlerHelper)
        {
            _nodeHandlerHelper = nodeHandlerHelper ?? throw new ArgumentNullException(nameof(nodeHandlerHelper));
        }

        public NodeRunnerInfo CanHandle(TaskContext context, IExecutionContext executionContext, GlibcCompatibilityInfo glibcInfo)
        {
            if (context.EffectiveMaxVersion < 16)
            {
                executionContext.Debug($"[Node16Strategy] EffectiveMaxVersion={context.EffectiveMaxVersion} < 16, skipping");
                return null;
            }

            if (AgentKnobs.EnableEOLNodeVersionPolicy.GetValue(executionContext).AsBoolean())
            {
                executionContext.Debug(StringUtil.Loc("NodeEOLFallbackBlocked", "Node16"));
                throw new NotSupportedException(StringUtil.Loc("NodeEOLPolicyBlocked", "Node16"));
            }

            // Legacy runners are only available when installed separately.
            var hostContext = executionContext.GetHostContext();
            string node16Folder = NodeVersionHelper.GetFolderName(NodeVersion.Node16);
            if (!_nodeHandlerHelper.IsNodeFolderExist(node16Folder, hostContext))
            {
                executionContext.Debug("[Node16Strategy] Node16 binary not found on disk, skipping to allow fallback to next strategy");
                return null;
            }

            if (context.HandlerData is Node16HandlerData)
            {
                return new NodeRunnerInfo
                {
                    NodePath = null,
                    NodeVersion = NodeVersion.Node16,
                    Reason = "Selected for Node16 task handler"
                };
            }

            return new NodeRunnerInfo
            {
                NodePath = null,
                NodeVersion = NodeVersion.Node16,
                Reason = "Fallback to manually installed Node16"
            };
        }

        public NodeRunnerInfo CanHandleInContainer(TaskContext context, IExecutionContext executionContext, IDockerCommandManager dockerManager)
        {
            if (context.Container == null)
            {
                executionContext.Debug("[Node16Strategy] CanHandleInContainer called but no container context provided");
                return null;
            }

            if (AgentKnobs.EnableEOLNodeVersionPolicy.GetValue(executionContext).AsBoolean())
            {
                throw new NotSupportedException("No compatible Node.js version available for container execution. Node16 is blocked by EOL policy. Please update your pipeline to use Node20 or Node24 tasks.");
            }

            // Only use Node16 if the binary actually exists on disk
            var hostContext = executionContext.GetHostContext();
            string node16Folder = NodeVersionHelper.GetFolderName(NodeVersion.Node16);
            if (!_nodeHandlerHelper.IsNodeFolderExist(node16Folder, hostContext))
            {
                executionContext.Debug("[Node16Strategy] Node16 binary not found on disk, skipping container fallback");
                return null;
            }

            executionContext.Debug("[Node16Strategy] Providing Node16 as final fallback for container");

            return new NodeRunnerInfo
            {
                NodePath = null,
                NodeVersion = NodeVersion.Node16,
                Reason = "Fallback to manually installed Node16 for container execution"
            };
        }
    }
}