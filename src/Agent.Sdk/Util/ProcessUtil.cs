// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;

namespace Microsoft.VisualStudio.Services.Agent.Util
{
    internal class Interop
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct PROCESS_BASIC_INFORMATION
        {
            internal IntPtr ExitStatus;
            internal IntPtr PebBaseAddress;
            internal IntPtr AffinityMask;
            internal IntPtr BasePriority;
            internal IntPtr UniqueProcessId;
            internal IntPtr InheritedFromUniqueProcessId;
        }

        [DllImport("ntdll.dll", SetLastError = true)]
        internal static extern int NtQueryInformationProcess(IntPtr processHandle, int processInformationClass, out PROCESS_BASIC_INFORMATION processInformation, int processInformationLength, out int returnLength);
    }

    public static class WindowsProcessUtil
    {
        private const string ParentProcessState = nameof(ParentProcessState);

        internal static int? GetParentProcessId(IntPtr handle)
        {
            Interop.PROCESS_BASIC_INFORMATION pbi;
            int returnLength;
            int status = Interop.NtQueryInformationProcess(handle, 0, out pbi, Marshal.SizeOf<Interop.PROCESS_BASIC_INFORMATION>(), out returnLength);

            if (status != 0)
            {
                return null;
            }

            int parentProcessId = pbi.InheritedFromUniqueProcessId.ToInt32();

            return parentProcessId;
        }

        internal static (Process process, Dictionary<string, string> telemetry) GetParentProcess(Process process)
        {
            var telemetry = new Dictionary<string, string>();

            try
            {
                int? parentProcessId = GetParentProcessId(process.Handle);

                if (parentProcessId == null)
                {
                    return (null, telemetry);
                }

                Process parentProcess = Process.GetProcessById(parentProcessId.Value);

                if (parentProcess == null
                    || parentProcess.StartTime > process.StartTime
                    || parentProcess.HasExited)
                {
                    return (null, telemetry);
                }

                return (parentProcess, telemetry);
            }
            catch (ArgumentException)
            {
                telemetry.Add(ParentProcessState, "Invalid argument for GetProcessById");
                return (null, telemetry);
            }
            catch (Win32Exception)
            {
                telemetry.Add(ParentProcessState, "Win32 exception: could not retrieve parent process");
                return (null, telemetry);
            }
        }

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

        public static (List<Process> processes, Dictionary<string, string> telemetry) GetProcessList(
            Process process,
            bool useInteropToFindParentProcess)
        {
            var processList = new List<Process>() { process };
            var telemetry = new Dictionary<string, string>();

            while (true)
            {
                Process lastProcess = processList.Last();
                Process parentProcess;

                if (useInteropToFindParentProcess)
                {
                    (parentProcess, telemetry) = GetParentProcess(lastProcess);
                }
                else
                {
                    parentProcess = GetParentProcess(lastProcess.Id);
                }

                if (parentProcess == null)
                {
                    return (processList, telemetry);
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

        public static (bool isRunningInPowerShell, Dictionary<string, string> telemetry) AgentIsRunningInPowerShell(bool useInteropKnob)
        {
            var (processList, telemetry) = GetProcessList(Process.GetCurrentProcess(), useInteropKnob);
            var isProcessRunningInPowerShell = processList.Exists(ProcessIsPowerShell);

            return (isProcessRunningInPowerShell, telemetry);
        }
    }
}
