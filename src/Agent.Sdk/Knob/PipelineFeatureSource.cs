// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Agent.Sdk.Knob;

/// <summary>
/// Wrapper knob source for runtime feature flags.
/// </summary>
public class PipelineFeatureSource : RuntimeKnobSource
{
    public PipelineFeatureSource(string featureName) : base($"DistributedTask.Agent.{featureName}")
    {
    }
}
