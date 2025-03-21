// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
//using Microsoft.TeamFoundation.DistributedTask.Logging;
using System;
using Microsoft.Security.Utilities;

using ISecretMaskerTfs = Microsoft.TeamFoundation.DistributedTask.Logging.ISecretMasker;

namespace Agent.Sdk.SecretMasking
{
    /// <summary>
    /// Extended ISecretMasker interface that is adding support of logging secret masker methods
    /// </summary>
    public interface ILoggedSecretMasker : ISecretMasker, ISecretMaskerTfs
    {
        static int MinimumSecretLength { get; }

        void AddRegex(String pattern, string origin);
        void AddValue(String value, string origin);
        void AddValueEncoder(LiteralEncoder encoder, string origin);
        void SetTrace(ITraceWriter trace);
        new string MaskSecrets(string input);
    }
}
