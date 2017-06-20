using System;
using System.ServiceProcess;
using System.Diagnostics;
using System.ComponentModel;
using System.Reflection;
using System.Threading;

namespace AgentService
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        static int Main(String[] args)
        {
            if (args != null && args.Length >= 1)
            {
                EventLogger.WriteInfo(String.Format("Received Command - {0}", args[0]));

                if(args[0].Equals("init", StringComparison.InvariantCultureIgnoreCase))
                {
                    return SetupEventSource();
                }

                if(args[0].Equals("runAsProcess", StringComparison.InvariantCultureIgnoreCase))
                {
                    LaunchAgentListener();
                    return 0;
                }

                if(args[0].Equals("stopagentlistener", StringComparison.InvariantCultureIgnoreCase))
                {
                    if(args.Length > 1 && !string.IsNullOrEmpty(args[1]))
                    {
                        int pId = -1;
                        int.TryParse(args[1], out pId);
                        EventLogger.WriteInfo(String.Format("Received stopagentlistener command to stop process with Id - {0}", pId));
                        ProcessHelper.StopProcess(pId);
                    }
                    else
                    {
                        var ex = new Exception("Incorrect process id for AgentListener process");
                        EventLogger.WriteException(ex);
                        throw ex;
                    }
                    return 0;
                }
            }

            ServiceBase[] ServicesToRun;
            ServicesToRun = new ServiceBase[]
            {
                new AgentService(args.Length > 0 ? args[0] : "VstsAgentService")
            };
            ServiceBase.Run(ServicesToRun);

            return 0;
        }

        public static void LaunchAgentListener()
        {
            AgentListener agentListener = null;
            int returnCode = -1;
            while(returnCode != 0)
            {
                try
                {
                    agentListener = new AgentListener(ExecutionMode.Process);
                    EventLogger.WriteInfo("Starting VSTS Agent Process");
                    returnCode = agentListener.Run();
                    EventLogger.WriteInfo(string.Format("Agent.Listener.exe, return code - {0}", returnCode));
                    //waiting for sometime before resuming the Agent.Listener.exe
                    //this is just to make sure if there is any background work on the server
                    //and to not have listener process getting created very fast in case of some issue
                    Thread.Sleep(TimeSpan.FromSeconds(5));
                }
                catch(Exception ex)
                {
                    EventLogger.WriteException(ex);
                    if(agentListener != null)
                    {
                        agentListener.Stop();
                    }
                }
            }
            EventLogger.WriteInfo(string.Format("Stopping the AgentService.exe"));
        }

        public static int SetupEventSource()
        {
            // TODO: LOC all strings.
            if (!EventLog.Exists("Application"))
            {
                Console.WriteLine("[ERROR] Application event log doesn't exist on current machine.");
                return 1;
            }

            EventLog applicationLog = new EventLog("Application");
            if (applicationLog.OverflowAction == OverflowAction.DoNotOverwrite)
            {
                Console.WriteLine("[WARNING] The retention policy for Application event log is set to \"Do not overwrite events\".");
                Console.WriteLine("[WARNING] Make sure manually clear logs as needed, otherwise AgentService will stop writing output to event log.");
            }

            try
            {
                EventLog.WriteEntry(EventLogger.EventSourceName, "create event log trace source for vsts-agent service", EventLogEntryType.Information, 100);
                return 0;
            }
            catch (Win32Exception ex)
            {
                Console.WriteLine("[ERROR] Unable to create '{0}' event source under 'Application' event log.", EventLogger.EventSourceName);
                Console.WriteLine("[ERROR] {0}",ex.Message);
                Console.WriteLine("[ERROR] Error Code: {0}", ex.ErrorCode);
                return 1;
            }
        }
    }
}