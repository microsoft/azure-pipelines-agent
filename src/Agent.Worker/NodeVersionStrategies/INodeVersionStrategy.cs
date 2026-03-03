// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Microsoft.VisualStudio.Services.Agent.Worker.Container;

namespace Microsoft.VisualStudio.Services.Agent.Worker.NodeVersionStrategies
{
    /// <summary>
    /// Strategy interface for both host and container node selection.
    /// </summary>
    public interface INodeVersionStrategy
    {

        /// <summary>
        /// Evaluates if this strategy can handle the given context and determines the node version to use.
        /// Includes handler type checks, knob evaluation, EOL policy enforcement, and glibc compatibility.
        /// </summary>
        /// <param name="context">Context with environment, task, and glibc information</param>
        /// <param name="executionContext">Execution context for knob evaluation</param>
        /// <param name="glibcInfo">Glibc compatibility information for Node versions</param>
        /// <returns>NodeRunnerInfo with selected version and metadata if this strategy can handle the context, null if it cannot handle</returns>
        /// <exception cref="NotSupportedException">Thrown when EOL policy prevents using any compatible version</exception>
        NodeRunnerInfo CanHandle(TaskContext context, IExecutionContext executionContext, GlibcCompatibilityInfo glibcInfo);

        /// <summary>
        /// Evaluates if this strategy can handle container execution and determines the node version to use.
        /// Only Node24, Node20, and Node16 strategies support container execution.
        /// </summary>
        /// <param name="context">Context with container and task information</param>
        /// <param name="executionContext">Execution context for knob evaluation</param>
        /// <param name="dockerManager">Docker command manager for container operations</param>
        /// <returns>NodeRunnerInfo with selected version and metadata if this strategy can handle container execution, null if it cannot handle or doesn't support containers</returns>
        /// <exception cref="NotSupportedException">Thrown when EOL policy prevents using any compatible version</exception>
        NodeRunnerInfo CanHandleInContainer(TaskContext context, IExecutionContext executionContext, IDockerCommandManager dockerManager)
        {
            // Default implementation: older strategies (Node10, Node6) don't support container execution
            return null;
        }
    }
}