using System;
using System.Runtime.InteropServices;

namespace Microsoft.VisualStudio.Services.Agent.Util
{
    public sealed class DarwinUtil
    {
       // 10.0 covers all versions prior to Darwin 5
       // Mac OS X 10.1 mapped to Darwin 5.x, and the mapping continues that way
       // So just subtract 4 from the Darwin version.
       // https://en.wikipedia.org/wiki/Darwin_%28operating_system%29
        public static string GetOSVersionString()
            => Environment.OSVersion.Version.Major < 5 ? "10.0" : $"10.{Environment.OSVersion.Version.Major - 4}";

    }
}
