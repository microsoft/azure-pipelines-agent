using System.Net;
using System.Net.Http;

namespace Microsoft.VisualStudio.Services.Agent.Util
{
    public static class ProxyUtil
    {
        public static void SetDefaultHttpClientProxy(IWebProxy proxy)
        {
            if (proxy != null)
            {
                HttpClient.DefaultProxy = proxy;
            }
        }
    }
}