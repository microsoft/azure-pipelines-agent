using System.Net;

namespace Microsoft.VisualStudio.Services.Agent
{
  // The purpose of this class is to store user's credential during agent configuration and retrive the credential back at runtime.
#if OS_WINDOWS
    [ServiceLocator(Default = typeof(WindowsAgentCredentialStore))]
#elif OS_OSX
  [ServiceLocator(Default = typeof(MacOSAgentCredentialStore))]
#else
    [ServiceLocator(Default = typeof(LinuxAgentCredentialStore))]
#endif
    public interface IAgentCredentialStore : IAgentService
    {
        NetworkCredential Write(string target, string username, string password);

        // throw exception when target not found from cred store
        NetworkCredential Read(string target);

        // throw exception when target not found from cred store
        void Delete(string target);
    }
}
