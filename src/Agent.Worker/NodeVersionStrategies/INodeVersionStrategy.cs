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
        /// Evaluates if this strategy can handle the given context and determines the node version to use.
        /// Includes handler type checks, knob evaluation, EOL policy enforcement, and glibc compatibility.
        /// </summary>
        /// <param name="context">Context with environment, task, and glibc information</param>
        /// <param name="executionContext">Execution context for knob evaluation</param>
        /// <param name="glibcInfo">Glibc compatibility information for Node versions</param>
        /// <returns>NodeRunnerInfo with selected version and metadata if this strategy can handle the context, null if it cannot handle</returns>
        /// <exception cref="NotSupportedException">Thrown when EOL policy prevents using any compatible version</exception>
        NodeRunnerInfo CanHandle(TaskContext context, IExecutionContext executionContext, GlibcCompatibilityInfo glibcInfo);
    }
}
