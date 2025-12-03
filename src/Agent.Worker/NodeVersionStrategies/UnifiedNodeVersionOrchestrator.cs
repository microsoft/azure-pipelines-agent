// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Microsoft.VisualStudio.Services.Agent.Worker.NodeVersionStrategies
{
    /// <summary>
    /// Orchestrates Node version selection for BOTH host and container.
    /// Single source of truth for all Node selection logic.
    /// 
    /// ⭐ KEY DESIGN PRINCIPLES:
    /// 1. Strategies ordered by HandlerData.Priority from TaskManager.cs (no duplication)
    /// 2. NO separate EOL policy strategy - each strategy handles its own EOL enforcement
    /// 3. First strategy that returns true from CanHandle() wins
    /// 4. Works identically for host and container (path translation in strategies)
    /// 
    /// Usage:
    /// var context = new UnifiedNodeContext { ... };
    /// var orchestrator = new UnifiedNodeVersionOrchestrator();
    /// var result = orchestrator.SelectNodeVersion(context);
    /// // result.NodePath is ready to use (host or container path)
    /// </summary>
    public sealed class UnifiedNodeVersionOrchestrator
    {
        private readonly List<IUnifiedNodeVersionStrategy> _strategies;

        /// <summary>
        /// Initializes orchestrator with all available strategies.
        /// 
        /// ⭐ STRATEGY REGISTRATION ORDER:
        /// Strategies are registered in the order they should be checked.
        /// We DON'T sort by priority here - strategies are already in correct order.
        /// 
        /// Priority order (from HandlerData):
        /// - CustomNode: 0 (highest priority - always check first)
        /// - Node24HandlerData: 101
        /// - Node20_1HandlerData: 102
        /// - Node16HandlerData: 103 (⭐ EOL - blocked by policy)
        /// - Node10HandlerData: 104 (⭐ EOL - blocked by policy)
        /// - NodeHandlerData (Node6): 105 (⭐ EOL - blocked by policy)
        /// 
        /// Note: We don't need a separate EOL policy strategy because:
        /// - Each strategy enforces its own EOL policy in CanHandle()
        /// - If a strategy is EOL and policy is enabled, CanHandle() returns false
        /// - This naturally prevents EOL versions from being selected
        /// </summary>
        public UnifiedNodeVersionOrchestrator()
        {
            _strategies = new List<IUnifiedNodeVersionStrategy>();

            // ⭐ Register all strategies in priority order ⭐
            // Priority 0 (highest): Custom node path overrides everything
            // TODO: Uncomment after testing Node24→Node6 strategies
            _strategies.Add(new UnifiedCustomNodeStrategy());
            
            // Priority 101: Node24 (current, glibc fallback: 24→20→16)
            _strategies.Add(new UnifiedNode24Strategy());
            
            // Priority 102: Node20 (current, glibc fallback: 20→16)
            _strategies.Add(new UnifiedNode20Strategy());
            
            // Priority 103: Node16 (⭐ EOL - blocked by policy)
            _strategies.Add(new UnifiedNode16Strategy());
            
            // Priority 104: Node10 (⭐ EOL - blocked by policy)
            _strategies.Add(new UnifiedNode10Strategy());
            
            // Priority 105: Node6 (⭐ EOL - blocked by policy, lowest priority)
            _strategies.Add(new UnifiedNode6Strategy());
        }

        /// <summary>
        /// Constructor that accepts custom strategies (for testing).
        /// </summary>
        /// <param name="strategies">List of strategies to use</param>
        public UnifiedNodeVersionOrchestrator(IEnumerable<IUnifiedNodeVersionStrategy> strategies)
        {
            if (strategies == null)
            {
                throw new ArgumentNullException(nameof(strategies));
            }

            _strategies = new List<IUnifiedNodeVersionStrategy>(strategies);
        }

        /// <summary>
        /// Selects Node version for either host or container.
        /// 
        /// ⭐ SELECTION ALGORITHM:
        /// 1. Iterate through strategies in registration order (already priority-ordered)
        /// 2. Call CanHandle() on each strategy
        ///    - Strategy checks: handler type, knobs, EOL policy, glibc compatibility
        /// 3. First strategy that returns true is selected
        /// 4. Call GetNodePath() to get the actual path
        ///    - May throw NotSupportedException if EOL policy violated (defense in depth)
        /// 5. Log selection and emit warnings
        /// 6. Return result
        /// 
        /// If no strategy can handle:
        /// - Likely all strategies rejected due to EOL policy
        /// - Throw NotSupportedException with helpful message
        /// 
        /// Example flow (Node24 task with glibc error):
        /// 1. CustomNodeStrategy.CanHandle() → false (no custom path)
        /// 2. Node24Strategy.CanHandle() → true (Node24 handler + knob enabled)
        /// 3. Node24Strategy.GetNodePath() → node20_1 (glibc fallback)
        /// 4. Return result with warning about fallback
        /// 
        /// Example flow (Node16 task with EOL policy):
        /// 1. CustomNodeStrategy.CanHandle() → false (no custom path)
        /// 2. Node24Strategy.CanHandle() → false (not Node24 handler)
        /// 3. Node20Strategy.CanHandle() → false (not Node20 handler)
        /// 4. Node16Strategy.CanHandle() → false (EOL policy enabled)
        /// 5. Throw NotSupportedException (no compatible version)
        /// </summary>
        /// <param name="context">Context with environment, task, and glibc information</param>
        /// <returns>NodePathResult with selected path, version, reason, and optional warning</returns>
        /// <exception cref="ArgumentNullException">If context is null</exception>
        /// <exception cref="NotSupportedException">If no compatible Node version available</exception>
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

            // Log context information for debugging
            string environmentType = context.IsContainer ? "Container" : "Host";
            context.ExecutionContext.Debug($"[{environmentType}] Starting node version selection");
            context.ExecutionContext.Debug($"[{environmentType}] Handler type: {context.HandlerData?.GetType().Name ?? "null"}");
            context.ExecutionContext.Debug($"[{environmentType}] Handler priority: {context.HandlerData?.Priority ?? 0}");
            context.ExecutionContext.Debug($"[{environmentType}] Node24 glibc error: {context.Node24HasGlibcError}");
            context.ExecutionContext.Debug($"[{environmentType}] Node20 glibc error: {context.Node20HasGlibcError}");

            // Iterate through strategies in priority order
            foreach (var strategy in _strategies)
            {
                context.ExecutionContext.Debug($"[{environmentType}] Checking strategy: {strategy.Name}");

                try
                {
                    // Check if strategy can handle this context
                    // ⭐ CanHandle() checks: handler type, knobs, EOL policy, glibc ⭐
                    if (strategy.CanHandle(context))
                    {
                        context.ExecutionContext.Debug($"[{environmentType}] Strategy '{strategy.Name}' can handle this context");

                        // Get the node path (may throw if EOL policy violated)
                        NodePathResult result = strategy.GetNodePath(context);

                        // Log successful selection
                        int priority = context.HandlerData?.Priority ?? 0;
                        context.ExecutionContext.Output(
                            $"[{environmentType}] Selected Node version: {result.NodeVersion} " +
                            $"(Strategy: {strategy.Name}, Priority: {priority})");
                        context.ExecutionContext.Debug($"[{environmentType}] Node path: {result.NodePath}");
                        context.ExecutionContext.Debug($"[{environmentType}] Reason: {result.Reason}");

                        // Emit warning if present (e.g., glibc fallback)
                        if (!string.IsNullOrEmpty(result.Warning))
                        {
                            context.ExecutionContext.Warning(result.Warning);
                        }

                        // ⭐ Return the successful result ⭐
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
                    
                    // Re-throw immediately - don't try other strategies
                    throw;
                }
                catch (Exception ex)
                {
                    // For other exceptions, log and continue (strategy might be broken, try next one)
                    context.ExecutionContext.Warning($"[{environmentType}] Strategy '{strategy.Name}' threw unexpected exception: {ex.Message}");
                    context.ExecutionContext.Debug($"[{environmentType}] Exception details: {ex}");
                    // Continue to next strategy
                }
            }

            // ⭐ No strategy could handle this context ⭐
            // This typically means:
            // 1. All strategies rejected due to EOL policy
            // 2. Handler type not recognized
            // 3. Required knobs not enabled
            //
            // Provide helpful error message
            string handlerType = context.HandlerData?.GetType().Name ?? "Unknown";
            throw new NotSupportedException(
                $"No compatible Node.js version available for {environmentType.ToLowerInvariant()} execution. " +
                $"Handler type: {handlerType}. " +
                $"This may occur if all available versions are blocked by EOL policy. " +
                $"Please update your pipeline to use Node20 or Node24 tasks. " +
                $"To temporarily disable EOL policy: Set AGENT_ENABLE_EOL_NODE_VERSION_POLICY=false");
        }

        /// <summary>
        /// Gets the number of registered strategies (for testing/diagnostics).
        /// </summary>
        public int StrategyCount => _strategies.Count;

        /// <summary>
        /// Gets the list of registered strategy names (for testing/diagnostics).
        /// </summary>
        public IEnumerable<string> StrategyNames => _strategies.Select(s => s.Name);
    }
}
