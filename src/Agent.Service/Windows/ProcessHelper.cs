using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AgentService
{
    public class ProcessHelper
    {
        private const int CTRL_C_EVENT = 0;
        
        public static void StopProcess(int processId)
        {            
            try
            {
                Process targetProcess = Process.GetProcessById(processId);
                if (targetProcess == null)
                {
                    throw new ArgumentException(String.Format("No process found with the given id - {0}", processId));
                }

                if (targetProcess.HasExited)
                {
                    EventLogger.WriteInfo(String.Format("The target process (Id - {0}) is not running at present", processId));
                    return;
                }

                // Try to let the agent process know that we are stopping
                //Attach service process to console of Agent.Listener process. This is needed,
                //because windows service doesn't use its own console.
                if (AttachConsole((uint)targetProcess.Id))
                {
                    //Prevent main service process from stopping because of Ctrl + C event with SetConsoleCtrlHandler
                    SetConsoleCtrlHandler(null, true);
                    try
                    {
                        //Generate console event for current console with GenerateConsoleCtrlEvent (processGroupId should be zero)
                        GenerateConsoleCtrlEvent(CTRL_C_EVENT, 0);
                        //Wait for the process to finish (give it up to 30 seconds)
                        targetProcess.WaitForExit(30000);
                    }
                    finally
                    {
                        //Disconnect from console and restore Ctrl+C handling by main process
                        FreeConsole();
                        SetConsoleCtrlHandler(null, false);
                    }
                }

                // if agent is still running, kill it
                if (!targetProcess.HasExited)
                {
                    EventLogger.WriteInfo(String.Format("Win32 error code for Ctrl+C execution - {0}", Marshal.GetLastWin32Error()));
                    EventLogger.WriteInfo("Process didnt exit after 30 seconds of sending Ctrl+C, killing it now");
                    targetProcess.Kill();
                }
            }
            catch (Exception exception)
            {
                // InvalidOperationException is thrown when there is no process associated to the process object. 
                // There is no process to kill, Log the exception and shutdown the service. 
                // If we don't handle this here, the service get into a state where it can neither be stoped nor restarted (Error 1061)
                EventLogger.WriteException(exception);
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        private static extern bool FreeConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate HandlerRoutine, bool Add);

        // Delegate type to be used as the Handler Routine for SetConsoleCtrlHandler
        delegate Boolean ConsoleCtrlDelegate(uint CtrlType);
    }
}