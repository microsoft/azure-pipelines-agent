// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using Agent.Sdk;
using Agent.Sdk.Knob;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.Container;
using Microsoft.VisualStudio.Services.Agent.Worker.Handlers;

namespace Microsoft.VisualStudio.Services.Agent.Worker.NodeVersionStrategies
{
    public sealed class Node24Strategy : INodeVersionStrategy
    {
        private readonly INodeHandlerHelper _nodeHandlerHelper;

        public Node24Strategy(INodeHandlerHelper nodeHandlerHelper)
        {
            _nodeHandlerHelper = nodeHandlerHelper ?? throw new ArgumentNullException(nameof(nodeHandlerHelper));
        }

        public NodeRunnerInfo CanHandle(TaskContext context, IExecutionContext executionContext, GlibcCompatibilityInfo glibcInfo)
        {
            bool useNode24Globally = AgentKnobs.UseNode24.GetValue(executionContext).AsBoolean();
            bool hasNode24Handler = context.HandlerData is Node24HandlerData;
            bool useNode24WithHandlerData = AgentKnobs.UseNode24withHandlerData.GetValue(executionContext).AsBoolean();
            bool eolPolicyEnabled = AgentKnobs.EnableEOLNodeVersionPolicy.GetValue(executionContext).AsBoolean();

            var hostContext = executionContext.GetHostContext();
            string node24Folder = NodeVersionHelper.GetFolderName(NodeVersion.Node24);

            if (!_nodeHandlerHelper.IsNodeFolderExist(node24Folder, hostContext))
            {
                executionContext.Debug("[Node24Strategy] Node24 binary not available on this platform, skipping");
                return null;
            }

            if (glibcInfo.Node24HasGlibcError)
            {
                executionContext.Debug(StringUtil.Loc("NodeGlibcFallbackWarning", "agent", "Node24", "Node20"));
                return null;
            }

            string taskName = executionContext.Variables.Get(Constants.Variables.Task.DisplayName) ?? "Unknown Task";

            if (useNode24Globally)
            {
                executionContext.Debug("[Node24Strategy] AGENT_USE_NODE24=true → Global override");
                return new NodeRunnerInfo
                {
                    NodePath = null,
                    NodeVersion = NodeVersion.Node24,
                    Reason = "Selected via global AGENT_USE_NODE24 override",
                    Warning = null
                };
            }

            if (hasNode24Handler)
            {
                if (useNode24WithHandlerData)
                {
                    return new NodeRunnerInfo
                    {
                        NodePath = null,
                        NodeVersion = NodeVersion.Node24,
                        Reason = "Selected for Node24 task with handler knob enabled",
                        Warning = null
                    };
                }
                else
                {
                    executionContext.Debug("[Node24Strategy] Node24 task detected but handler knob disabled, skipping to allow fallback");
                    return null;
                }
            }

            if (eolPolicyEnabled)
            {
                return new NodeRunnerInfo
                {
                    NodePath = null,
                    NodeVersion = NodeVersion.Node24,
                    Reason = "Upgraded from end-of-life Node version due to EOL policy",
                    Warning = StringUtil.Loc("NodeEOLUpgradeWarning", taskName)
                };
            }

            return null;
        }

        public NodeRunnerInfo CanHandleInContainer(TaskContext context, IExecutionContext executionContext, IDockerCommandManager dockerManager)
        {
            if (context.Container == null)
            {
                executionContext.Debug("[Node24Strategy] CanHandleInContainer called but no container context provided");
                return null;
            }

            bool useNode24ToStartContainer = AgentKnobs.UseNode24ToStartContainer.GetValue(executionContext).AsBoolean();
            
            if (!useNode24ToStartContainer)
            {
                executionContext.Debug("[Node24Strategy] UseNode24ToStartContainer=false, cannot handle container");
                return null;
            }

            executionContext.Debug("[Node24Strategy] UseNode24ToStartContainer=true, checking Node24 availability in container");

            try
            {
                if (NodeContainerTestHelper.CanExecuteNodeInContainer(context, executionContext, dockerManager, NodeVersion.Node24, "Node24Strategy"))
                {
                    return new NodeRunnerInfo
                    {
                        NodePath = null,
                        NodeVersion = NodeVersion.Node24,
                        Reason = "Node24 available in container via UseNode24ToStartContainer knob",
                        Warning = null
                    };
                }
                else
                {
                    executionContext.Debug("[Node24Strategy] Node24 test failed in container, returning null for fallback");
                    return null;
                }
            }
            catch (Exception ex)
            {
                executionContext.Warning($"[Node24Strategy] Failed to test Node24 in container: {ex.Message}");
                return null;
            }
        }
    }
}