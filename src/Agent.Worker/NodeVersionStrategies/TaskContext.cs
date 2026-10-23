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

        /// <summary>
        /// Returns the maximum Node version this task was authored to run on,
        /// derived from the handler data type.
        /// Strategies use this as a ceiling: if EffectiveMaxVersion is less than their version, they return null.
        /// Global overrides and EOL policy bypass this ceiling.
        /// </summary>
        public int EffectiveMaxVersion
        {
            get
            {
                return HandlerData switch
                {
                    Node24HandlerData => 24,
                    Node20_1HandlerData => 20,
                    Node16HandlerData => 16,
                    Node10HandlerData => 10,
                    NodeHandlerData => 6,
                    _ => 6
                };
            }
        }
    }
}
