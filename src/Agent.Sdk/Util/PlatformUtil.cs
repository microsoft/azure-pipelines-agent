// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Agent.Sdk.Knob;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.Win32;
using Newtonsoft.Json;
using System.ServiceProcess;
using Agent.Sdk.Util;

namespace Agent.Sdk
{
    public static class PlatformUtil
    {
        private static UtilKnobValueContext _knobContext = UtilKnobValueContext.Instance();
        private static OperatingSystem[] net6SupportedSystems;
        private static HttpClient httpClient = new HttpClient();

        // System.Runtime.InteropServices.OSPlatform is a struct, so it is
        // not suitable for switch statements.
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1717: Only FlagsAttribute enums should have plural names")]
        public enum OS
        {
            Linux,
            OSX,
            Windows,
        }

        public static OS HostOS
        {
            [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1065: Do not raise exceptions in unexpected")]
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

        public static bool RunningOnRHELVersion(string version)
        {
            if (!(detectedRHELVersion is null))
            {
                return (bool)detectedRHELVersion;
            }

            DetectRHELVersion(version);

            return (bool)detectedRHELVersion;
        }

        public static string GetSystemId()
        {
            return PlatformUtil.HostOS switch
            {
                PlatformUtil.OS.Linux => GetLinuxId(),
                PlatformUtil.OS.OSX => "MacOS",
                PlatformUtil.OS.Windows => GetWindowsId(),
                _ => null
            };
        }

        public static SystemVersion GetSystemVersion()
        {
            return PlatformUtil.HostOS switch
            {
                PlatformUtil.OS.Linux => new SystemVersion(GetLinuxName(), null),
                PlatformUtil.OS.OSX => new SystemVersion(GetOSxName(), null),
                PlatformUtil.OS.Windows => new SystemVersion(GetWindowsName(), GetWindowsVersion()),
                _ => null
            };
        }

        private static void DetectRHELVersion(string version)
        {
            lock (detectedRHELVersionlock)
            {
                if (!RunningOnLinux || !File.Exists("/etc/redhat-release"))
                {
                    detectedRHELVersion = false;
                }
                else
                {
                    detectedRHELVersion = false;
                    try
                    {
                        string redhatVersion = File.ReadAllText("/etc/redhat-release");
                        if (redhatVersion.StartsWith("CentOS release " + version)
                            || redhatVersion.StartsWith("Red Hat Enterprise Linux Server release " + version))
                        {
                            detectedRHELVersion = true;
                        }
                    }
                    catch (IOException)
                    {
                        // IOException indicates we couldn't read that file; probably not RHEL version.
                    }
                }
            }
        }

        private static string GetLinuxId()
        {
            if (RunningOnLinux && File.Exists("/etc/os-release"))
            {
                Regex linuxIdRegex = new Regex("^ID\\s*=\\s*\"?(?<id>[0-9a-z._-]+)\"?");

                using (StreamReader reader = new StreamReader("/etc/os-release"))
                {
                    while (!reader.EndOfStream)
                    {
                        string line = reader.ReadLine();
                        var linuxIdRegexMatch = linuxIdRegex.Match(line);

                        if (linuxIdRegexMatch.Success)
                        {
                            return linuxIdRegexMatch.Groups["id"].Value;
                        }
                    }
                }
            }

            return null;
        }

        private static string GetLinuxName()
        {
            if (RunningOnLinux && File.Exists("/etc/os-release"))
            {
                Regex linuxVersionIdRegex = new Regex("^VERSION_ID\\s*=\\s*\"?(?<id>[0-9a-z._-]+)\"?");

                using (StreamReader reader = new StreamReader("/etc/os-release"))
                {
                    while (!reader.EndOfStream)
                    {
                        string line = reader.ReadLine();
                        var linuxVersionIdRegexMatch = linuxVersionIdRegex.Match(line);

                        if (linuxVersionIdRegexMatch.Success)
                        {
                            return linuxVersionIdRegexMatch.Groups["id"].Value;
                        }
                    }
                }
            }

            return null;
        }

        private static string GetOSxName()
        {
            if (RunningOnMacOS && File.Exists("/System/Library/CoreServices/SystemVersion.plist"))
            {
                var systemVersionFile = XDocument.Load("/System/Library/CoreServices/SystemVersion.plist");
                var parsedSystemVersionFile = systemVersionFile.Descendants("dict")
                    .SelectMany(d => d.Elements("key").Zip(d.Elements().Where(e => e.Name != "key"), (k, v) => new { Key = k, Value = v }))
                    .ToDictionary(i => i.Key.Value, i => i.Value.Value);
                return parsedSystemVersionFile.ContainsKey("ProductVersion") ? parsedSystemVersionFile["ProductVersion"] : null;
            }

            return null;
        }

        private static string GetWindowsId()
        {
            StringBuilder result = new StringBuilder();
            result.Append("Windows");

            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
            {
                if (key != null)
                {
                    var installationType = key.GetValue("InstallationType");
                    if (installationType != null)
                    {
                        result.Append($" {installationType}");
                    }
                }
            }

            return result.ToString();
        }

        private static string GetWindowsName()
        {
            Regex productNameRegex = new Regex("(Windows)(\\sServer)?\\s(?<versionNumber>[\\d.]+)");

            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
            {
                if (key != null)
                {
                    var productName = key.GetValue("ProductName");
                    var productNameRegexMatch = productNameRegex.Match(productName?.ToString());

                    if (productNameRegexMatch.Success)
                    {
                        return productNameRegexMatch.Groups["versionNumber"]?.Value;
                    }
                }
            }

            return null;
        }

        private static string GetWindowsVersion()
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
            {
                if (key != null)
                {
                    var currentBuildNumber = key.GetValue("CurrentBuildNumber");
                    return currentBuildNumber?.ToString();
                }
            }

            return null;
        }

        private static bool? detectedRHELVersion = null;
        private static object detectedRHELVersionlock = new object();

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

        public static bool BuiltOnX86
        {
            get
            {
#if X86
                return true;
#else
                return false;
#endif
            }
        }

        public static bool UseLegacyHttpHandler
        {
            // In .NET Core 2.1, we couldn't use the new SocketsHttpHandler for Windows or Linux
            // On Linux, negotiate auth didn't work if the TFS URL was HTTPS
            // On Windows, proxy was not working
            // But on ARM/ARM64 Linux, the legacy curl dependency is problematic
            // (see https://github.com/dotnet/runtime/issues/28891), so we slowly
            // started to use the new handler.
            //
            // The legacy handler is going away in .NET 5.0, so we'll go ahead
            // and remove its usage now. In case this breaks anyone, adding
            // a temporary knob so they can re-enable it.
            // https://github.com/dotnet/runtime/issues/35365#issuecomment-667467706
            get => AgentKnobs.UseLegacyHttpHandler.GetValue(_knobContext).AsBoolean();
        }

        private async static Task<OperatingSystem[]> GetNet6SupportedSystems()
        {
            string serverFileUrl = "https://raw.githubusercontent.com/microsoft/azure-pipelines-agent/master/src/Agent.Listener/net6.json";
            string supportOSfilePath = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), "net6.json");
            string supportOSfileContent;
            bool supportOSfileExists = File.Exists(supportOSfilePath);

            if ((!supportOSfileExists || File.GetLastWriteTimeUtc(supportOSfilePath) < DateTime.UtcNow.AddHours(-1))
                && AgentKnobs.EnableFetchingNet6List.GetValue(_knobContext).AsBoolean())
            {
                HttpResponseMessage response = await httpClient.GetAsync(serverFileUrl);
                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"Getting file \"net6.json\" from server failed. Status code: {response.StatusCode}");
                }
                supportOSfileContent = await response.Content.ReadAsStringAsync();
                await File.WriteAllTextAsync(supportOSfilePath, supportOSfileContent);
            }
            else
            {
                if (net6SupportedSystems != null)
                {
                    return net6SupportedSystems;
                }

                if (!supportOSfileExists)
                {
                    throw new FileNotFoundException("File with list of systems supporting .NET 6 is absent", supportOSfilePath);
                }

                supportOSfileContent = await File.ReadAllTextAsync(supportOSfilePath);
            }

            net6SupportedSystems = JsonConvert.DeserializeObject<OperatingSystem[]>(supportOSfileContent);
            return net6SupportedSystems;
        }

        public async static Task<bool> IsNet6Supported()
        {
            OperatingSystem[] net6SupportedSystems = await GetNet6SupportedSystems();

            string systemId = PlatformUtil.GetSystemId();
            SystemVersion systemVersion = PlatformUtil.GetSystemVersion();
            return net6SupportedSystems.Any((s) => s.Equals(systemId, systemVersion));
        }

        public async static Task<bool> DoesSystemPersistsInNet6Whitelist()
        {
            OperatingSystem[] net6SupportedSystems = await GetNet6SupportedSystems();
            string systemId = PlatformUtil.GetSystemId();

            return net6SupportedSystems.Any((s) => s.Equals(systemId));
        }
        public static bool DetectDockerContainer()
        {
            bool isDockerContainer = false;

            try
            {
                if (PlatformUtil.RunningOnWindows)
                {
                    // For Windows we check Container Execution Agent Service (cexecsvc) existence
                    var serviceName = "cexecsvc";
                    ServiceController[] scServices = ServiceController.GetServices();
                    if (scServices.Any(x => String.Equals(x.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase) && x.Status == ServiceControllerStatus.Running))
                    {
                        isDockerContainer = true;
                    }
                }
                else
                {
                    // In Unix in control group v1, we can identify if a process is running in a Docker
                    var initProcessCgroup = File.ReadLines("/proc/1/cgroup");
                    if (initProcessCgroup.Any(x => x.IndexOf(":/docker/", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        isDockerContainer = true;
                    }
                }
            }
            catch (Exception ex)
            {
                // Logging exception will be handled by JobRunner
                throw;
            }
            return isDockerContainer;
        }

        public static bool DetectAzureVM()
        {
            bool isAzureVM = false;

            try
            {
                // Metadata information endpoint can be used to check whether we're in Azure VM
                // Additional info: https://learn.microsoft.com/en-us/azure/virtual-machines/windows/instance-metadata-service?tabs=linux
                using var metadataProvider = new AzureInstanceMetadataProvider();
                if (metadataProvider.HasMetadata())
                    isAzureVM = true;
            }
            catch (Exception ex)
            {
                // Logging exception will be handled by JobRunner
                throw;
            }
            return isAzureVM;
        }
    }

#pragma warning disable CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
    public class SystemVersion
#pragma warning restore CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
    {
        public ParsedVersion Name { get; }

        public ParsedVersion Version { get; }

        [JsonConstructor]
        public SystemVersion(string name, string version)
        {
            if (name == null && version == null)
            {
                throw new Exception("You need to provide at least one not-nullable parameter");
            }

            if (name != null)
            {
                this.Name = new ParsedVersion(name);
            }

            if (version != null)
            {
                this.Version = new ParsedVersion(version);
            }
        }

        public override bool Equals(object obj)
        {
            if (obj is SystemVersion comparingOSVersion)
            {
                return ((this.Name != null && comparingOSVersion.Name != null)
                    ? this.Name.Equals(comparingOSVersion.Name)
                    : true) && ((this.Version != null && comparingOSVersion.Version != null)
                    ? this.Version.Equals(comparingOSVersion.Version)
                    : true);
            }

            return false;
        }

        public override string ToString()
        {
            StringBuilder result = new StringBuilder();

            if (this.Name != null)
            {
                result.Append($"OS name: {this.Name}");
            }

            if (this.Version != null)
            {

                result.Append(string.Format("{0}OS version: {1}",
                    string.IsNullOrEmpty(result.ToString()) ? string.Empty : ", ",
                    this.Version));
            }

            return result.ToString();
        }
    }

#pragma warning disable CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
    public class ParsedVersion
#pragma warning restore CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
    {
        private readonly Regex parsedVersionRegex = new Regex("^((?<Major>[\\d]+)(\\.(?<Minor>[\\d]+))?(\\.(?<Build>[\\d]+))?(\\.(?<Revision>[\\d]+))?)(?<suffix>[^+]+)?(?<minFlag>[+])?$");
        private readonly string originalString;

        public Version Version { get; }

        public string Syffix { get; }

        public bool MinFlag { get; }

        public ParsedVersion(string version)
        {
            this.originalString = version;

            var parsedVersionRegexMatch = parsedVersionRegex.Match(version.Trim());

            if (!parsedVersionRegexMatch.Success)
            {
                throw new Exception($"String {version} can't be parsed");
            }

            string versionString = string.Format(
                "{0}.{1}.{2}.{3}",
                parsedVersionRegexMatch.Groups["Major"].Value,
                !string.IsNullOrEmpty(parsedVersionRegexMatch.Groups["Minor"].Value) ? parsedVersionRegexMatch.Groups["Minor"].Value : "0",
                !string.IsNullOrEmpty(parsedVersionRegexMatch.Groups["Build"].Value) ? parsedVersionRegexMatch.Groups["Build"].Value : "0",
                !string.IsNullOrEmpty(parsedVersionRegexMatch.Groups["Revision"].Value) ? parsedVersionRegexMatch.Groups["Revision"].Value : "0");

            this.Version = new Version(versionString);
            this.Syffix = parsedVersionRegexMatch.Groups["suffix"].Value;
            this.MinFlag = !string.IsNullOrEmpty(parsedVersionRegexMatch.Groups["minFlag"].Value);
        }

        public override bool Equals(object obj)
        {
            if (obj is ParsedVersion comparingVersion)
            {
                return this.MinFlag
                    ? this.Version <= comparingVersion.Version
                    : this.Version == comparingVersion.Version
                    && (this.Syffix != null && comparingVersion.Syffix != null
                        ? this.Syffix.Equals(comparingVersion.Syffix, StringComparison.OrdinalIgnoreCase)
                        : true);
            }

            return false;
        }

        public override string ToString()
        {
            return this.originalString;
        }
    }

    public class OperatingSystem
    {
        public string Id { get; set; }

        public SystemVersion[] Versions { get; set; }

        public OperatingSystem() { }

        public bool Equals(string systemId) =>
            this.Id.Equals(systemId, StringComparison.OrdinalIgnoreCase);

        public bool Equals(string systemId, SystemVersion systemVersion) =>
            this.Equals(systemId) && this.Versions.Length > 0
                ? this.Versions.Any(version => version.Equals(systemVersion))
                : false;
    }

}
