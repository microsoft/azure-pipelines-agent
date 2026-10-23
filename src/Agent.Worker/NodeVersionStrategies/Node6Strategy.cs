// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using Agent.Sdk.Knob;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker;
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
            bool eolPolicyEnabled = AgentKnobs.EnableEOLNodeVersionPolicy.GetValue(executionContext).AsBoolean();
            bool hasNode6Handler = context.HandlerData != null && context.HandlerData.GetType() == typeof(NodeHandlerData);

            string taskName = executionContext.Variables.Get(Constants.Variables.Task.DisplayName) ?? "Unknown Task";

            if (eolPolicyEnabled)
            {
                throw new NotSupportedException(StringUtil.Loc("NodeEOLPolicyBlocked", "Node6"));
            }

            if (hasNode6Handler)
            {
                // Only use Node6 if the binary actually exists on disk
                // (e.g., vsts-agent package includes it; pipelines-agent does not).
                // When absent, return null so the orchestrator falls through to its
                // clean terminal error instead of returning a non-existent node path
                // that fails later at process launch.
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
                    Warning = StringUtil.Loc("NodeEOLRetirementWarning", taskName)
                };
            }

            return null;
        }
    }
}
