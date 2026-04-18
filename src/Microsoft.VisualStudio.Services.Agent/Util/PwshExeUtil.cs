// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using Agent.Sdk.Util;
using System;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Microsoft.VisualStudio.Services.Agent.Util
{
    [ServiceLocator(Default = typeof(PwshExeUtil))]
    public interface IPwshExeUtil : IAgentService
    {
        string GetPath();
    }

    public sealed class PwshExeUtil : AgentService, IPwshExeUtil
    {
        private static readonly Version MinimumVersion = new Version(7, 0);

        public string GetPath()
        {
            Trace.Entering();

            string commandName = PlatformUtil.RunningOnWindows ? "pwsh.exe" : "pwsh";
            string pwshPath = WhichUtil.Which(commandName, trace: Trace);
            if (string.IsNullOrEmpty(pwshPath))
            {
                throw new InvalidOperationException(StringUtil.Loc("FileNotFound", commandName));
            }

            Version version = GetVersion(pwshPath);
            if (version < MinimumVersion)
            {
                throw new InvalidOperationException($"A compatible version of pwsh was not found. Minimum required version is {MinimumVersion}.");
            }

            return pwshPath;
        }

        private Version GetVersion(string pwshPath)
        {
            ArgUtil.NotNullOrEmpty(pwshPath, nameof(pwshPath));

            string output = string.Empty;
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = pwshPath,
                    Arguments = "-NoLogo -NoProfile -NonInteractive -Command \"$PSVersionTable.PSVersion.ToString()\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                if (!process.Start())
                {
                    throw new InvalidOperationException($"Unable to start '{pwshPath}'.");
                }

                output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException($"Unable to determine pwsh version from '{pwshPath}'. {error}".Trim());
                }
            }

            string versionString = output?.Trim();
            if (Version.TryParse(versionString, out Version version))
            {
                return version;
            }

            Match match = Regex.Match(versionString ?? string.Empty, @"\d+\.\d+(\.\d+)?(\.\d+)?", RegexOptions.CultureInvariant);
            if (match.Success && Version.TryParse(match.Value, out version))
            {
                return version;
            }

            throw new InvalidOperationException($"Unable to parse pwsh version '{versionString}'.");
        }
    }
}
