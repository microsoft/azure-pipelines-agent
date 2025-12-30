// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Agent.Sdk;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.Container;

namespace Microsoft.VisualStudio.Services.Agent.Worker.NodeVersionStrategies
{
    public sealed class NodeVersionOrchestrator
    {
        private readonly List<INodeVersionStrategy> _strategies;
        private readonly IExecutionContext ExecutionContext;
        private readonly IHostContext HostContext;
        private readonly IGlibcCompatibilityInfoProvider GlibcChecker;

        public NodeVersionOrchestrator(IExecutionContext executionContext, IHostContext hostContext)
        {
            ArgUtil.NotNull(executionContext, nameof(executionContext));
            ArgUtil.NotNull(hostContext, nameof(hostContext));
            ExecutionContext = executionContext;
            HostContext = hostContext;
            GlibcChecker = HostContext.GetService<IGlibcCompatibilityInfoProvider>();
            GlibcChecker.Initialize(hostContext);
            _strategies = new List<INodeVersionStrategy>();

            // IMPORTANT: Strategy order determines selection priority
            // Add strategies in descending priority order (newest/preferred versions first)
            // The orchestrator will try each strategy in order until one can handle the request
            _strategies.Add(new Node24Strategy());
            _strategies.Add(new Node20Strategy());
            _strategies.Add(new Node16Strategy());
            _strategies.Add(new Node10Strategy());
            _strategies.Add(new Node6Strategy());
        }

        public async Task<NodeRunnerInfo>  SelectNodeVersionAsync(TaskContext context)
        {
            ArgUtil.NotNull(context, nameof(context));

            // Use container-specific selection logic if we're in a container
            if (context.Container != null)
            {
                // Get docker manager for container node testing
                var dockerManager = HostContext.GetService<IDockerCommandManager>();
                return await SelectNodeVersionForContainerAsync(context, dockerManager);
            }

            string environmentType = "Host";
            ExecutionContext.Debug($"[{environmentType}] Starting node version selection");
            ExecutionContext.Debug($"[{environmentType}] Handler type: {context.HandlerData?.GetType().Name ?? "null"}");

            var glibcInfo = await GlibcChecker.GetGlibcCompatibilityAsync(context, ExecutionContext);

            foreach (var strategy in _strategies)
            {
                ExecutionContext.Debug($"[{environmentType}] Checking strategy: {strategy.GetType().Name}");

                try
                {
                    var selectionResult = strategy.CanHandle(context, ExecutionContext, glibcInfo);
                    if (selectionResult != null)
                    {
                        var result = CreateNodeRunnerInfoWithPath(context, selectionResult);
                        
                        // Publish telemetry for monitoring node version selection via Kusto
                        PublishNodeVersionSelectionTelemetry(result, strategy, environmentType, context);
                        
                        ExecutionContext.Output(
                            $"[{environmentType}] Selected Node version: {result.NodeVersion} (Strategy: {strategy.GetType().Name})");
                        ExecutionContext.Debug($"[{environmentType}] Node path: {result.NodePath}");
                        ExecutionContext.Debug($"[{environmentType}] Reason: {result.Reason}");

                        if (!string.IsNullOrEmpty(result.Warning))
                        {
                            ExecutionContext.Warning(result.Warning);
                        }

                        return result;
                    }
                    else
                    {
                        ExecutionContext.Debug($"[{environmentType}] Strategy '{strategy.GetType().Name}' cannot handle this context");
                    }
                }
                catch (NotSupportedException ex)
                {
                    ExecutionContext.Debug($"[{environmentType}] Strategy '{strategy.GetType().Name}' threw NotSupportedException: {ex.Message}");
                    ExecutionContext.Error($"Node version selection failed: {ex.Message}");
                    throw;
                }
                catch (Exception ex)
                {
                    ExecutionContext.Warning($"[{environmentType}] Strategy '{strategy.GetType().Name}' threw unexpected exception: {ex.Message} - trying next strategy");
                }
            }

            string handlerType = context.HandlerData?.GetType().Name ?? "Unknown";
            throw new NotSupportedException(StringUtil.Loc("NodeVersionNotAvailable", handlerType));
        }

        /// <summary>
        /// Container-specific node version selection using CanHandleInContainer methods.
        /// Follows the container knob precedence: Node24 → Node20 → Node16.
        /// </summary>
        public async Task<NodeRunnerInfo> SelectNodeVersionForContainerAsync(TaskContext context, IDockerCommandManager dockerManager)
        {
            string environmentType = "Container";
            ExecutionContext.Debug($"[{environmentType}] Starting container node version selection");
            ExecutionContext.Debug($"[{environmentType}] Handler type: {context.HandlerData?.GetType().Name ?? "null"}");

            var glibcInfo = await GlibcChecker.GetGlibcCompatibilityAsync(context, ExecutionContext);

            // Container strategies in priority order: Node24 → Node20 → Node16
            List<INodeVersionStrategy>  containerStrategies = new List<INodeVersionStrategy>();

            // IMPORTANT: Strategy order determines selection priority
            // Add strategies in descending priority order (newest/preferred versions first)
            // The orchestrator will try each strategy in order until one can handle the request
            containerStrategies.Add(new Node24Strategy());
            containerStrategies.Add(new Node20Strategy());
            containerStrategies.Add(new Node16Strategy());

            foreach (var strategy in containerStrategies)
            {
                ExecutionContext.Debug($"[{environmentType}] Checking container strategy: {strategy.GetType().Name}");

                try
                {
                    var selectionResult = strategy.CanHandleInContainer(context, ExecutionContext, dockerManager, glibcInfo);
                    if (selectionResult != null)
                    {
                        var result = CreateNodeRunnerInfoWithPath(context, selectionResult);
                        
                        // Publish telemetry for monitoring node version selection via Kusto
                        PublishNodeVersionSelectionTelemetry(result, strategy, environmentType, context);
                        
                        ExecutionContext.Output(
                            $"[{environmentType}] Selected Node version: {result.NodeVersion} (Strategy: {strategy.GetType().Name})");
                        ExecutionContext.Debug($"[{environmentType}] Node path: {result.NodePath}");
                        ExecutionContext.Debug($"[{environmentType}] Reason: {result.Reason}");

                        if (!string.IsNullOrEmpty(result.Warning))
                        {
                            ExecutionContext.Warning(result.Warning);
                        }

                        return result;
                    }
                    else
                    {
                        ExecutionContext.Debug($"[{environmentType}] Strategy '{strategy.GetType().Name}' cannot handle this container context");
                    }
                }
                catch (NotSupportedException ex)
                {
                    ExecutionContext.Debug($"[{environmentType}] Strategy '{strategy.GetType().Name}' threw NotSupportedException: {ex.Message}");
                    ExecutionContext.Error($"Container node version selection failed: {ex.Message}");
                    throw;
                }
                catch (Exception ex)
                {
                    ExecutionContext.Warning($"[{environmentType}] Strategy '{strategy.GetType().Name}' threw unexpected exception: {ex.Message} - trying next strategy");
                }
            }

            throw new NotSupportedException("No Node.js version could be selected for container execution. Please check your container knobs and node availability.");
        }

        private NodeRunnerInfo CreateNodeRunnerInfoWithPath(TaskContext context, NodeRunnerInfo selection)
        {
            string externalsPath = HostContext.GetDirectory(WellKnownDirectory.Externals);
            string nodeFolder = NodeVersionHelper.GetFolderName(selection.NodeVersion);
            
            if (context.Container != null)
            {
                // Container execution: use container's OS to determine executable name
                string containerExeExtension = context.Container.ImageOS == PlatformUtil.OS.Windows ? ".exe" : "";
                string hostPath = Path.Combine(externalsPath, nodeFolder, "bin", $"node{IOUtil.ExeExtension}");
                string containerNodePath = context.Container.TranslateToContainerPath(hostPath);
                // Fix the executable extension for the container OS
                string finalPath = containerNodePath.Replace($"node{IOUtil.ExeExtension}", $"node{containerExeExtension}");
                
                return new NodeRunnerInfo
                {
                    NodePath = finalPath,
                    NodeVersion = selection.NodeVersion,
                    Reason = selection.Reason,
                    Warning = selection.Warning
                };
            }
            else
            {
                // Host execution: use host OS executable extension
                string hostPath = Path.Combine(externalsPath, nodeFolder, "bin", $"node{IOUtil.ExeExtension}");
                
                return new NodeRunnerInfo
                {
                    NodePath = hostPath,
                    NodeVersion = selection.NodeVersion,
                    Reason = selection.Reason,
                    Warning = selection.Warning
                };
            }
        }

        private void PublishNodeVersionSelectionTelemetry(NodeRunnerInfo result, INodeVersionStrategy strategy, string environmentType, TaskContext context)
        {
            try
            {
                var telemetryData = new Dictionary<string, string>
                {
                    { "NodeVersion", result.NodeVersion.ToString() },
                    { "Strategy", strategy.GetType().Name },
                    { "EnvironmentType", environmentType },
                    { "HandlerType", context.HandlerData?.GetType().Name ?? "Unknown" },
                    { "SelectionReason", result.Reason ?? "" },
                    { "HasWarning", (!string.IsNullOrEmpty(result.Warning)).ToString() },
                    { "JobId", ExecutionContext.Variables.System_JobId.ToString() },
                    { "PlanId", ExecutionContext.Variables.Get(Constants.Variables.System.PlanId) ?? "" },
                    { "AgentName", ExecutionContext.Variables.Get(Constants.Variables.Agent.Name) ?? "" },
                    { "IsContainer", (context.Container != null).ToString() }
                };
                
                ExecutionContext.PublishTaskRunnerTelemetry(telemetryData);
            }
            catch (Exception ex)
            {
                ExecutionContext.Debug($"Failed to publish node version selection telemetry: {ex.Message}");
            }
        }
    }
}