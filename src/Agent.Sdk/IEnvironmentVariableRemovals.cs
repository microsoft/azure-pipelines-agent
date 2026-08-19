// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace Agent.Sdk
{
    public interface IEnvironmentVariableRemovals
    {
        IReadOnlyCollection<string> RemovedEnvironmentVariables { get; }
    }
}
