// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Agent.Sdk
{
    public static class PlatformUtil
    {
        // System.Runtime.InteropServices.OSPlatform is a struct, so it is
        // not suitable for switch statements.
        public enum OS
        {
            Linux,
            OSX,
            Windows,
        }

        public static OS HostOS
        {
            get
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    return OS.Linux;
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    return OS.OSX;
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return OS.Windows;
                }

                throw new NotImplementedException($"Unsupported OS: {RuntimeInformation.OSDescription}");
            }
        }

        public static bool RunningOnWindows
        {
            get => PlatformUtil.HostOS == PlatformUtil.OS.Windows;
        }

        public static bool RunningOnMacOS
        {
            get => PlatformUtil.HostOS == PlatformUtil.OS.OSX;
        }

        public static bool RunningOnLinux
        {
            get => PlatformUtil.HostOS == PlatformUtil.OS.Linux;
        }

        public static bool RunningOnRHEL6
        {
            get
            {
                if (!(detectedRHEL6 is null))
                {
                    return (bool)detectedRHEL6;
                }

                DetectRHEL6();

                return (bool)detectedRHEL6;
            }
        }

        private static void DetectRHEL6()
        {
            lock (detectedRHEL6lock)
            {
                if (!RunningOnLinux || !File.Exists("/etc/redhat-release"))
                {
                    detectedRHEL6 = false;
                }
                else
                {
                    detectedRHEL6 = false;
                    try
                    {
                        string redhatVersion = File.ReadAllText("/etc/redhat-release");
                        if (redhatVersion.StartsWith("CentOS release 6.")
                            || redhatVersion.StartsWith("Red Hat Enterprise Linux Server release 6."))
                        {
                            detectedRHEL6 = true;
                        }
                    }
                    catch (IOException)
                    {
                        // IOException indicates we couldn't read that file; probably not RHEL6
                    }
                }
            }
        }

        private static bool? detectedRHEL6 = null;
        private static object detectedRHEL6lock = new object();

        public static Architecture HostArchitecture
        {
            get => RuntimeInformation.OSArchitecture;
        }

        public static bool IsX86
        {
            get => PlatformUtil.HostArchitecture == Architecture.X86;
        }

        public static bool IsX64
        {
            get => PlatformUtil.HostArchitecture == Architecture.X64;
        }

        public static bool IsArm
        {
            get => PlatformUtil.HostArchitecture == Architecture.Arm;
        }

        public static bool IsArm64
        {
            get => PlatformUtil.HostArchitecture == Architecture.Arm64;
        }

        // remove this after addressing
        // https://github.com/microsoft/azure-pipelines-agent/issues/2875
        public static bool UseLegacyHttpHandler
        {
            // In .NET Core 2.1, we couldn't use the new SocketsHttpHandler for Windows or Linux
            // On Linux, negotiate auth didn't work if the TFS URL was HTTPS
            // On Windows, proxy was not working
            // But on ARM/ARM64 Linux, the legacy curl dependency is problematic
            // (see https://github.com/dotnet/runtime/issues/28891)
            get => !(PlatformUtil.RunningOnLinux && (PlatformUtil.IsArm || PlatformUtil.IsArm64));
        }
    }
}