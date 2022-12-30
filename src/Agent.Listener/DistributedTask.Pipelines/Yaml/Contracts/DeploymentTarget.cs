// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace Microsoft.TeamFoundation.DistributedTask.Orchestration.Server.Pipelines.Yaml.Contracts
{
    internal sealed class DeploymentTarget : IPhaseTarget
    {
        internal String ContinueOnError { get; set; }

        internal String Group { get; set; }

        internal String HealthOption { get; set; }

        internal String Percentage { get; set; }

        internal IList<String> Tags { get; set; }

        internal String TimeoutInMinutes { get; set; }

        /// <summary>Number of retries for task failure</summary>
        internal String RetryCountOnTaskFailure { get; set; }
    }
}
