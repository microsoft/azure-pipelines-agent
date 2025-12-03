// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Agent.Sdk;
using Microsoft.TeamFoundation.DistributedTask.WebApi;

namespace Microsoft.VisualStudio.Services.Agent.Worker.NodeVersionStrategies
{
    /// <summary>
    /// Unified context for both host and container node selection.
    /// ⭐ DESIGN PRINCIPLE: Contains ONLY essential runtime data - strategies read their own knobs! ⭐
    /// 
    /// This approach keeps the context clean and strategies encapsulated:
    /// - Context = Runtime data (environment, glibc errors, task info)
    /// - Strategies = Read configuration (knobs) when needed via ExecutionContext
    /// 
    /// Benefits:
    /// - Lazy evaluation: Knobs only read if strategy is considered
    /// - Clear ownership: Each strategy owns its knob dependencies
    /// - Clean separation: Data vs Configuration
    /// - Easy testing: Mock only what's needed per test
    /// </summary>
    public sealed class UnifiedNodeContext
    {
        // ============================================
        // Environment Information
        // ============================================

        /// <summary>
        /// True if running in a container, false if running on host.
        /// This determines path translation behavior.
        /// </summary>
        public bool IsContainer { get; set; }

        /// <summary>
        /// True if the host OS is Linux.
        /// Used for glibc compatibility checks.
        /// </summary>
        public bool IsHostLinux { get; set; }

        /// <summary>
        /// True if running on Alpine Linux.
        /// Alpine has different glibc compatibility requirements.
        /// </summary>
        public bool IsAlpine { get; set; }

        // ============================================
        // Task Information
        // ============================================

        /// <summary>
        /// The handler data from the task definition.
        /// ⭐ REUSES existing classes from TaskManager.cs:
        /// - Node24HandlerData (Priority 101)
        /// - Node20_1HandlerData (Priority 102)
        /// - Node16HandlerData (Priority 103)
        /// - Node10HandlerData (Priority 104)
        /// - NodeHandlerData/Node6 (Priority 105)
        /// 
        /// Strategies use this to:
        /// - Check handler type (e.g., is it Node24HandlerData?)
        /// - Get priority for selection ordering
        /// </summary>
        public BaseNodeHandlerData HandlerData { get; set; }

        // ============================================
        // Glibc Compatibility (Runtime Test Results)
        // ============================================

        /// <summary>
        /// True if Node24 has glibc compatibility errors (requires glibc 2.28+).
        /// This is a RUNTIME test result, not configuration.
        /// Triggers fallback: Node24 → Node20
        /// </summary>
        public bool Node24HasGlibcError { get; set; }

        /// <summary>
        /// True if Node20 has glibc compatibility errors (requires glibc 2.17+).
        /// This is a RUNTIME test result, not configuration.
        /// Triggers fallback: Node20 → Node16
        /// </summary>
        public bool Node20HasGlibcError { get; set; }

        // ============================================
        // Container-Specific Information
        // ============================================

        /// <summary>
        /// Container information for path translation.
        /// Null for host execution.
        /// 
        /// Used by strategies to call container.TranslateToContainerPath():
        /// - Host path: C:\agent\_work\_tool\node24\bin\node.exe
        /// - Container path: /azp/node24/bin/node
        /// </summary>
        public ContainerInfo Container { get; set; }

        // ============================================
        // Services (For Strategies to Read Knobs/Paths)
        // ============================================

        /// <summary>
        /// Host context for directory lookups.
        /// Used by strategies to get externals directory:
        /// hostContext.GetDirectory(WellKnownDirectory.Externals)
        /// </summary>
        public IHostContext HostContext { get; set; }

        /// <summary>
        /// Execution context for logging, warnings, and KNOB READING.
        /// ⭐ Strategies use this to read their own knobs:
        /// 
        /// Example in UnifiedNode24Strategy:
        /// bool useNode24 = AgentKnobs.UseNode24.GetValue(ctx.ExecutionContext).AsBoolean();
        /// 
        /// This is lazy evaluation - knobs only read if strategy is considered.
        /// </summary>
        public IExecutionContext ExecutionContext { get; set; }

        /// <summary>
        /// Step target for custom node path lookup.
        /// Used by UnifiedCustomNodeStrategy to check:
        /// string customPath = ctx.StepTarget?.CustomNodePath;
        /// 
        /// Null for container execution (containers use Container.CustomNodePath instead).
        /// </summary>
        public ExecutionTargetInfo StepTarget { get; set; }

        // ============================================
        // Strategy Decision (Set by CanHandle, Used by GetNodePath)
        // ============================================

        /// <summary>
        /// The selected node version determined by CanHandle().
        /// This eliminates duplication between CanHandle and GetNodePath.
        /// 
        /// Examples: "node24", "node20_1", "node16", "node10", "node"
        /// 
        /// Flow:
        /// 1. Strategy.CanHandle() makes decision and sets this value
        /// 2. Strategy.GetNodePath() uses this value to build paths
        /// 
        /// Benefits:
        /// - No code duplication between CanHandle/GetNodePath
        /// - CanHandle is the single source of truth for decisions
        /// - GetNodePath just builds paths, no complex logic
        /// </summary>
        public string SelectedNodeVersion { get; set; }

        /// <summary>
        /// Reason for the node version selection (for logging/debugging).
        /// Set by CanHandle() along with SelectedNodeVersion.
        /// 
        /// Examples:
        /// - "Global Node24 enabled"
        /// - "Node24 handler with knob enabled"
        /// - "Node24 glibc error → Node20 fallback"
        /// - "EOL policy upgrade"
        /// </summary>
        public string SelectionReason { get; set; }

        /// <summary>
        /// Warning message if fallback occurred (for user notification).
        /// Set by CanHandle() if glibc fallback happens.
        /// 
        /// Example: "The agent operating system doesn't support Node24. Using Node20 instead."
        /// </summary>
        public string SelectionWarning { get; set; }

        // ============================================
        // ⭐ NO KNOB PROPERTIES ⭐
        // ============================================
        // We do NOT store knobs here!
        // - NO EOLPolicyEnabled
        // - NO UseNode20Knob
        // - NO UseNode24Knob
        // - NO CustomNodePath
        //
        // Why? Because:
        // 1. Lazy evaluation: Knobs only read if strategy is considered (performance)
        // 2. Clear ownership: Each strategy owns its knob dependencies
        // 3. Clean separation: Context = Data, Strategies = Configuration
        // 4. Easy testing: No need to mock knobs in context, mock ExecutionContext instead
        //
        // Strategies read knobs directly via ExecutionContext when needed!
    }
}
