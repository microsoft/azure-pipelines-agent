using System;

namespace Microsoft.VisualStudio.Services.Agent.Listener
{
    public class Utility
    {
        /// <summary>
        /// Checks if the OS version is win8 or above(doesn't check whether it is client or server)
        /// </summary>
        public static bool IsWin8OrAbove()
        {
            Version osVersion = Environment.OSVersion.Version;
            return (osVersion.Major >= 6 && osVersion.Minor >= 2);
        }
    }
}
