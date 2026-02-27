// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.CompilerServices;

namespace Agent.Sdk
{
    public interface ITraceWriter
    {
        void Info(string message, [CallerMemberName] string operation = "");
        void Verbose(string message, [CallerMemberName] string operation = "");
    }
}
