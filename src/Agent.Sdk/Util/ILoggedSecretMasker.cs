using Microsoft.TeamFoundation.DistributedTask.Logging;
using System;

namespace Agent.Sdk.Util
{
    /// <summary>
    /// Extended ISecretMasker interface that is adding support of logging secret masker methods
    /// </summary>
    public interface ILoggedSecretMasker : ISecretMasker
    {
        int MinSecretLengthLimit { get; }

        void AddRegex(String pattern, string origin);
        void AddValue(String value, string origin);
        void AddValueEncoder(ValueEncoder encoder, string origin);
        void SetTrace(ITraceWriter trace);
    }
}
