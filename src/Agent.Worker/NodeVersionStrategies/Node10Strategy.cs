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
            bool eolPolicyEnabled = AgentKnobs.EnableEOLNodeVersionPolicy.GetValue(executionContext).AsBoolean();
            string taskName = executionContext.Variables.Get(Constants.Variables.Task.DisplayName) ?? "Unknown Task";

            if (context.EffectiveMaxVersion < 10)
            {
                executionContext.Debug($"[Node10Strategy] EffectiveMaxVersion={context.EffectiveMaxVersion} < 10, skipping");
                return null;
            }

            if (eolPolicyEnabled)
            {
                throw new NotSupportedException(StringUtil.Loc("NodeEOLPolicyBlocked", "Node10"));
            }

            // Only use Node10 if the binary actually exists on disk
            // (e.g., vsts-agent package includes it; pipelines-agent does not).
            // When absent, return null so the orchestrator falls through to its
            // clean terminal error instead of returning a non-existent node path
            // that fails later at process launch.
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
                    Reason = "Selected for Node10 task handler",
                    Warning = StringUtil.Loc("NodeEOLRetirementWarning", taskName)
                };
            }

            bool isAlpine = PlatformUtil.RunningOnAlpine;
            if (isAlpine)
            {
                executionContext.Warning(
                    "Using Node10 on Alpine Linux because Node6 is not compatible. " +
                    "Node10 has reached End-of-Life. Please upgrade to Node20 or Node24 for continued support.");

                return new NodeRunnerInfo
                {
                    NodePath = null,
                    NodeVersion = NodeVersion.Node10,
                    Reason = "Selected for Alpine Linux compatibility (Node6 incompatible)",
                    Warning = StringUtil.Loc("NodeEOLRetirementWarning", taskName)
                };
            }

            return null;
        }
        
    }
}
