using System;
using System.Net;

namespace Microsoft.VisualStudio.Services.Agent.Listener.Diagnostics
{
    class DnsTest : IDiagnosticTest
    {
        public bool Execute(ITerminal terminal)
        {
            try
            {
                IPHostEntry host = Dns.GetHostEntry(c_hostname);

                terminal.WriteLine(string.Format("GetHostEntry: {0} returns:", c_hostname));
                foreach (IPAddress address in host.AddressList)
                {
                    terminal.WriteLine($"    {address}");
                }
                return true;
            }
            catch (Exception ex)
            {
                terminal.WriteError(ex);
                return false;
            }
        }

        private const string c_hostname = "www.bing.com";
    }
}
