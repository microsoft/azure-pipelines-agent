#if OS_WINDOWS
using Microsoft.VisualStudio.Services.Agent.Listener.Configuration;
using Microsoft.VisualStudio.Services.Agent.Listener;
using Microsoft.VisualStudio.Services.Agent.Util;
using Moq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Listener
{
    public sealed class AgentAutoLogonTestL0
    {
        private Mock<INativeWindowsServiceHelper> _windowsServiceHelper;
        private Mock<IPromptManager> _promptManager;
        private Mock<IProcessInvoker> _processInvoker;
        private CommandSettings _command;
        private string _sid = "007";
        private string _userName = "ironMan";
        private string _domainName = "avengers";
        private MockRegistryManager _mockRegManager;
        private bool _powerCfgCalledForACOption = false;
        private bool _powerCfgCalledForDCOption = false;
        private bool _stopListenerCalled = false;

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Agent")]
        public async void TestAutoLogonConfiguration()
        {
            using (var hc = new TestHostContext(this))
            {
                SetupTestEnv(hc);

                var iConfigManager = new InteractiveSessionConfigurationManager();
                iConfigManager.Initialize(hc);
                iConfigManager.Configure(_command);

                VerifyTheRegistryChanges(_userName, _domainName);
                Assert.True(_powerCfgCalledForACOption);
                Assert.True(_powerCfgCalledForDCOption);
                Assert.Equal(false, iConfigManager.RestartNeeded());
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Agent")]
        public async void TestAutoLogonConfigurationForDifferentUser()
        {
            using (var hc = new TestHostContext(this))
            {
                SetupTestEnv(hc);

                //override behavior
                _windowsServiceHelper.Setup(x => x.HasActiveSession(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

                var iConfigManager = new InteractiveSessionConfigurationManager();
                iConfigManager.Initialize(hc);
                iConfigManager.Configure(_command);
                
                VerifyTheRegistryChanges(_userName, _domainName, _sid);
                Assert.True(_powerCfgCalledForACOption);
                Assert.True(_powerCfgCalledForDCOption);
                Assert.Equal(true, iConfigManager.RestartNeeded());
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Agent")]
        public async void TestInteractiveSessionUnConfigure()
        {            
            //strategy-
            //1. fill some existing values in the registry
            //2. run configure
            //3. make sure the old values are there in the backup
            //4. unconfigure
            //5. make sure original values are reverted back

            using (var hc = new TestHostContext(this))
            {
                SetupTestEnv(hc);

                SetupRegistrySettings(hc);

                var iConfigManager = new InteractiveSessionConfigurationManager();
                iConfigManager.Initialize(hc);
                iConfigManager.Configure(_command);

                //make sure the backup was taken for the keys
                RegistryVerificationForUnConfigure(hc, checkBackupKeys:true);

                iConfigManager.UnConfigure(-1);

                //original values were reverted
                RegistryVerificationForUnConfigure(hc);
                Assert.True(_stopListenerCalled, "Stop listener was not called as part of unconfigure.");
            }
        }

        private void RegistryVerificationForUnConfigure(TestHostContext hc, string sid = null, bool checkBackupKeys=false)
        {
            var regManager = hc.GetService<IWindowsRegistryManager>();

            var userRegistryRootPath = string.IsNullOrEmpty(sid) 
                                        ? RegistryConstants.CurrentUserRootPath : String.Format(RegistryConstants.DifferentUserRootPath, sid);
            
            //screen saver (user specific)
            var screenSaverKeyPath = string.Format($@"{userRegistryRootPath}\{RegistryConstants.RegPaths.ScreenSaver}");
            var keyName = checkBackupKeys 
                            ? RegistryConstants.GetBackupKeyName(WellKnownRegistries.ScreenSaver)
                            : RegistryConstants.KeyNames.ScreenSaver;
            var screenSaverValue = regManager.GetKeyValue(screenSaverKeyPath, keyName);
            var expectedValue = "1";
            var validationPassed = string.Equals(expectedValue, screenSaverValue, StringComparison.OrdinalIgnoreCase);

            Assert.True(validationPassed, $"UnConfigure (verifying backupkeys - {checkBackupKeys}) : {WellKnownRegistries.ScreenSaver} Key value is not correct. Expected - {expectedValue} Actual - {screenSaverValue}");

            //autologon (machine wide)
            var autoLogonKeyPath = string.Format($@"{RegistryConstants.LocalMachineRootPath}\{RegistryConstants.RegPaths.AutoLogon}");
            keyName = checkBackupKeys 
                        ? RegistryConstants.GetBackupKeyName(WellKnownRegistries.AutoLogon)
                        : RegistryConstants.KeyNames.AutoLogon;

            var autoLogonValue = regManager.GetKeyValue(autoLogonKeyPath, keyName);
            expectedValue = "0";
            validationPassed = string.Equals(expectedValue, autoLogonValue, StringComparison.OrdinalIgnoreCase);
            Assert.True(validationPassed, $"UnConfigure (verifying backupkeys - {checkBackupKeys}) : {WellKnownRegistries.AutoLogon} Key value is not correct. Expected - {expectedValue} Actual - {autoLogonValue}");

            //autologon password (delete key)
            keyName = checkBackupKeys 
                        ? RegistryConstants.GetBackupKeyName(WellKnownRegistries.AutoLogonPassword)
                        : RegistryConstants.KeyNames.AutoLogonPassword;
            var autologonPwdValue = regManager.GetKeyValue(autoLogonKeyPath, keyName);
            expectedValue = "xyz";
            validationPassed = string.Equals(expectedValue, autologonPwdValue, StringComparison.OrdinalIgnoreCase);
            Assert.True(validationPassed, $"UnConfigure (verifying backupkeys - {checkBackupKeys}) : {WellKnownRegistries.AutoLogonPassword} Key value is not correct. Expected - {expectedValue} Actual - {autologonPwdValue}");
        }

        private void SetupRegistrySettings(TestHostContext hc, string sid = null)
        {
            var regManager = hc.GetService<IWindowsRegistryManager>();

            var userRegistryRootPath = string.IsNullOrEmpty(sid) 
                                        ? RegistryConstants.CurrentUserRootPath : String.Format(RegistryConstants.DifferentUserRootPath, sid);
            
            //screen saver (user specific)
            var screenSaverKeyPath = string.Format($@"{userRegistryRootPath}\{RegistryConstants.RegPaths.ScreenSaver}");
            regManager.SetKeyValue(screenSaverKeyPath, RegistryConstants.KeyNames.ScreenSaver, "1");            

            //autologon (machine wide)
            var autoLogonKeyPath = string.Format($@"{RegistryConstants.LocalMachineRootPath}\{RegistryConstants.RegPaths.AutoLogon}");
            regManager.SetKeyValue(autoLogonKeyPath, RegistryConstants.KeyNames.AutoLogon, "0");

            //autologon password (delete key)            
            regManager.SetKeyValue(autoLogonKeyPath, RegistryConstants.KeyNames.AutoLogonPassword , "xyz");
        }

        private void SetupTestEnv(TestHostContext hc)
        {
            _powerCfgCalledForACOption = _powerCfgCalledForDCOption = false;
            _stopListenerCalled = false;

            _windowsServiceHelper = new Mock<INativeWindowsServiceHelper>();
            hc.SetSingleton<INativeWindowsServiceHelper>(_windowsServiceHelper.Object);

            _promptManager = new Mock<IPromptManager>();
            hc.SetSingleton<IPromptManager>(_promptManager.Object);

            hc.SetSingleton<IWhichUtil>(new WhichUtil());

            _promptManager
                .Setup(x => x.ReadValue(
                    Constants.Agent.CommandLine.Args.WindowsLogonAccount, // argName
                    It.IsAny<string>(), // description
                    It.IsAny<bool>(), // secret
                    It.IsAny<string>(), // defaultValue
                    Validators.NTAccountValidator, // validator
                    It.IsAny<bool>())) // unattended
                .Returns(string.Format(@"{0}\{1}", _domainName, _userName));

            _windowsServiceHelper.Setup(x => x.IsValidCredential(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(true);
            _windowsServiceHelper.Setup(x => x.SetAutoLogonPassword(It.IsAny<string>()));
            _windowsServiceHelper.Setup(x => x.HasActiveSession(It.IsAny<string>(), It.IsAny<string>())).Returns(true);             
            _windowsServiceHelper.Setup(x => x.GetSecurityId(It.IsAny<string>(), It.IsAny<string>())).Returns(_sid);

            _processInvoker = new Mock<IProcessInvoker>();
            hc.EnqueueInstance<IProcessInvoker>(_processInvoker.Object);
            hc.EnqueueInstance<IProcessInvoker>(_processInvoker.Object);
            hc.EnqueueInstance<IProcessInvoker>(_processInvoker.Object);
            _processInvoker.Setup(x => x.ExecuteAsync(
                                                It.IsAny<String>(), 
                                                "powercfg.exe", 
                                                "/Change monitor-timeout-ac 0", 
                                                null,
                                                It.IsAny<CancellationToken>())).Returns(Task.FromResult<int>(SetPowerCfgFlags(true)));

            _processInvoker.Setup(x => x.ExecuteAsync(
                                                It.IsAny<String>(), 
                                                "powercfg.exe", 
                                                "/Change monitor-timeout-dc 0", 
                                                null,
                                                It.IsAny<CancellationToken>())).Returns(Task.FromResult<int>(SetPowerCfgFlags(false)));

            _processInvoker.Setup(x => x.ExecuteAsync(
                                                It.IsAny<String>(),
                                                It.IsAny<String>(),
                                                "stopagentlistener -1",
                                                null,
                                                It.IsAny<CancellationToken>())).Returns(Task.FromResult<int>(CallStopListener()));

            _mockRegManager = new MockRegistryManager();
            hc.SetSingleton<IWindowsRegistryManager>(_mockRegManager);

            _command = new CommandSettings(
                hc,
                new[]
                {
                    "--windowslogonaccount", "wont be honored",
                    "--windowslogonpassword", "sssh"
                });
        }

        private int SetPowerCfgFlags(bool isForACOption)
        {
            if(isForACOption)
            {
                _powerCfgCalledForACOption = true;
            }
            else
            {
                _powerCfgCalledForDCOption = true;
            }
            return 0;
        }

        private int CallStopListener()
        {
            _stopListenerCalled = true;
            return 0;
        }

        public void VerifyTheRegistryChanges(string expectedUserName, string expectedDomainName, string userSid = null)
        {
            ValidateRegistryValue(WellKnownRegistries.ScreenSaver, "0", userSid);
            ValidateRegistryValue(WellKnownRegistries.ScreenSaverDomainPolicy, "0", userSid);
            
            //autologon
            ValidateRegistryValue(WellKnownRegistries.AutoLogon, "1", userSid);
            ValidateRegistryValue(WellKnownRegistries.AutoLogonUserName, expectedUserName, userSid);
            ValidateRegistryValue(WellKnownRegistries.AutoLogonDomainName, expectedDomainName, userSid);
            ValidateRegistryValue(WellKnownRegistries.AutoLogonCount, null, userSid);
            ValidateRegistryValue(WellKnownRegistries.AutoLogonPassword, null, userSid);

            //shutdown reason
            ValidateRegistryValue(WellKnownRegistries.ShutdownReason, "0", userSid);
            ValidateRegistryValue(WellKnownRegistries.ShutdownReasonUI, "0", userSid);

            //todo: startup process validation
            // ValidateRegistryValue(WellKnownRegistries.StartupProcess, , usersid);

            // regPath = GetRegistryKeyPath(WellKnownRegistries.StartupProcess, userSid);
            // var processPath = regManager.GetKeyValue(regPath, WellKnownRegistries.StartupProcess);
        }

        public void ValidateRegistryValue(WellKnownRegistries registry, string expectedValue, string userSid)
        {
            var regPath = GetRegistryKeyPath(registry, userSid);
            var regKey = RegistryConstants.GetActualKeyNameForWellKnownRegistry(registry);
            var actualValue = _mockRegManager.GetKeyValue(regPath, regKey);

            var validationPassed = string.Equals(expectedValue, actualValue, StringComparison.OrdinalIgnoreCase);
            Assert.True(validationPassed, $"{registry.ToString()} validation failed. Expected - {expectedValue} Actual - {actualValue}");
        }

        private string GetUserRegistryRootPath(string sid)
        {
            return string.IsNullOrEmpty(sid) ?
                RegistryConstants.CurrentUserRootPath :
                String.Format(RegistryConstants.DifferentUserRootPath, sid);
        }

        private string GetRegistryKeyPath(WellKnownRegistries targetRegistryKey, string userSid = null)
        {
            var userHivePath = GetUserRegistryRootPath(userSid);
            switch(targetRegistryKey)
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
    }

    public class MockRegistryManager : AgentService, IWindowsRegistryManager
    {
        private Dictionary<string, string> _regStore;

        public MockRegistryManager()
        {
            _regStore = new Dictionary<string, string>();
        }
        
        public void DeleteKey(RegistryScope scope, string path, string keyName)
        {
            var completePath = path;
            switch(scope)
            {
                case RegistryScope.CurrentUser :
                    completePath = string.Format($@"{RegistryConstants.CurrentUserRootPath}\{path}");
                    break;
                case RegistryScope.LocalMachine:
                    completePath = string.Format($@"{RegistryConstants.LocalMachineRootPath}\{path}");
                    break;
                default:
                    throw new InvalidOperationException("wrong scope");
            }

            var key = string.Concat(completePath, keyName);
            _regStore.Remove(key);
        }

        public string GetKeyValue(string path, string keyName)
        {
            var key = string.Concat(path, keyName);
            return _regStore.ContainsKey(key) ? _regStore[key] : null;
        }

        public void SetKeyValue(string path, string keyName, string keyValue)
        {
            var key = string.Concat(path, keyName);
            if(_regStore.ContainsKey(key))
            {
                _regStore[key] = keyValue;
            }
            else
            {
                _regStore.Add(key, keyValue);
            }
        }

        public bool RegsitryExists(string securityId)
        {
            return true;
        }
    }
}
#endif