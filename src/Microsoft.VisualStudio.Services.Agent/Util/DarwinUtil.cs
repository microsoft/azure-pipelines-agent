using System;
using System.Runtime.InteropServices;

namespace Microsoft.VisualStudio.Services.Agent.Util
{
    public sealed class DarwinUtil
    {
        private const int CTL_KERN = 1;
        private const int KERN_OSRELEASE = 2;

        private static string GetKernelRelease()
        {
            unsafe
            {
                const uint BUFFER_LENGTH = 32;

                var name = stackalloc int[2];
                name[0] = CTL_KERN;
                name[1] = KERN_OSRELEASE;

                var buf = stackalloc byte[(int)BUFFER_LENGTH];
                var len = stackalloc uint[1];
                *len = BUFFER_LENGTH;

                try
                {
                    // If the buffer isn't big enough, it seems sysctl still returns 0 and just sets len to the
                    // necessary buffer size. This appears to be contrary to the man page, but it's easy to detect
                    // by simply checking len against the buffer length.
                    if (sysctl(name, 2, buf, len, IntPtr.Zero, 0) == 0 && *len < BUFFER_LENGTH)
                    {
                        return Marshal.PtrToStringAnsi((IntPtr)buf, (int)*len);
                    }
                }
                catch (Exception ex)
                {
                    throw new PlatformNotSupportedException("Error reading Darwin Kernel Version", ex);
                }
                throw new PlatformNotSupportedException("Unknown error reading Darwin Kernel Version");
            }
        }

        public static string GetOSVersionString()
        {
            var kernelRelease = GetKernelRelease();
            if (!Version.TryParse(kernelRelease, out Version? version) || version.Major < 5)
            {
                // 10.0 covers all versions prior to Darwin 5
                // Similarly, if the version is not a valid version number, but we have still detected that it is Darwin, we just assume
                // it is OS X 10.0
                return "10.0";
            }
            else
            {
                // Mac OS X 10.1 mapped to Darwin 5.x, and the mapping continues that way
                // So just subtract 4 from the Darwin version.
                // https://en.wikipedia.org/wiki/Darwin_%28operating_system%29
                return $"10.{version.Major - 4}";
            }
        }

        [DllImport("libc")]
        private static extern unsafe int sysctl(
            int* name,
            uint namelen,
            byte* oldp,
            uint* oldlenp,
            IntPtr newp,
            uint newlen);
    }
}
