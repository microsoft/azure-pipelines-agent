// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Agent.Sdk.Knob
{
    public interface IEnvironmentKnobSource : IKnobSource
    {
        string GetEnvironmentVariableName();
    }
}
