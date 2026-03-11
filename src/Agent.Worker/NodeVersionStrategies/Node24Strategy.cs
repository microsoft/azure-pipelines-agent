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
            bool hasNode24Handler = context.HandlerData is Node24HandlerData;
            bool useNode24WithHandlerData = AgentKnobs.UseNode24withHandlerData.GetValue(executionContext).AsBoolean();
            bool eolPolicyEnabled = AgentKnobs.EnableEOLNodeVersionPolicy.GetValue(executionContext).AsBoolean();
            string node24Folder = NodeVersionHelper.GetFolderName(NodeVersion.Node24);

            if (!IsNodeExecutable(node24Folder, executionContext))
            {
                executionContext.Debug("[Node24Strategy] Node24 not executable on this platform (e.g., exit code 216 or node binary missing), checking fallback options");
                return null;
            }

            if (useNode24Globally)
            {
                executionContext.Debug("[Node24Strategy] AGENT_USE_NODE24=true → Global override");
                return DetermineNodeVersionSelection(context, eolPolicyEnabled, "Selected via global AGENT_USE_NODE24 override", executionContext, glibcInfo, skipNode24Check: false);
            }

            if (hasNode24Handler)
            {
                if (useNode24WithHandlerData)
                {
                    return DetermineNodeVersionSelection(context, eolPolicyEnabled, "Selected for Node24 task with handler knob enabled", executionContext, glibcInfo, skipNode24Check: false);
                }
                else
                {
                    return new NodeRunnerInfo
                    {
                        NodePath = null,
                        NodeVersion = NodeVersion.Node20,
                        Reason = "Node24 task detected but handler knob disabled, falling back to Node20",
                        Warning = null
                    };
                }
            }

            if (eolPolicyEnabled)
            {
                return DetermineNodeVersionSelection(context, eolPolicyEnabled, "Upgraded from end-of-life Node version due to EOL policy", executionContext, glibcInfo, skipNode24Check: false, isUpgradeScenario: true);
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

        /// <summary>
        /// Determines the appropriate Node version based on glibc compatibility and EOL policy.
        /// </summary>
        /// <param name="skipNode24Check">When true, skips checking Node24 glibc (used when Node24 folder doesn't exist)</param>
        private NodeRunnerInfo DetermineNodeVersionSelection(TaskContext context, bool eolPolicyEnabled, string baseReason, IExecutionContext executionContext, GlibcCompatibilityInfo glibcInfo, bool skipNode24Check, bool isUpgradeScenario = false)
        {
            string systemType = context.Container != null ? "container" : "agent";
            string taskName = executionContext.Variables.Get(Constants.Variables.Task.DisplayName) ?? "Unknown Task";
            string upgradeWarning = isUpgradeScenario ? StringUtil.Loc("NodeEOLUpgradeWarning", taskName) : null;

            if (!skipNode24Check && !glibcInfo.Node24HasGlibcError)
            {
                return new NodeRunnerInfo
                {
                    NodePath = null,
                    NodeVersion = NodeVersion.Node24,
                    Reason = baseReason,
                    Warning = upgradeWarning
                };
            }

            if (!glibcInfo.Node20HasGlibcError)
            {
                string fallbackReason = skipNode24Check 
                    ? $"{baseReason}, fallback to Node20 because Node24 is not available"
                    : $"{baseReason}, fallback to Node20 due to Node24 glibc compatibility issue";

                return new NodeRunnerInfo
                {
                    NodePath = null,
                    NodeVersion = NodeVersion.Node20,
                    Reason = fallbackReason,
                    Warning = upgradeWarning ?? StringUtil.Loc("NodeGlibcFallbackWarning", systemType, "Node24", "Node20")
                };
            }

            executionContext.Debug("[Node24Strategy] Node20 has glibc error, checking EOL policy for Node16 fallback");

            if (eolPolicyEnabled)
            {
                executionContext.Debug("[Node24Strategy] EOL policy is enabled, cannot fall back to Node16");
                string handlerType = context.HandlerData != null ? context.HandlerData.GetType().Name : "UnknownHandlerData";
                throw new NotSupportedException($"No compatible Node.js version available for host execution. Handler type: {handlerType}. This may occur if all available versions are blocked by EOL policy. Please update your pipeline to use Node20 or Node24 tasks. To temporarily disable EOL policy: Set AGENT_RESTRICT_EOL_NODE_VERSIONS=false");
            }

            return new NodeRunnerInfo
            {
                NodePath = null,
                NodeVersion = NodeVersion.Node16,
                Reason = $"{baseReason}, fallback to Node16 due to both Node24 and Node20 glibc compatibility issues",
                Warning = StringUtil.Loc("NodeEOLRetirementWarning", taskName)
            };
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