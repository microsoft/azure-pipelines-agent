using System;
using System.Runtime.Serialization;

namespace Microsoft.VisualStudio.Services.Agent.Listener
{

    /// <summary>
    /// Represents errors that occur during validating intelligence pack signatures
    /// </summary>
    [SerializableAttribute]
    public class VerificationException : Exception
    {
        public VerificationException(string message) : base(message)
        {
        }

        public VerificationException(string message, Exception ex)
            : base(message, ex)
        {
        }

        protected VerificationException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
