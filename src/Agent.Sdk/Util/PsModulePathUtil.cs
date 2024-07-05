using Microsoft.IdentityModel.Tokens;
using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;

namespace Agent.Sdk.Util
{
    public static class PsModulePathUtil
    {
        [SupportedOSPlatform("windows")]
        public static bool ContainsPowershellCoreLocations(string psModulePath)
        {
            if (psModulePath.IsNullOrEmpty())
            {
                return false;
            }

            string programFilesPath = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesPath86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            string psHomeModuleLocation = Path.Combine(programFilesPath, "PowerShell", "Modules");
            string psHomeModuleLocation86 = Path.Combine(programFilesPath86, "PowerShell", "Modules");

            string programFilesModuleLocation = Path.Combine(programFilesPath.ToLower(), "powershell", "7", "Modules");
            string programFilesModuleLocation86 = Path.Combine(programFilesPath86.ToLower(), "powershell", "7", "Modules");

            string[] wellKnownLocations = new[]
            {
                psHomeModuleLocation, psHomeModuleLocation86, programFilesModuleLocation, programFilesModuleLocation86
            };

            bool containsPwshLocations = wellKnownLocations.Any(location => psModulePath.Contains(location, StringComparison.OrdinalIgnoreCase));

            return containsPwshLocations;
        }
    }
}
