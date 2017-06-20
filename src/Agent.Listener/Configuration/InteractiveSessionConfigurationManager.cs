#if OS_WINDOWS
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.TeamFoundation.DistributedTask.WebApi;

namespace Microsoft.VisualStudio.Services.Agent.Listener.Configuration
{
    [ServiceLocator(Default = typeof(InteractiveSessionConfigurationManager))]
    public interface IInteractiveSessionConfigurationManager : IAgentService
    {
        void Configure(CommandSettings command);
        void UnConfigure(int listenerProcessId);
        bool RestartNeeded();
        bool IsInteractiveSessionConfigured();
    }

    public class InteractiveSessionConfigurationManager : AgentService, IInteractiveSessionConfigurationManager
    {
        private ITerminal _terminal;

        public override void Initialize(IHostContext hostContext)
        {
            base.Initialize(hostContext);
            _terminal = hostContext.GetService<ITerminal>();
        }

        public void Configure(CommandSettings command)
        {
            var logonAccount = command.GetAutoLogonUserName();

            string domainName;
            string userName;

            GetAccountSegments(logonAccount, out domainName, out userName);

            if ((string.IsNullOrEmpty(domainName) || domainName.Equals(".", StringComparison.CurrentCultureIgnoreCase)) && !logonAccount.Contains("@"))
            {
                logonAccount = String.Format("{0}\\{1}", Environment.MachineName, userName);
            }

            Trace.Info("LogonAccount after transforming: {0}, user: {1}, domain: {2}", logonAccount, userName, domainName);

            string logonPassword = string.Empty;
            var windowsServiceHelper = HostContext.GetService<INativeWindowsServiceHelper>();
            while (true)
            {
                logonPassword = command.GetWindowsLogonPassword(logonAccount);
                if (windowsServiceHelper.IsValidCredential(domainName, userName, logonPassword))
                {
                    Trace.Info("Credential validation succeeded");
                    break;
                }
                
                if (command.Unattended)
                {
                    throw new SecurityException(StringUtil.Loc("InvalidWindowsCredential"));
                }
                    
                Trace.Info("Invalid credential entered");
                _terminal.WriteLine(StringUtil.Loc("InvalidWindowsCredential"));
            }

            bool isCurrentUserSameAsAutoLogonUser = windowsServiceHelper.HasActiveSession(domainName, userName);                
            var securityIdForTheUser = windowsServiceHelper.GetSecurityId(domainName, userName);
            var regManager = HostContext.GetService<IWindowsRegistryManager>();
            
            InteractiveSessionRegistryManager regHelper = isCurrentUserSameAsAutoLogonUser 
                                                ? new InteractiveSessionRegistryManager(regManager)
                                                : new InteractiveSessionRegistryManager(regManager, securityIdForTheUser);
            
            if(!isCurrentUserSameAsAutoLogonUser && !regHelper.ValidateIfRegistryExistsForTheUser(securityIdForTheUser))
            {
                Trace.Error(String.Format($"The autologon user '{logonAccount}' doesnt have a user profile on the machine. Please login once with the expected autologon user and reconfigure the agent again"));
                throw new InvalidOperationException("No user profile exists for the AutoLogon user");
            }

            DisplayWarningsIfAny(regHelper);        
            UpdateRegistriesForInteractiveSession(regHelper, userName, domainName, logonPassword);
            ConfigurePowerOptions();
        }

        public bool RestartNeeded()
        {
            return !IsCurrentUserSameAsAutoLogonUser();
        }

        public void UnConfigure(int listenerProcessId)
        {
            /* When AutoLogon is enabled on the agent, the processes are launched in following manner
            AgentService.exe (in process mode) hosts (has handle of) Agent.Listener.exe
            To stop the agent gracefully we need to sent 'Ctrl+C' to Agent.Listener.exe so that it can stop itself (through CtrlCEventHandler)
            
            Approach 1- Propogating Ctrl+C from one process to the other
            If we implement the same CtrlC handler in AgentService.exe and let it know about the exit and then it can route Ctrl+C to Agent.listener.exe.
            Agent unconfiguration happens through Agent.Listener.exe (called with remove flag), lets call it UnInstaller process
            So UnInstaller process has to send Ctrl+C to AgentService.exe, to do so UnInstaller has to detach its own console
            which is not possible as "Config.cmd remove" call is run through the console and user is shown couple of messages.
            The similar issue occurs when AgentService.exe has to supply the Ctrl+C to Agent.Listener.exe

            Approach 2- Invoke AgentService.exe seperately with a specific argument
            We can invoke AgentService.exe with a specific argument and then it can send Ctrl+C to Agent.Lister.exe.
            If Ctrl+C fires up Agent.Listener.exe exits itself with '0' exit code.  AgentService.exe which is hosting the Agent.Listener.exe
            receives the exit code of the process and then it can stop itself based on the exit code.
            
            Given the limitaion of Approach 1, we are taking approach 2.
             */
            var agentServicePath = Path.Combine(HostContext.GetDirectory(WellKnownDirectory.Bin), "agentservice.exe");
            Trace.Info($"Stopping the agent listener. Process Id - {listenerProcessId}");

            using (var processInvoker = HostContext.CreateService<IProcessInvoker>())
            {
                processInvoker.ExecuteAsync(
                                workingDirectory: string.Empty,
                                fileName: agentServicePath,
                                arguments: string.Format("stopagentlistener {0}", listenerProcessId),
                                environment: null,
                                cancellationToken: CancellationToken.None);
            }

            Trace.Info("Reverting the registry settings now.");
            InteractiveSessionRegistryManager regHelper = new InteractiveSessionRegistryManager(HostContext.GetService<IWindowsRegistryManager>());
            regHelper.RevertBackOriginalRegistrySettings();
        }

        public bool IsInteractiveSessionConfigured()
        {
            //ToDo: Different user scenario
            
            //find out the path for startup process if it is same as current agent location, yes it was configured
            var regHelper = new InteractiveSessionRegistryManager(HostContext.GetService<IWindowsRegistryManager>(), null);
            var startupCommand = regHelper.GetStartupProcessCommand();

            if(string.IsNullOrEmpty(startupCommand))
            {
                return false;
            }

            var expectedStartupProcessDir = Path.Combine(HostContext.GetDirectory(WellKnownDirectory.Bin));
            return Path.GetDirectoryName(startupCommand).Equals(expectedStartupProcessDir, StringComparison.CurrentCultureIgnoreCase);
        }

        private void UpdateRegistriesForInteractiveSession(InteractiveSessionRegistryManager regHelper, string userName, string domainName, string logonPassword)
        {
            regHelper.UpdateStandardRegistrySettings();

            //auto logon
            ConfigureAutoLogon(regHelper, userName, domainName, logonPassword);

            //startup process
            var startupProcessPath = Path.Combine(HostContext.GetDirectory(WellKnownDirectory.Bin), "agent.service.exe");
            var startupCommand = string.Format($@"{startupProcessPath} runAsProcess");
            Trace.Verbose($"Setting startup command as {startupCommand}");

            regHelper.SetStartupProcessCommand(startupCommand);
        }

        private void ConfigureAutoLogon(InteractiveSessionRegistryManager regHelper, string userName, string domainName, string logonPassword)
        {
            //find out if the autologon was already enabled, show warning in that case
            ShowAutoLogonWarningIfAlreadyEnabled(regHelper, userName);

            var windowsHelper = HostContext.GetService<INativeWindowsServiceHelper>();
            windowsHelper.SetAutoLogonPassword(logonPassword);

            regHelper.UpdateAutoLogonSettings(userName, domainName);
        }

        private void ShowAutoLogonWarningIfAlreadyEnabled(InteractiveSessionRegistryManager regHelper, string userName)
        {
            regHelper.FetchAutoLogonUserDetails(out string autoLogonUserName, out string domainName);
            if(autoLogonUserName != null && !userName.Equals(autoLogonUserName, StringComparison.CurrentCultureIgnoreCase))
            {
                _terminal.WriteLine(string.Format(StringUtil.Loc("AutoLogonAlreadyEnabledWarning"), userName));
            }
        }

        private bool IsCurrentUserSameAsAutoLogonUser()
        {
            var regHelper = new InteractiveSessionRegistryManager(HostContext.GetService<IWindowsRegistryManager>());            
            regHelper.FetchAutoLogonUserDetails(out string userName, out string domainName);
            if(string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(domainName))
            {
                throw new InvalidOperationException("AutoLogon is not configured on the machine.");
            }

            var nativeWindowsHelper = HostContext.GetService<INativeWindowsServiceHelper>();
            return nativeWindowsHelper.HasActiveSession(domainName, userName);
        }

        private void DisplayWarningsIfAny(InteractiveSessionRegistryManager regHelper)
        {
            var warningReasons = regHelper.GetInteractiveSessionRelatedWarningsIfAny();
            if(warningReasons.Count > 0)
            {
                _terminal.WriteLine(StringUtil.Loc("UITestingWarning"));
                for(int i=0; i < warningReasons.Count; i++)
                {
                    _terminal.WriteLine(String.Format("{0} - {1}", (i+1).ToString(), warningReasons[i]));
                }
            }
        }

        private void ConfigurePowerOptions()
        {
            var whichUtil = HostContext.GetService<IWhichUtil>();
            var filePath = whichUtil.Which("powercfg.exe");
            string[] commands = new string[] {"/Change monitor-timeout-ac 0", "/Change monitor-timeout-dc 0"};

            foreach (var command in commands)
            {
                try
                {
                    Trace.Info($"Running powercfg.exe with {command}");
                    using (var processInvoker = HostContext.CreateService<IProcessInvoker>())
                    {
                        processInvoker.OutputDataReceived += delegate (object sender, ProcessDataReceivedEventArgs message)
                        {
                            Trace.Verbose(message.Data);
                        };

                        processInvoker.ErrorDataReceived += delegate (object sender, ProcessDataReceivedEventArgs message)
                        {
                            Trace.Error(message.Data);
                        };

                        processInvoker.ExecuteAsync(
                                workingDirectory: string.Empty,
                                fileName: filePath,
                                arguments: command,
                                environment: null,
                                cancellationToken: CancellationToken.None);
                    }
                }
                catch(Exception ex)
                {
                    //we will not stop the configuration. just show the warning and continue
                    _terminal.WriteLine(StringUtil.Loc("PowerOptionsConfigError"));
                    Trace.Error(ex);
                }
            }
        }

        //todo: move it to a utility class so that at other places it can be re-used
        private void GetAccountSegments(string account, out string domain, out string user)
        {
            string[] segments = account.Split('\\');
            domain = string.Empty;
            user = account;
            if (segments.Length == 2)
            {
                domain = segments[0];
                user = segments[1];
            }
        }
    }
}
#endif