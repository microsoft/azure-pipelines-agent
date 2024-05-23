namespace Agent.Sdk.Util
{
    internal class NullTraceWriter : ITraceWriter
    {
        public void Info(string message)
        {
        }

        public void Verbose(string message)
        {
        }
    }
}
