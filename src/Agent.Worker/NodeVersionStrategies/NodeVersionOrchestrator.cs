// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Services.Agent.Util;

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
            _strategies.Add(new CustomNodeStrategy());
            _strategies.Add(new Node24Strategy());
            _strategies.Add(new Node20Strategy());
            _strategies.Add(new Node16Strategy());
            _strategies.Add(new Node10Strategy());
            _strategies.Add(new Node6Strategy());
        }

        public async Task<NodeRunnerInfo>  SelectNodeVersionAsync(TaskContext context)
        {
            ArgUtil.NotNull(context, nameof(context));

            string environmentType = context.Container != null ? "Container" : "Host";
            ExecutionContext.Debug($"[{environmentType}] Starting node version selection");
            ExecutionContext.Debug($"[{environmentType}] Handler type: {context.HandlerData?.GetType().Name ?? "null"}");

            var glibcInfo = await GlibcChecker.GetGlibcCompatibilityAsync(context);

            foreach (var strategy in _strategies)
            {
                ExecutionContext.Debug($"[{environmentType}] Checking strategy: {strategy.GetType().Name}");

                try
                {
                    var selectionResult = strategy.CanHandle(context, ExecutionContext, glibcInfo);
                    if (selectionResult != null)
                    {
                        var result = CreateNodeRunnerInfoWithPath(context, selectionResult);

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

        private NodeRunnerInfo CreateNodeRunnerInfoWithPath(TaskContext context, NodeRunnerInfo selection)
        {
            // If the strategy already provided a complete path (like CustomNodeStrategy), use it directly
            if (!string.IsNullOrEmpty(selection.NodePath))
            {
                return selection;
            }
            
            // For standard node versions, construct path from externals directory
            string externalsPath = HostContext.GetDirectory(WellKnownDirectory.Externals);
            string hostPath = Path.Combine(externalsPath, selection.NodeVersion, "bin", $"node{IOUtil.ExeExtension}");
            string finalPath = context.Container != null ? 
                            context.Container.TranslateToContainerPath(hostPath) : hostPath;

            return new NodeRunnerInfo
            {
                NodePath = finalPath,
                NodeVersion = selection.NodeVersion,
                Reason = selection.Reason,
                Warning = selection.Warning
            };
        }
    }
}
