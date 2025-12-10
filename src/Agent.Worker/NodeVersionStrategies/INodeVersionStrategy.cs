// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace Microsoft.VisualStudio.Services.Agent.Worker.NodeVersionStrategies
{
    /// <summary>
    /// Strategy interface for both host and container node selection.
    /// </summary>
    public interface INodeVersionStrategy
    {
        /// <summary>
        /// Human-readable name of this strategy for logging and debugging.
        /// Examples: "Node24", "Node20", "Node16", "CustomNode"
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Checks if this strategy can handle the given context.
        /// Includes handler type, knob checks, EOL policy, and glibc compatibility.
        /// </summary>
        /// <param name="context">Context with environment, task, and glibc information</param>
        /// <returns>True if this strategy can handle the context, false otherwise</returns>
        NodeRunnerInfo CanHandle(NodeContext context);

        /// <summary>
        /// Gets the Node path for the given context.
        /// Works for both host and container (path translation handled internally).
        /// May throw NotSupportedException if EOL policy is violated.
        /// </summary>
        /// <param name="context">Context with environment, task, and glibc information</param>
        /// <returns>NodeRunnerInfo with path, version, reason, and optional warning</returns>
        /// <exception cref="NotSupportedException">If EOL policy prevents using this version</exception>
        // NodeRunnerInfo GetNodePath(NodeContext context);
    }
}
