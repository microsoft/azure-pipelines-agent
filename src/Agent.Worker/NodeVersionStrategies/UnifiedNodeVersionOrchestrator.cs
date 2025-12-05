// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Microsoft.VisualStudio.Services.Agent.Worker.NodeVersionStrategies
{
    public sealed class UnifiedNodeVersionOrchestrator
    {
        private readonly List<IUnifiedNodeVersionStrategy> _strategies;

        public UnifiedNodeVersionOrchestrator()
        {
            _strategies = new List<IUnifiedNodeVersionStrategy>();

            _strategies.Add(new UnifiedCustomNodeStrategy());
            _strategies.Add(new UnifiedNode24Strategy());
            _strategies.Add(new UnifiedNode20Strategy());
            _strategies.Add(new UnifiedNode16Strategy());
            _strategies.Add(new UnifiedNode10Strategy());
            _strategies.Add(new UnifiedNode6Strategy());
        }

        public UnifiedNodeVersionOrchestrator(IEnumerable<IUnifiedNodeVersionStrategy> strategies)
        {
            if (strategies == null)
            {
                throw new ArgumentNullException(nameof(strategies));
            }

            _strategies = new List<IUnifiedNodeVersionStrategy>(strategies);
        }

        public NodePathResult SelectNodeVersion(UnifiedNodeContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (context.ExecutionContext == null)
            {
                throw new ArgumentException("ExecutionContext is required in context", nameof(context));
            }

            if (context.HostContext == null)
            {
                throw new ArgumentException("HostContext is required in context", nameof(context));
            }

            string environmentType = context.IsContainer ? "Container" : "Host";
            context.ExecutionContext.Debug($"[{environmentType}] Starting node version selection");
            context.ExecutionContext.Debug($"[{environmentType}] Handler type: {context.HandlerData?.GetType().Name ?? "null"}");

            foreach (var strategy in _strategies)
            {
                context.ExecutionContext.Debug($"[{environmentType}] Checking strategy: {strategy.Name}");

                try
                {
                    if (strategy.CanHandle(context))
                    {
                        NodePathResult result = strategy.GetNodePath(context);

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
                    // ⭐ FAIL FAST: Strategy determined this is the right handler but configuration is invalid ⭐
                    context.ExecutionContext.Debug($"[{environmentType}] Strategy '{strategy.Name}' threw NotSupportedException: {ex.Message}");
                    context.ExecutionContext.Error($"Node version selection failed: {ex.Message}");
                    throw;
                }
                catch (Exception ex)
                {
                    context.ExecutionContext.Warning($"[{environmentType}] Strategy '{strategy.Name}' threw unexpected exception: {ex.Message}");
                }
            }

            string handlerType = context.HandlerData?.GetType().Name ?? "Unknown";
            throw new NotSupportedException(StringUtil.Loc("NodeVersionNotAvailable", handlerType));
            // throw new NotSupportedException($"No compatible Node.js version available for host execution. Handler type: {0}. This may occur if all available versions are blocked by EOL policy. Please update your pipeline to use Node20 or Node24 tasks. To temporarily disable EOL policy: Set AGENT_ENABLE_EOL_NODE_VERSION_POLICY=false", handlerType);
        }

        public int StrategyCount => _strategies.Count;

        public IEnumerable<string> StrategyNames => _strategies.Select(s => s.Name);
    }
}
