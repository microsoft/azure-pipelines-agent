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

        public static OS RunningOnOS
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
            get => PlatformUtil.RunningOnOS == PlatformUtil.OS.Windows;
        }

        public static bool RunningOnMacOS
        {
            get => PlatformUtil.RunningOnOS == PlatformUtil.OS.OSX;
        }

        public static bool RunningOnLinux
        {
            get => PlatformUtil.RunningOnOS == PlatformUtil.OS.Linux;
        }

        public static bool RunningOnRHEL6
        {
            get
            {
                if (!RunningOnLinux || !File.Exists("/etc/redhat-release"))
                {
                    return false;
                }

                try
                {
                    string redhatVersion = File.ReadAllText("/etc/redhat-release");
                    if (redhatVersion.StartsWith("CentOS release 6.")
                        || redhatVersion.StartsWith("Red Hat Enterprise Linux Server release 6."))
                    {
                        return true;
                    }
                }
                catch (IOException)
                {
                    // IOException indicates we couldn't read that file; probably not RHEL6
                }

                return false;
            }
        }

        public static Architecture RunningOnArchitecture
        {
            get
            {
                return RuntimeInformation.OSArchitecture;
            }
        }
    }
}