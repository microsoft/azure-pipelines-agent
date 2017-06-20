#if OS_WINDOWS
using System;
using System.Collections.Generic;
using System.Security.Principal;
using Microsoft.Win32;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Microsoft.VisualStudio.Services.Agent.Listener.Configuration
{
    public class InteractiveSessionRegistryManager
    {
        private IWindowsRegistryManager _registryManager;
        private string _userSecurityId;
        List<KeyValuePair<WellKnownRegistries, string>> _standardRegistries;

        public InteractiveSessionRegistryManager(IWindowsRegistryManager regManager, string sid = null)
        {
            _registryManager = regManager;
            _userSecurityId = sid;
            InitializeStandardRegistrySettings();
        }

        private void InitializeStandardRegistrySettings()
        {
            _standardRegistries = new List<KeyValuePair<WellKnownRegistries, string>>()
            {
                new KeyValuePair<WellKnownRegistries, string>(WellKnownRegistries.ScreenSaver, "0"),
                new KeyValuePair<WellKnownRegistries, string>(WellKnownRegistries.ScreenSaverDomainPolicy, "0"),
                new KeyValuePair<WellKnownRegistries, string>(WellKnownRegistries.ShutdownReason, "0"),
                new KeyValuePair<WellKnownRegistries, string>(WellKnownRegistries.ShutdownReasonUI, "0"),
                new KeyValuePair<WellKnownRegistries, string>(WellKnownRegistries.LegalNoticeCaption, ""),
                new KeyValuePair<WellKnownRegistries, string>(WellKnownRegistries.LegalNoticeText, "")
            };
        }

        public bool ValidateIfRegistryExistsForTheUser(string sid)
        {
            return _registryManager.RegsitryExists(sid);
        }

        public void UpdateStandardRegistrySettings()
        {
            foreach(var regSetting in _standardRegistries)
            {
                SetRegistryKeyValue(regSetting.Key, regSetting.Value);
            }
        }

        public void RevertBackOriginalRegistrySettings()
        {
            foreach(var regSetting in _standardRegistries)
            {
                RevertBackOriginalRegistry(regSetting.Key);
            }

            //auto-logon
            RevertBackOriginalRegistry(WellKnownRegistries.AutoLogonUserName);
            RevertBackOriginalRegistry(WellKnownRegistries.AutoLogonDomainName);
            RevertBackOriginalRegistry(WellKnownRegistries.AutoLogonPassword);
            RevertBackOriginalRegistry(WellKnownRegistries.AutoLogonCount);
            RevertBackOriginalRegistry(WellKnownRegistries.AutoLogon);

            //startup process
            RevertBackOriginalRegistry(WellKnownRegistries.StartupProcess);
        }

        public void UpdateAutoLogonSettings(string userName, string domainName)
        {
            SetRegistryKeyValue(WellKnownRegistries.AutoLogonUserName, userName);
            SetRegistryKeyValue(WellKnownRegistries.AutoLogonDomainName, domainName);

            //this call is to take the backup of the password key if already exists as we delete the key in the next step
            SetRegistryKeyValue(WellKnownRegistries.AutoLogonPassword, "");
            DeleteRegistry(WellKnownRegistries.AutoLogonPassword);

            //this call is to take the backup of the password key if already exists as we delete the key in the next step
            SetRegistryKeyValue(WellKnownRegistries.AutoLogonCount, "");
            DeleteRegistry(WellKnownRegistries.AutoLogonCount);

            SetRegistryKeyValue(WellKnownRegistries.AutoLogon, "1");
        }

        public string GetAutoLogonUserName()
        {
            var regValue = GetRegistryKeyValue(WellKnownRegistries.AutoLogon);
            int.TryParse(regValue, out int autoLogonEnabled);

            return autoLogonEnabled == 1
                    ? GetRegistryKeyValue(WellKnownRegistries.AutoLogonUserName)
                    : null;
        }

        public void SetStartupProcessCommand(string startupCommand)
        {            
            SetRegistryKeyValue(WellKnownRegistries.StartupProcess, startupCommand);
        }

        public string GetStartupProcessCommand()
        {            
            return GetRegistryKeyValue(WellKnownRegistries.StartupProcess);
        }

        public List<string> GetInteractiveSessionRelatedWarningsIfAny()
        {
            var warningReasons = new List<string>();

            //screen saver
            var screenSaverValue = GetRegistryKeyValue(WellKnownRegistries.ScreenSaverDomainPolicy);
            int.TryParse(screenSaverValue, out int isScreenSaverDomainPolicySet);
            if (isScreenSaverDomainPolicySet == 1)
            {
                warningReasons.Add(StringUtil.Loc("UITestingWarning_ScreenSaver"));
            }

            //shutdown reason
            var shutdownReasonValue = GetRegistryKeyValue(WellKnownRegistries.ShutdownReason);
            int.TryParse(shutdownReasonValue, out int shutdownReasonOn);
            if (shutdownReasonOn == 1)
            {
                warningReasons.Add(StringUtil.Loc("UITestingWarning_ShutdownReason"));
            }

            //legal caption/text
            var legalNoticeCaption = GetRegistryKeyValue(WellKnownRegistries.LegalNoticeCaption);
            var legalNoticeText =  GetRegistryKeyValue(WellKnownRegistries.LegalNoticeText);
            if (!string.IsNullOrEmpty(legalNoticeCaption) || !string.IsNullOrEmpty(legalNoticeText))
            {
                warningReasons.Add(StringUtil.Loc("UITestingWarning_LegalNotice"));
            }

            //auto-logon
            var autoLogonCountValue = GetRegistryKeyValue(WellKnownRegistries.AutoLogonCount);            
            if (!string.IsNullOrEmpty(autoLogonCountValue))
            {
                warningReasons.Add(StringUtil.Loc("UITestingWarning_AutoLogonCount"));
            }
            
            return warningReasons;
        }

        public void FetchAutoLogonUserDetails(out string userName, out string domainName)
        {
            userName = null;
            domainName = null;

            var regValue = GetRegistryKeyValue(WellKnownRegistries.AutoLogon);
            int.TryParse(regValue, out int autoLogonEnabled);
            if (autoLogonEnabled == 1)
            {
                userName = GetRegistryKeyValue(WellKnownRegistries.AutoLogonUserName);
                domainName = GetRegistryKeyValue(WellKnownRegistries.AutoLogonDomainName);
            }
        }

        private void SetRegistryKeyValue(WellKnownRegistries targetRegistry, string keyValue)
        {
            TakeBackupIfNeeded(targetRegistry);
            SetRegistryKeyInternal(targetRegistry, keyValue);
        }

        private string GetRegistryKeyValue(WellKnownRegistries targetRegistry)
        {
            var regPath = GetRegistryKeyPath(targetRegistry, _userSecurityId);
            if (string.IsNullOrEmpty(regPath))
            {
                return null;
            }
            
            var regKey = RegistryConstants.GetActualKeyNameForWellKnownRegistry(targetRegistry);
            return _registryManager.GetKeyValue(regPath, regKey);
        }

        private void DeleteRegistry(WellKnownRegistries targetRegistry, bool deleteBackupKey = false)
        {
            var regKeyName = deleteBackupKey 
                                ? RegistryConstants.GetBackupKeyName(targetRegistry) 
                                : RegistryConstants.GetActualKeyNameForWellKnownRegistry(targetRegistry);

            switch (targetRegistry)
            {
                //user specific registry settings
                case WellKnownRegistries.ScreenSaver :
                    _registryManager.DeleteKey(RegistryScope.CurrentUser, RegistryConstants.RegPaths.ScreenSaver, regKeyName);
                    break;
                case WellKnownRegistries.ScreenSaverDomainPolicy:
                    _registryManager.DeleteKey(RegistryScope.CurrentUser, RegistryConstants.RegPaths.ScreenSaverDomainPolicy, regKeyName);
                    break;
                case WellKnownRegistries.StartupProcess:
                    _registryManager.DeleteKey(RegistryScope.CurrentUser, RegistryConstants.RegPaths.StartupProcess, regKeyName);
                    break;

                //machine specific registry settings         
                case WellKnownRegistries.AutoLogon :
                case WellKnownRegistries.AutoLogonUserName:
                case WellKnownRegistries.AutoLogonDomainName :
                case WellKnownRegistries.AutoLogonPassword:
                case WellKnownRegistries.AutoLogonCount:
                    _registryManager.DeleteKey(RegistryScope.LocalMachine, RegistryConstants.RegPaths.AutoLogon, regKeyName);
                    break;

                case WellKnownRegistries.ShutdownReason :
                case WellKnownRegistries.ShutdownReasonUI :
                    _registryManager.DeleteKey(RegistryScope.LocalMachine, RegistryConstants.RegPaths.ShutdownReasonDomainPolicy, regKeyName);
                    break;
                case WellKnownRegistries.LegalNoticeCaption :
                case WellKnownRegistries.LegalNoticeText :
                    _registryManager.DeleteKey(RegistryScope.LocalMachine, RegistryConstants.RegPaths.LegalNotice, regKeyName);
                    break;
                default:
                   throw new InvalidOperationException("Invalid registry key to delete");
            }
        }

        private void RevertBackOriginalRegistry(WellKnownRegistries targetRegistry)
        {
            var regPath = GetRegistryKeyPath(targetRegistry, _userSecurityId);
            RevertBackOriginalRegistryInternal(regPath, targetRegistry);
        }

        private string GetRegistryKeyPath(WellKnownRegistries targetRegistry, string userSid = null)
        {
            var userHivePath = GetUserRegistryRootPath(userSid);
            switch (targetRegistry)
            {
                //user specific registry settings
                case WellKnownRegistries.ScreenSaver :
                    return string.Format($@"{userHivePath}\{RegistryConstants.RegPaths.ScreenSaver}");

                case WellKnownRegistries.ScreenSaverDomainPolicy:
                    return string.Format($@"{userHivePath}\{RegistryConstants.RegPaths.ScreenSaverDomainPolicy}");

                case WellKnownRegistries.StartupProcess:
                    return string.Format($@"{userHivePath}\{RegistryConstants.RegPaths.StartupProcess}");

                //machine specific registry settings         
                case WellKnownRegistries.AutoLogon :
                case WellKnownRegistries.AutoLogonUserName:
                case WellKnownRegistries.AutoLogonDomainName :
                case WellKnownRegistries.AutoLogonPassword:
                case WellKnownRegistries.AutoLogonCount:
                    return string.Format($@"{RegistryConstants.LocalMachineRootPath}\{RegistryConstants.RegPaths.AutoLogon}");

                case WellKnownRegistries.ShutdownReason :
                case WellKnownRegistries.ShutdownReasonUI :
                    return string.Format($@"{RegistryConstants.LocalMachineRootPath}\{RegistryConstants.RegPaths.ShutdownReasonDomainPolicy}");

                case WellKnownRegistries.LegalNoticeCaption :
                case WellKnownRegistries.LegalNoticeText :
                    return string.Format($@"{RegistryConstants.LocalMachineRootPath}\{RegistryConstants.RegPaths.LegalNotice}");
                default:
                   return null;
            }
        }

        private void RevertBackOriginalRegistryInternal(string regPath, WellKnownRegistries targetRegistry)
        {
            var originalKeyName = RegistryConstants.GetActualKeyNameForWellKnownRegistry(targetRegistry);
            var backupKeyName = RegistryConstants.GetBackupKeyName(targetRegistry);

            var originalValue = _registryManager.GetKeyValue(regPath, backupKeyName);            
            if (string.IsNullOrEmpty(originalValue))
            {
                DeleteRegistry(targetRegistry);
                return;
            }

            //revert back the original value
            _registryManager.SetKeyValue(regPath, originalKeyName, originalValue);
            //delete the backup key
            DeleteRegistry(targetRegistry, true);
        }
        
        private void SetRegistryKeyInternal(WellKnownRegistries targetRegistry, string keyValue)
        {
            var regPath = GetRegistryKeyPath(targetRegistry, _userSecurityId);
            var regKeyName = RegistryConstants.GetActualKeyNameForWellKnownRegistry(targetRegistry);
            _registryManager.SetKeyValue(regPath, regKeyName, keyValue);
        }

        private string GetUserRegistryRootPath(string sid)
        {
            return string.IsNullOrEmpty(sid) ?
                RegistryConstants.CurrentUserRootPath :
                String.Format(RegistryConstants.DifferentUserRootPath, sid);
        }

        private void TakeBackupIfNeeded(WellKnownRegistries registry)
        {
            string origValue = GetRegistryKeyValue(registry);
            if (!string.IsNullOrEmpty(origValue))
            {
                var regPath = GetRegistryKeyPath(registry, _userSecurityId);
                var backupKeyName = RegistryConstants.GetBackupKeyName(registry);
                _registryManager.SetKeyValue(regPath, backupKeyName, origValue);
            }
        }        
    }
    
    public enum WellKnownRegistries
    {
        ScreenSaver,
        ScreenSaverDomainPolicy,
        AutoLogon,
        AutoLogonUserName,
        AutoLogonDomainName,
        AutoLogonCount,
        AutoLogonPassword,
        StartupProcess,
        ShutdownReason,
        ShutdownReasonUI,
        LegalNoticeCaption,
        LegalNoticeText
    }

    public enum RegistryScope
    {
        CurrentUser,
        LocalMachine
    }

    public class RegistryConstants
    {
        public const string CurrentUserRootPath = @"HKEY_CURRENT_USER";
        public const string LocalMachineRootPath = @"HKEY_LOCAL_MACHINE";
        public const string DifferentUserRootPath = @"HKEY_USERS\{0}";
        public const string BackupKeyPrefix = "VSTSAgentBackup_";

        public struct RegPaths
        {
            public const string ScreenSaver = @"Control Panel\Desktop";
            public const string ScreenSaverDomainPolicy = @"Software\Policies\Microsoft\Windows\Control Panel\Desktop";
            public const string StartupProcess = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
            public const string AutoLogon = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";
            public const string ShutdownReasonDomainPolicy = @"SOFTWARE\Policies\Microsoft\Windows NT\Reliability";
            public const string LegalNotice = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";
        }

        public struct KeyNames
        {
            public const string ScreenSaver = "ScreenSaveActive";
            public const string AutoLogon = "AutoAdminLogon";
            public const string AutoLogonUserName = "DefaultUserName";
            public const string AutoLogonDomainName = "DefaultDomainName";
            public const string AutoLogonCount = "AutoLogonCount";
            public const string AutoLogonPassword = "DefaultPassword";
            public const string StartupProcess = "VSTSAgent";
            public const string ShutdownReason = "ShutdownReasonOn";
            public const string ShutdownReasonUI = "ShutdownReasonUI";
            public const string LegalNoticeCaption = "legalnoticecaption";
            public const string LegalNoticeText = "legalnoticetext";
        }

        public static string GetActualKeyNameForWellKnownRegistry(WellKnownRegistries registry)
        {
            switch(registry)
            {
                case WellKnownRegistries.ScreenSaverDomainPolicy:
                case WellKnownRegistries.ScreenSaver:
                    return KeyNames.ScreenSaver;
                case WellKnownRegistries.AutoLogon:
                    return KeyNames.AutoLogon;
                case WellKnownRegistries.AutoLogonUserName :
                    return KeyNames.AutoLogonUserName;
                case WellKnownRegistries.AutoLogonDomainName:
                    return KeyNames.AutoLogonDomainName;
                case WellKnownRegistries.AutoLogonCount:
                    return KeyNames.AutoLogonCount;
                case WellKnownRegistries.AutoLogonPassword:
                    return KeyNames.AutoLogonPassword;
                case WellKnownRegistries.StartupProcess:
                    return KeyNames.StartupProcess;
                case WellKnownRegistries.ShutdownReason:
                    return KeyNames.ShutdownReason;
                case WellKnownRegistries.ShutdownReasonUI:
                    return KeyNames.ShutdownReasonUI;
                case WellKnownRegistries.LegalNoticeCaption:
                    return KeyNames.LegalNoticeCaption;
                case WellKnownRegistries.LegalNoticeText:
                    return KeyNames.LegalNoticeText;
                default:
                    return null;
            }                
        }

        public static string GetBackupKeyName(WellKnownRegistries registry)
        {
            return string.Concat(RegistryConstants.BackupKeyPrefix, GetActualKeyNameForWellKnownRegistry(registry));
        }
    }
}
#endif