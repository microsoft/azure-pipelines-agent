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

        public static OS BuiltOnOS
        {
            get
            {
#if OS_LINUX
                return OS.Linux;
#elif OS_OSX
                return OS.OSX;
#elif OS_WINDOWS
                return OS.Windows;
#else
                #error Unknown OS
#endif
            }
        }

        public static OS BuiltForOS
        {
            get
            {
#if OS_LINUX
                return OS.Linux;
#elif OS_OSX
                return OS.OSX;
#elif OS_WINDOWS
                return OS.Windows;
#else
                #error Unknown OS
#endif
            }
        }

        public static OS RunningOnOS
        {
            get
            {
                // TODO: this should become a real runtime check
#if OS_LINUX
                return OS.Linux;
#elif OS_OSX
                return OS.OSX;
#elif OS_WINDOWS
                return OS.Windows;
#else
                #error Unknown OS
#endif
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

        public static Architecture BuiltOnArchitecture
        {
            get
            {
#if X86
                return Architecture.X86;
#elif X64
                return Architecture.X64;
#elif ARM
                return Architecture.Arm;
#elif ARM64            
                return Architecture.Arm64;
#else  
                #error Unknown Architecture
#endif
            }
        }

        public static Architecture BuiltForArchitecture
        {
            get
            {
#if X86
                return Architecture.X86;
#elif X64
                return Architecture.X64;
#elif ARM
                return Architecture.Arm;
#elif ARM64            
                return Architecture.Arm64;
#else  
                #error Unknown Architecture
#endif
            }
        }

        public static Architecture RunningOnArchitecture
        {
            get
            {
                // TODO: convert to runtime check
#if X86
                return Architecture.X86;
#elif X64
                return Architecture.X64;
#elif ARM
                return Architecture.Arm;
#elif ARM64            
                return Architecture.Arm64;
#else  
                #error Unknown Architecture
#endif
            }
        }
    }
}