// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;

namespace Microsoft.VisualStudio.Services.Agent.Util
{
    public static class BlobStoreWarningInfoProvider
    {
        /// <summary>
        /// Used to get platform-specific reference to allow list in documentation
        /// </summary>
        public static string GetAllowListLinkForCurrentPlatform()
        {
            var hostOS = PlatformUtil.HostOS;
            var infoURL = "";

            switch (hostOS)
            {
                case PlatformUtil.OS.Windows:
                    infoURL = PlatformSpecificAllowList.WindowsAllowList;
                    break;
                case PlatformUtil.OS.Linux:
                    infoURL = PlatformSpecificAllowList.LinuxAllowList;
                    break;
                case PlatformUtil.OS.OSX:
                    infoURL = PlatformSpecificAllowList.MacOSAllowList;
                    break;
                default:
                    infoURL = PlatformSpecificAllowList.GenericAllowList;
                    break;
            }

            return infoURL;
        }

        internal static class PlatformSpecificAllowList
        {
            public const string GenericAllowList = "https://aka.ms/adoallowlist";
            public const string WindowsAllowList = "https://aka.ms/windows-agent-allowlist";
            public const string MacOSAllowList = "https://aka.ms/macOS-agent-allowlist";
            public const string LinuxAllowList = "https://aka.ms/linux-agent-allowlist";
        }
    }
}
