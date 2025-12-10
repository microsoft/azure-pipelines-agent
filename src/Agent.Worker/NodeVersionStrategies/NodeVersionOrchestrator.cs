// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Microsoft.VisualStudio.Services.Agent.Worker.NodeVersionStrategies
{
    public sealed class NodeVersionOrchestrator
    {
        private readonly List<INodeVersionStrategy> _strategies;

        public NodeVersionOrchestrator()
        {
            _strategies = new List<INodeVersionStrategy>();

            _strategies.Add(new Node24Strategy());
            _strategies.Add(new Node20Strategy());
            _strategies.Add(new Node16Strategy());
            _strategies.Add(new Node10Strategy());
            _strategies.Add(new Node6Strategy());
        }

        public NodeVersionOrchestrator(IEnumerable<INodeVersionStrategy> strategies)
        {
            ArgUtil.NotNull(strategies, nameof(strategies));
            _strategies = new List<INodeVersionStrategy>(strategies);
        }

        public NodeRunnerInfo SelectNodeVersion(NodeContext context)
        {
            ArgUtil.NotNull(context, nameof(context));
            ArgUtil.NotNull(context.ExecutionContext, nameof(context.ExecutionContext));
            ArgUtil.NotNull(context.HostContext, nameof(context.HostContext));

            string environmentType = context.IsContainer ? "Container" : "Host";
            context.ExecutionContext.Debug($"[{environmentType}] Starting node version selection");
            context.ExecutionContext.Debug($"[{environmentType}] Handler type: {context.HandlerData?.GetType().Name ?? "null"}");

            foreach (var strategy in _strategies)
            {
                context.ExecutionContext.Debug($"[{environmentType}] Checking strategy: {strategy.Name}");

                try
                {
                    var selectionResult = strategy.CanHandle(context);
                    if (selectionResult != null)
                    {
                        var result = CreateNodeRunnerInfoWithPath(context, selectionResult);
                        // NodeRunnerInfo result = strategy.GetNodePath(context);

                        context.ExecutionContext.Output(
                            $"[{environmentType}] Selected Node version: {result.NodeVersion} (Strategy: {strategy.Name})");
                        context.ExecutionContext.Debug($"[{environmentType}] Node path: {result.NodePath}");
                        context.ExecutionContext.Debug($"[{environmentType}] Reason: {result.Reason}");

                        if (!string.IsNullOrEmpty(result.Warning))
                        {
                            context.ExecutionContext.Warning(result.Warning);
                        }

                        return result;
                    }
                    else
                    {
                        context.ExecutionContext.Debug($"[{environmentType}] Strategy '{strategy.Name}' cannot handle this context");
                    }
                }
                catch (NotSupportedException ex)
                {
                    context.ExecutionContext.Debug($"[{environmentType}] Strategy '{strategy.Name}' threw NotSupportedException: {ex.Message}");
                    context.ExecutionContext.Error($"Node version selection failed: {ex.Message}");
                    throw;
                }
                catch (Exception ex)
                {
                    context.ExecutionContext.Warning($"[{environmentType}] Strategy '{strategy.Name}' threw unexpected exception: {ex.Message} - trying next strategy");
                }
            }

            string handlerType = context.HandlerData?.GetType().Name ?? "Unknown";
            throw new NotSupportedException(StringUtil.Loc("NodeVersionNotAvailable", handlerType));
        }

        private NodeRunnerInfo CreateNodeRunnerInfoWithPath(NodeContext context, NodeRunnerInfo selection)
        {
            string externalsPath = context.HostContext.GetDirectory(WellKnownDirectory.Externals);
            string hostPath = Path.Combine(externalsPath, selection.NodeVersion, "bin", $"node{IOUtil.ExeExtension}");
            string finalPath = context.IsContainer && context.Container != null ? 
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
