// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Agent.Sdk.Knob
{
    public interface IKnobValueContext
    {
        string GetVariableValueOrDefault(string variableName);

        IScopedEnvironment GetScopedEnvironment();
    }
}
