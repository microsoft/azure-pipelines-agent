// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace Microsoft.VisualStudio.Services.Agent.Worker.NodeVersionStrategies
{
    /// <summary>
    /// Strategy interface that works for BOTH host and container node selection.
    /// Each Node version implements this once, used everywhere.
    /// 
    /// ⭐ KEY DESIGN DECISIONS:
    /// 1. NO Priority property - We use HandlerData.Priority from TaskManager.cs (no duplication)
    /// 2. NO ValidateEOL method - Each strategy handles its own EOL policy in CanHandle/GetNodePath
    /// 3. Each strategy reads its own knobs via ExecutionContext (lazy evaluation, clean encapsulation)
    /// 
    /// Strategy Implementation Pattern:
    /// - CanHandle(): Check handler type + knobs + EOL policy → Can we use this version?
    /// - GetNodePath(): Build path, handle glibc fallback, return result
    /// - GetNodePathForEnvironment(): Translate to host or container path
    /// 
    /// Example Flow:
    /// 1. Orchestrator calls CanHandle() on each strategy (in priority order from HandlerData)
    /// 2. First strategy that returns true is selected
    /// 3. Orchestrator calls GetNodePath() to get the actual path
    /// 4. Strategy may throw NotSupportedException if EOL policy violated
    /// </summary>
    public interface IUnifiedNodeVersionStrategy
    {
        /// <summary>
        /// Human-readable name of this strategy.
        /// Examples: "Node24", "Node20", "Node16", "CustomNode"
        /// Used for logging and debugging.
        /// </summary>
        string Name { get; }

        // ⭐ NO Priority property - We use ctx.HandlerData.Priority instead! ⭐
        // This avoids duplication since HandlerData classes already have Priority:
        // - Node24HandlerData.Priority = 101
        // - Node20_1HandlerData.Priority = 102
        // - Node16HandlerData.Priority = 103
        // - Node10HandlerData.Priority = 104
        // - NodeHandlerData.Priority = 105

        /// <summary>
        /// Checks if this strategy can handle the given context.
        /// 
        /// ⭐ INCLUDES EOL POLICY CHECK ⭐
        /// - Strategy should return false if EOL and policy enabled
        /// - This prevents EOL versions from being selected
        /// 
        /// Typical implementation checks:
        /// 1. Handler type: Is ctx.HandlerData the right type for this strategy?
        /// 2. Knobs: Are the required knobs enabled? (read via ctx.ExecutionContext)
        /// 3. EOL policy: If version is EOL, is policy disabled?
        /// 4. Glibc compatibility: Can we handle the fallback chain?
        /// 
        /// Example (Node24Strategy):
        /// - Check: ctx.HandlerData is Node24HandlerData
        /// - Read: UseNode24 knob via ctx.ExecutionContext
        /// - Check: EOL policy (Node24 → Node20 → Node16, reject if ends at Node16 with policy)
        /// 
        /// Example (Node16Strategy - EOL):
        /// - Check: ctx.HandlerData is Node16HandlerData
        /// - Read: EOL policy knob via ctx.ExecutionContext
        /// - Return false if EOL policy enabled (blocks EOL version)
        /// </summary>
        /// <param name="context">Context with environment, task, and glibc information</param>
        /// <returns>True if this strategy can handle the context, false otherwise</returns>
        bool CanHandle(UnifiedNodeContext context);

        /// <summary>
        /// Gets the Node path for the given context.
        /// Works for BOTH host and container (path translation handled internally).
        /// 
        /// ⭐ MAY THROW NotSupportedException IF EOL POLICY VIOLATED ⭐
        /// - Defense in depth: CanHandle() should have rejected, but we double-check here
        /// - Provides clear error message to user
        /// 
        /// Typical implementation:
        /// 1. Determine node folder (may have glibc fallback: Node24 → Node20 → Node16)
        /// 2. Build warning message if fallback occurred
        /// 3. Call GetNodePathForEnvironment() to get host or container path
        /// 4. Return NodePathResult with path, version, reason, warning
        /// 
        /// Example (Node24Strategy):
        /// - Start with "node24"
        /// - If Node24HasGlibcError: fallback to "node20_1"
        /// - If Node20HasGlibcError too: fallback to "node16"
        /// - Get path: host (C:\...) or container (/azp/...) based on ctx.IsContainer
        /// - Return result
        /// 
        /// Example (Node16Strategy - EOL):
        /// - Double-check EOL policy
        /// - Throw NotSupportedException if policy enabled (with helpful message)
        /// - Otherwise return "node16" path
        /// </summary>
        /// <param name="context">Context with environment, task, and glibc information</param>
        /// <returns>NodePathResult with path, version, reason, and optional warning</returns>
        /// <exception cref="NotSupportedException">If EOL policy prevents using this version</exception>
        NodePathResult GetNodePath(UnifiedNodeContext context);

        // ⭐ NO ValidateEOL method - Each strategy handles its own EOL policy! ⭐
        // Why? Because:
        // 1. Encapsulation: Strategy owns its EOL rules
        // 2. Flexibility: Different strategies can have different EOL behaviors
        // 3. Simplicity: No separate validation step, just CanHandle() check
        // 4. Clarity: EOL logic is in the strategy, not scattered across interface methods
    }
}
