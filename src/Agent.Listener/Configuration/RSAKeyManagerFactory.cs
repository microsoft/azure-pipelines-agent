using Agent.Sdk;
using System;

namespace Microsoft.VisualStudio.Services.Agent.Listener.Configuration
{
    /// <summary>
    /// On Windows, there is a more-secure implementation of IRSAKeyManager.
    /// The agent's service locator doesn't give an opportunity for runtime logic,
    /// therefore, we need a factory class to inject that logic.
    /// </summary>
    [ServiceLocator(Default = typeof(RSAKeyManagerFactory))]
    public interface IRSAKeyManagerFactory : IAgentService
    {
        IRSAKeyManager Instance { get; }
    }

    public class RSAKeyManagerFactory : AgentService, IRSAKeyManagerFactory
    {
        public IRSAKeyManager Instance
        {
            get
            {
                if (_instance is null)
                {
                    _instance = PlatformUtil.RunningOnWindows
                        ? (IRSAKeyManager)(new RSAEncryptedFileKeyManager())
                        : (IRSAKeyManager)(new RSAFileKeyManager());
                }

                return _instance;
            }
        }

        private static IRSAKeyManager _instance;
    }
}