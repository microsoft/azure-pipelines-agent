using System.Runtime.CompilerServices;

namespace Agent.Sdk.Util
{
    internal class NullTraceWriter : ITraceWriter
    {
        public void Info(string message, [CallerMemberName] string operation = "")
        {
        }

        public void Verbose(string message, [CallerMemberName] string operation = "")
        {
        }
    }
}
