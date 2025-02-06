// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;

namespace Microsoft.VisualStudio.Services.Agent
{
    // Store and retrieve user's credential for agent configuration.
    [ServiceLocator(
      PreferredOnWindows = typeof(WindowsAgentCredentialStore),
      PreferredOnMacOS = typeof(MacOSAgentCredentialStore),
      PreferredOnLinux = typeof(LinuxAgentCredentialStore),
      Default = typeof(NoOpAgentCredentialStore)
      )]
    public interface IAgentCredentialStore : IAgentService
    {
        void Write(string target, string username, string password);

        // throw exception when target not found from cred store
        NetworkCredential Read(string target);


        // variant that does not return NetworkCredential class, which suffers from OS-level crashes on macOS
        (string UserName, string Password) Read2(string target);

        // throw exception when target not found from cred store
        void Delete(string target);
    }
}
