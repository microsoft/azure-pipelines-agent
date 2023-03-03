// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Agent.Sdk.Knob
{
    public interface IKnobSource
    {
        string DefaultValue { get; set; }

        KnobValue GetValue(IKnobValueContext context);

        string GetDisplayString();
    }
}
