// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;

namespace Microsoft.VisualStudio.Services.Agent.Util
{
    public static class WindowsProcessUtil
    {
        public static Process GetParentProcess(int processId)
        {
            using var query = new ManagementObjectSearcher($"SELECT * FROM Win32_Process WHERE ProcessId={processId}");

            ManagementObject process = query.Get().OfType<ManagementObject>().FirstOrDefault();

            if (process == null)
            {
                return null;
            }

            var parentProcessId = (int)(uint)process["ParentProcessId"];

            try
            {
                return Process.GetProcessById(parentProcessId);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        public static List<Process> GetProcessList(Process process)
        {
            var processList = new List<Process>(){ process };

            while (true)
            {
                int lastProcessId = processList.Last().Id;

                Process parentProcess = GetParentProcess(lastProcessId);

                if (parentProcess == null)
                {
                    return processList;
                }

                processList.Add(parentProcess);
            }
        }

        public static bool ProcessIsPowerShell(Process process)
        {
            try
            {
                // Getting process name can throw.
                string name = process.ProcessName.ToLower();

                return name == "pwsh" || name == "powershell";
            }
            catch
            {
                return false;
            }
        }

        public static bool ProcessIsRunningInPowerShell(Process process)
        {
            return GetProcessList(process).Exists(ProcessIsPowerShell);
        }

        public static bool AgentIsRunningInPowerShell()
        {
            return ProcessIsRunningInPowerShell(Process.GetCurrentProcess());
        }
    }
}
