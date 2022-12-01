// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Microsoft.VisualStudio.Services.Agent
{
    public static class WindowsEnvVarHelper
    {
        // Reference: https://blogs.msdn.microsoft.com/matt_pietrek/2004/08/25/reading-another-processs-environment/
        // Reference: http://blog.gapotchenko.com/eazfuscator.net/reading-environment-variables
        public static string GetEnvironmentVariable(Process process, IHostContext hostContext, string variable)
        {
            ArgUtil.NotNull(hostContext, nameof(hostContext));
            ArgUtil.NotNull(process, nameof(process));
            var trace = hostContext.GetTrace(nameof(WindowsEnvVarHelper));

            IntPtr processHandle = process.SafeHandle.DangerousGetHandle();

            IntPtr environmentBlockAddress;
            if (Environment.Is64BitProcess)
            {
                PROCESS_BASIC_INFORMATION64 pbi = new PROCESS_BASIC_INFORMATION64();
                int returnLength = 0;
                int status = NtQueryInformationProcess64(processHandle, PROCESSINFOCLASS.ProcessBasicInformation, ref pbi, Marshal.SizeOf(pbi), ref returnLength);
                if (status != 0)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                bool wow64;
                if (!IsWow64Process(processHandle, out wow64))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                if (!wow64)
                {
                    // 64 bits process on 64 bits OS
                    IntPtr UserProcessParameterAddress = ReadIntPtr64(processHandle, new IntPtr(pbi.PebBaseAddress) + 0x20);
                    environmentBlockAddress = ReadIntPtr64(processHandle, UserProcessParameterAddress + 0x80);
                }
                else
                {
                    // 32 bits process on 64 bits OS
                    IntPtr UserProcessParameterAddress = ReadIntPtr32(processHandle, new IntPtr(pbi.PebBaseAddress) + 0x1010);
                    environmentBlockAddress = ReadIntPtr32(processHandle, UserProcessParameterAddress + 0x48);
                }
            }
            else
            {
                PROCESS_BASIC_INFORMATION32 pbi = new PROCESS_BASIC_INFORMATION32();
                int returnLength = 0;
                int status = NtQueryInformationProcess32(processHandle, PROCESSINFOCLASS.ProcessBasicInformation, ref pbi, Marshal.SizeOf(pbi), ref returnLength);
                if (status != 0)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                // 32 bits process on 32 bits OS
                IntPtr UserProcessParameterAddress = ReadIntPtr32(processHandle, new IntPtr(pbi.PebBaseAddress) + 0x10);
                environmentBlockAddress = ReadIntPtr32(processHandle, UserProcessParameterAddress + 0x48);
            }

            MEMORY_BASIC_INFORMATION memInfo = new MEMORY_BASIC_INFORMATION();
            if (VirtualQueryEx(processHandle, environmentBlockAddress, ref memInfo, Marshal.SizeOf(memInfo)) == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            Int64 dataSize = memInfo.RegionSize.ToInt64() - (environmentBlockAddress.ToInt64() - memInfo.BaseAddress.ToInt64());

            byte[] envData = new byte[dataSize];
            IntPtr res_len = IntPtr.Zero;
            if (!ReadProcessMemory(processHandle, environmentBlockAddress, envData, new IntPtr(dataSize), ref res_len))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            if (res_len.ToInt64() != dataSize)
            {
                throw new ArgumentOutOfRangeException(nameof(ReadProcessMemory));
            }

            var environmentVariables = _EnvToDictionary(envData);

            if (environmentVariables.TryGetValue(variable, out string envVariable))
            {
                return envVariable;
            }
            else
            {
                return null;
            }
        }

        // Reference: https://github.com/gapotchenko/Gapotchenko.FX/blob/master/Source/Gapotchenko.FX.Diagnostics.Process/ProcessEnvironment.cs
        static Dictionary<String,String> _EnvToDictionary(byte[] env)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            int len = env.Length;
            if (len < 4)
                return result;

            int n = len - 3;
            for (int i = 0; i < n; ++i)
            {
                byte c1 = env[i];
                byte c2 = env[i + 1];
                byte c3 = env[i + 2];
                byte c4 = env[i + 3];

                if (c1 == 0 && c2 == 0 && c3 == 0 && c4 == 0)
                {
                    len = i + 3;
                    break;
                }
            }

            char[] environmentCharArray = Encoding.Unicode.GetChars(env, 0, len);

            for (int i = 0; i < environmentCharArray.Length; i++)
            {
                int startIndex = i;
                while ((environmentCharArray[i] != '=') && (environmentCharArray[i] != '\0'))
                {
                    i++;
                }
                if (environmentCharArray[i] != '\0')
                {
                    if ((i - startIndex) == 0)
                    {
                        while (environmentCharArray[i] != '\0')
                        {
                            i++;
                        }
                    }
                    else
                    {
                        string str = new string(environmentCharArray, startIndex, i - startIndex);
                        i++;
                        int num3 = i;
                        while (environmentCharArray[i] != '\0')
                        {
                            i++;
                        }
                        string str2 = new string(environmentCharArray, num3, i - num3);
                        result[str] = str2;
                    }
                }
            }

            return result;
        }

        private static IntPtr ReadIntPtr32(IntPtr hProcess, IntPtr ptr)
        {
            IntPtr readPtr = IntPtr.Zero;
            IntPtr data = Marshal.AllocHGlobal(sizeof(Int32));
            try
            {
                IntPtr res_len = IntPtr.Zero;
                if (!ReadProcessMemory(hProcess, ptr, data, new IntPtr(sizeof(Int32)), ref res_len))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                if (res_len.ToInt32() != sizeof(Int32))
                {
                    throw new ArgumentOutOfRangeException(nameof(ReadProcessMemory));
                }

                readPtr = new IntPtr(Marshal.ReadInt32(data));
            }
            finally
            {
                Marshal.FreeHGlobal(data);
            }

            return readPtr;
        }

        private static IntPtr ReadIntPtr64(IntPtr hProcess, IntPtr ptr)
        {
            IntPtr readPtr = IntPtr.Zero;
            IntPtr data = Marshal.AllocHGlobal(IntPtr.Size);
            try
            {
                IntPtr res_len = IntPtr.Zero;
                if (!ReadProcessMemory(hProcess, ptr, data, new IntPtr(sizeof(Int64)), ref res_len))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                if (res_len.ToInt32() != IntPtr.Size)
                {
                    throw new ArgumentOutOfRangeException(nameof(ReadProcessMemory));
                }

                readPtr = Marshal.ReadIntPtr(data);
            }
            finally
            {
                Marshal.FreeHGlobal(data);
            }

            return readPtr;
        }

        private enum PROCESSINFOCLASS : int
        {
            ProcessBasicInformation = 0
        };

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORY_BASIC_INFORMATION
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public int AllocationProtect;
            public IntPtr RegionSize;
            public int State;
            public int Protect;
            public int Type;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_BASIC_INFORMATION64
        {
            public long ExitStatus;
            public long PebBaseAddress;
            public long AffinityMask;
            public long BasePriority;
            public long UniqueProcessId;
            public long InheritedFromUniqueProcessId;
        };

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_BASIC_INFORMATION32
        {
            public int ExitStatus;
            public int PebBaseAddress;
            public int AffinityMask;
            public int BasePriority;
            public int UniqueProcessId;
            public int InheritedFromUniqueProcessId;
        };

        [DllImport("ntdll.dll", SetLastError = true, EntryPoint = "NtQueryInformationProcess")]
        private static extern int NtQueryInformationProcess64(IntPtr processHandle, PROCESSINFOCLASS processInformationClass, ref PROCESS_BASIC_INFORMATION64 processInformation, int processInformationLength, ref int returnLength);

        [DllImport("ntdll.dll", SetLastError = true, EntryPoint = "NtQueryInformationProcess")]
        private static extern int NtQueryInformationProcess32(IntPtr processHandle, PROCESSINFOCLASS processInformationClass, ref PROCESS_BASIC_INFORMATION32 processInformation, int processInformationLength, ref int returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool IsWow64Process(IntPtr processHandle, out bool wow64Process);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, IntPtr lpBuffer, IntPtr dwSize, ref IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, [Out] byte[] lpBuffer, IntPtr dwSize, ref IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll")]
        private static extern int VirtualQueryEx(IntPtr processHandle, IntPtr baseAddress, ref MEMORY_BASIC_INFORMATION memoryInformation, int memoryInformationLength);
    }
}