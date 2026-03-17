// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading;
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
            bool useNode24WithHandlerData = AgentKnobs.UseNode24withHandlerData.GetValue(executionContext).AsBoolean();
            bool eolPolicyEnabled = AgentKnobs.EnableEOLNodeVersionPolicy.GetValue(executionContext).AsBoolean();
            string node24Folder = NodeVersionHelper.GetFolderName(NodeVersion.Node24);
            string taskName = executionContext.Variables.Get(Constants.Variables.Task.DisplayName) ?? "Unknown Task";

            if (!IsNodeExecutable(node24Folder, executionContext))
            {
                executionContext.Debug("[Node24Strategy] Node24 not executable on this platform (e.g., exit code 216 or node binary missing), checking fallback options");
                return null;
            }

            if (glibcInfo.Node24HasGlibcError)
            {
                executionContext.Debug(StringUtil.Loc("NodeGlibcFallbackWarning", "agent", "Node24", "Node20"));
                return null;
            }

            if (useNode24Globally)
            {
                executionContext.Debug("[Node24Strategy] AGENT_USE_NODE24=true, global override");
                return new NodeRunnerInfo
                {
                    NodePath = null,
                    NodeVersion = NodeVersion.Node24,
                    Reason = "Selected via global AGENT_USE_NODE24 override",
                    Warning = null
                };
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

            if (context.EffectiveMaxVersion < 24)
            {
                executionContext.Debug($"[Node24Strategy] EffectiveMaxVersion={context.EffectiveMaxVersion} < 24, skipping");
                return null;
            }

            if (context.HandlerData is Node24HandlerData)
            {
                if (!useNode24WithHandlerData)
                {
                    executionContext.Debug("[Node24Strategy] Node24 handler detected but UseNode24withHandlerData=false, skipping");
                    return null;
                }

                return new NodeRunnerInfo
                {
                    NodePath = null,
                    NodeVersion = NodeVersion.Node24,
                    Reason = "Selected for Node24 task handler",
                    Warning = null
                };
            }

            return null;
        }

        private bool IsNodeExecutable(string nodeFolder, IExecutionContext executionContext)
        {
            var hostContext = executionContext.GetHostContext();
            if (!_nodeHandlerHelper.IsNodeFolderExist(nodeFolder, hostContext))
            {
                executionContext.Debug($"[Node24Strategy] Node folder does not exist: {nodeFolder}");
                return false;
            }
            var nodePath = Path.Combine(hostContext.GetDirectory(WellKnownDirectory.Externals), nodeFolder, "bin", $"node{IOUtil.ExeExtension}");
            try
            {
                var processInvoker = hostContext.CreateService<IProcessInvoker>();
                var exitCodeTask = processInvoker.ExecuteAsync(
                                        workingDirectory: hostContext.GetDirectory(WellKnownDirectory.Work),
                                        fileName: nodePath,
                                        arguments: "-v",
                                        environment: null,
                                        requireExitCodeZero: false,
                                        outputEncoding: null,
                                        cancellationToken: CancellationToken.None);

                int exitCode = exitCodeTask.GetAwaiter().GetResult();
                return exitCode != 216;
            }
            catch (Exception ex)
            {
                executionContext.Debug($"[Node24Strategy] Node executable test threw exception: {ex.Message}");
                return false;
            }
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