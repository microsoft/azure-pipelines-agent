// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;

namespace Microsoft.VisualStudio.Services.Agent.Util
{
    internal record ProcessInfo(int ProcessId, string ProcessName);

    public static class WindowsProcessUtil
    {
        internal static ProcessInfo GetParentProcessInformation(int processId)
        {
            using var query = new ManagementObjectSearcher($"SELECT ParentProcessId FROM Win32_Process WHERE ProcessId={processId}");

            using ManagementObjectCollection queryResult = query.Get();

            using ManagementObject foundProcess = queryResult.OfType<ManagementObject>().FirstOrDefault();

            if (foundProcess == null)
            {
                return null;
            }

            int parentProcessId = (int)(uint)foundProcess["ParentProcessId"];

            try
            {
                using var parentProcess = Process.GetProcessById(parentProcessId);
                return new(parentProcess.Id, parentProcess.ProcessName);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        internal static List<ProcessInfo> GetProcessList()
        {
            using var currentProcess = Process.GetCurrentProcess();
            var currentProcessInfo = new ProcessInfo(currentProcess.Id, currentProcess.ProcessName);

            var processes = new List<ProcessInfo>() { new(currentProcessInfo.ProcessId, currentProcessInfo.ProcessName) };

            const int maxSearchDepthForProcess = 10;

            while (processes.Count < maxSearchDepthForProcess)
            {
                ProcessInfo lastProcessInfo = processes.Last();
                ProcessInfo parentProcessInfo = GetParentProcessInformation(lastProcessInfo.ProcessId);

                if (parentProcessInfo == null)
                {
                    return processes;
                }

                processes.Add(parentProcessInfo);
            }

            return processes;
        }

        public static bool IsAgentRunningInPowerShellCore()
        {
            List<ProcessInfo> processList = GetProcessList();

            bool isProcessRunningInPowerShellCore = processList.Exists(process => process.ProcessName == "pwsh");

            return isProcessRunningInPowerShellCore;
        }
    }
}