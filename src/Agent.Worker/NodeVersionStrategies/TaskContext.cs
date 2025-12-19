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
    public sealed class TaskContext
    {
        /// <summary>
        /// The handler data from the task definition.
        /// </summary>
        public BaseNodeHandlerData HandlerData { get; set; }

        /// <summary>
        /// Container information for path translation. Null for host execution.
        /// </summary>
        public ContainerInfo Container { get; set; }

        /// <summary>
        /// Step target for custom node path lookup. Null for container execution.
        /// </summary>
        public ExecutionTargetInfo StepTarget { get; set; }
    }
}
