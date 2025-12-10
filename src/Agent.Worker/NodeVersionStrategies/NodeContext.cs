// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Agent.Sdk;
using Microsoft.TeamFoundation.DistributedTask.WebApi;

namespace Microsoft.VisualStudio.Services.Agent.Worker.NodeVersionStrategies
{
    /// <summary>
    /// Context for both host and container node selection.
    /// Contains runtime data - strategies read their own knobs via ExecutionContext.
    /// </summary>
    public sealed class NodeContext
    {
        /// <summary>
        /// True if running in a container, false if running on host.
        /// </summary>
        public bool IsContainer { get; set; }

        /// <summary>
        /// True if the host OS is Linux.
        /// </summary>
        public bool IsHostLinux { get; set; }

        /// <summary>
        /// True if running on Alpine Linux.
        /// </summary>
        public bool IsAlpine { get; set; }

        /// <summary>
        /// The handler data from the task definition.
        /// </summary>
        public BaseNodeHandlerData HandlerData { get; set; }

        /// <summary>
        /// True if Node24 has glibc compatibility errors (requires glibc 2.28+).
        /// </summary>
        public bool Node24HasGlibcError { get; set; }

        /// <summary>
        /// True if Node20 has glibc compatibility errors (requires glibc 2.17+).
        /// </summary>
        public bool Node20HasGlibcError { get; set; }

        /// <summary>
        /// Container information for path translation. Null for host execution.
        /// </summary>
        public ContainerInfo Container { get; set; }

        /// <summary>
        /// Host context for directory lookups.
        /// </summary>
        public IHostContext HostContext { get; set; }

        /// <summary>
        /// Execution context for logging, warnings, and knob reading.
        /// </summary>
        public IExecutionContext ExecutionContext { get; set; }

        /// <summary>
        /// Step target for custom node path lookup. Null for container execution.
        /// </summary>
        public ExecutionTargetInfo StepTarget { get; set; }

        /// <summary>
        /// The selected node version determined by CanHandle().
        /// Examples: "node24", "node20_1", "node16", "node10", "node"
        /// </summary>
        public string SelectedNodeVersion { get; set; }

        /// <summary>
        /// Reason for the node version selection (for logging/debugging).
        /// </summary>
        public string SelectionReason { get; set; }

        /// <summary>
        /// Warning message if fallback occurred (for user notification).
        /// </summary>
        public string SelectionWarning { get; set; }
    }
}
