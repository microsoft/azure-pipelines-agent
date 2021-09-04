// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace Microsoft.TeamFoundation.DistributedTask.Orchestration.Server.Pipelines.Yaml.Contracts
{
    internal sealed class PhaseSelector
    {
        internal String Name { get; set; }

        internal IDictionary<String, IList<ISimpleStep>> StepOverrides { get; set; }
    }
}
