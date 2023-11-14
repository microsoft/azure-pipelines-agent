// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
//using Microsoft.TeamFoundation.DistributedTask.Logging;
using ValueEncoder = Microsoft.TeamFoundation.DistributedTask.Logging.ValueEncoder;
using System;

namespace Agent.Sdk.SecretMasking
{
    /// <summary>
    /// Extended ISecretMasker interface that is adding support of logging secret masker methods
    /// </summary>
    public interface ILoggedSecretMasker : ISecretMasker
    {
        static int MinSecretLengthLimit { get; }

        void AddRegex(String pattern, string origin);
        void AddValue(String value, string origin);
        void AddValueEncoder(ValueEncoder encoder, string origin);
        void SetTrace(ITraceWriter trace);
    }
}
