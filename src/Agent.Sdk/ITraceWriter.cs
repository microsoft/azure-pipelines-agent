// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Agent.Sdk
{
    public interface ITraceWriter
    {
        void Info(string message);
        void Verbose(string message);
    }
}
