// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Agent.Sdk.Knob
{
    public interface IKnobSource
    {
        KnobValue GetValue(IKnobValueContext context);

        string GetDisplayString();
    }
}
