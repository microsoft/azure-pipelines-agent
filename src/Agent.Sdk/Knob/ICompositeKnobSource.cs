// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Agent.Sdk.Knob
{
    public interface ICompositeKnobSource : IKnobSource
    {
        bool hasEnvironmentSourceWithName(string name);
    }
}
