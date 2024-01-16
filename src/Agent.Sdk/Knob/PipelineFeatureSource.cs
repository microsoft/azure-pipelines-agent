// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Agent.Sdk.Knob;

public class PipelineFeatureSource : IKnobSource
{
    private readonly string _runTimeVar;

    public PipelineFeatureSource(string featureName)
    {
        _runTimeVar = $"DistributedTask.Agent.{featureName}";
    }

    public KnobValue GetValue(IKnobValueContext context)
    {
        ArgUtil.NotNull(context, nameof(context));

        string value = null;
        try
        {
            value = context.GetVariableValueOrDefault(_runTimeVar);
        }
        catch (NotSupportedException)
        {
            throw new NotSupportedException($"{nameof(PipelineFeatureSource)} not supported for context type {context.GetType()}");
        }

        if (!string.IsNullOrEmpty(value))
        {
            return new KnobValue(value, this);
        }
        return null;
    }

    public string GetDisplayString()
    {
        return $"$({_runTimeVar})";
    }
}
