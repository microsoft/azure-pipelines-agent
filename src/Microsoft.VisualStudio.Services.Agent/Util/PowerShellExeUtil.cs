// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;

namespace Microsoft.VisualStudio.Services.Agent.Util
{
    [ServiceLocator(Default = typeof(PowerShellExeUtil))]
    [SupportedOSPlatform("windows")]
    public interface IPowerShellExeUtil : IAgentService
    {
        string GetPath();
    }

    [SupportedOSPlatform("windows")]
    public sealed class PowerShellExeUtil : AgentService, IPowerShellExeUtil
    {
        private static readonly Version MinimumVersion = new Version(3, 0);

        public string GetPath()
        {
            Trace.Entering();
            var infos = new List<PowerShellInfo>();

            // Get all generation subkeys.
            string[] generations;
            using (RegistryKey powerShellKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\PowerShell"))
            {
                generations =
                    (powerShellKey?.GetSubKeyNames() ?? new string[0])
                    // Filter out non-integers.
                    .Where(x =>
                    {
                        Trace.Verbose($"Candidate generation: '{x}'");
                        int generationInt;
                        return int.TryParse(x, NumberStyles.None, NumberFormatInfo.InvariantInfo, out generationInt);
                    })
                    .ToArray();
            }

            foreach (string generation in generations)
            {
                // Create a new generation info data class.
                Trace.Info($"Generation: '{generation}'");
                var info = new PowerShellInfo();

                // Get the engine version.
                string versionString = GetHklmValue($@"SOFTWARE\Microsoft\PowerShell\{generation}\PowerShellEngine", "PowerShellVersion") as string;
                if (string.IsNullOrEmpty(versionString) ||
                    !Version.TryParse(versionString, out info.Version))
                {
                    Trace.Info("Unable to determine the Powershell engine version.  Possibly Powershell is not installed.");
                    continue;
                }

                // Check the minimum version.
                if (info.Version < MinimumVersion)
                {
                    Trace.Info("Unsupported version. Skipping.");
                    continue;
                }

                // Get the console host directory.
                string applicationBase = GetHklmValue($@"SOFTWARE\Microsoft\PowerShell\{generation}\PowerShellEngine", "ApplicationBase") as string;
                if (string.IsNullOrEmpty(applicationBase))
                {
                    Trace.Warning("Unable to locate application base. Skipping.");
                    continue;
                }

                // Check the file path.
                info.Path = Path.Combine(applicationBase, "powershell.exe");
                if (!File.Exists(info.Path))
                {
                    Trace.Warning($"Console host does not exist at expected location: '{info.Path}'");
                    continue;
                }

                ArgUtil.NotNullOrEmpty(info.Path, nameof(info.Path));
                ArgUtil.NotNull(info.Version, nameof(info.Version));
                infos.Add(info);
            }

            // Throw if not found.
            PowerShellInfo latest = infos.OrderByDescending(x => x.Version).FirstOrDefault();
            if (latest == null)
            {
                throw new InvalidOperationException(StringUtil.Loc("PowerShellNotInstalledMinVersion0", MinimumVersion));
            }

            return latest.Path;
        }

        private object GetHklmValue(string keyName, string valueName)
        {
            keyName = $@"HKEY_LOCAL_MACHINE\{keyName}";
            object value = Registry.GetValue(keyName, valueName, defaultValue: null);
            if (object.ReferenceEquals(value, null))
            {
                Trace.Info($"Key name '{keyName}', value name '{valueName}' is null.");
                return null;
            }

            Trace.Info($"Key name '{keyName}', value name '{valueName}': '{value}'");
            return value;
        }

        private sealed class PowerShellInfo
        {
            public string Path;
            public Version Version; // This is a field so it can be passed as an out param to TryParse.
        }
    }
}
