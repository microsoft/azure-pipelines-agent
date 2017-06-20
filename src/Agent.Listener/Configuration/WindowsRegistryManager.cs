#if OS_WINDOWS
using System;
using System.Collections.Generic;
using System.Security.Principal;
using Microsoft.Win32;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Microsoft.VisualStudio.Services.Agent.Listener.Configuration
{
    [ServiceLocator(Default = typeof(WindowsRegistryManager))]
    public interface IWindowsRegistryManager : IAgentService
    {
        string GetKeyValue(string path, string subKeyName);
        void SetKeyValue(string path, string subKeyName, string subKeyValue);
        void DeleteKey(RegistryScope scope, string path, string subKeyName);
        bool RegsitryExists(string securityId);
    }

    public class WindowsRegistryManager : AgentService, IWindowsRegistryManager
    {
        public void DeleteKey(RegistryScope scope, string path, string subKeyName)
        {
            RegistryKey key = null;
            switch(scope)
            {
                case RegistryScope.CurrentUser :
                    key = Registry.CurrentUser.OpenSubKey(path, true);                    
                    break;
                case RegistryScope.LocalMachine:
                    key = Registry.LocalMachine.OpenSubKey(path, true);                    
                    break;
            }

            if(key != null)
            {
                using(key)
                {
                    key.DeleteSubKey(subKeyName, false);
                }
            }
        }

        public string GetKeyValue(string path, string subKeyName)
        {
            var regValue = Registry.GetValue(path, subKeyName, null);
            return regValue != null ? regValue.ToString() : null;
        }

        public void SetKeyValue(string path, string subKeyName, string subKeyValue)
        {
            Registry.SetValue(path, subKeyName, subKeyValue, RegistryValueKind.String);
        }

        public bool RegsitryExists(string securityId)
        {
            return Registry.Users.OpenSubKey(securityId) != null;
        }
    }
}
#endif