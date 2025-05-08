// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Agent.Sdk.Knob;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.Win32;
using Newtonsoft.Json;
using System.ServiceProcess;
using Agent.Sdk.Util;
using System.Net.Http;
using System.Net;

namespace Agent.Sdk
{
    public static class PlatformUtil
    {
        private static UtilKnobValueContext _knobContext = UtilKnobValueContext.Instance();

        private static readonly string[] linuxReleaseFilePaths = new string[2] { "/etc/os-release", "/usr/lib/os-release" };

        // System.Runtime.InteropServices.OSPlatform is a struct, so it is
        // not suitable for switch statements.
        // The SupportedOSPlatformGuard is not supported on enums, so call sites using this need to suppress warnings https://github.com/dotnet/runtime/issues/51541
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

        [SupportedOSPlatformGuard("windows")]
        public static bool RunningOnWindows => PlatformUtil.HostOS == PlatformUtil.OS.Windows;

        [SupportedOSPlatformGuard("macos")]
        public static bool RunningOnMacOS => PlatformUtil.HostOS == PlatformUtil.OS.OSX;

        [SupportedOSPlatformGuard("linux")]
        public static bool RunningOnLinux => PlatformUtil.HostOS == PlatformUtil.OS.Linux;

        public static bool RunningOnAlpine
        {
            get
            {
                if (File.Exists("/etc/alpine-release"))
                {
                    return true;
                }

                return false;
            }
        }

        public static async Task<bool> IsRunningOnAppleSiliconAsX64Async(CancellationToken cancellationToken)
        {
            if (RunningOnMacOS)
            {
                try
                {
                    // See https://stackoverflow.com/questions/65259300/detect-apple-silicon-from-command-line
                    var cpuBrand = await ExecuteShCommand("sysctl -n machdep.cpu.brand_string", cancellationToken);
                    var processArchitecture = await ExecuteShCommand("uname -m", cancellationToken);
                    return cpuBrand.Contains("Apple") && processArchitecture.Contains("x86_64");
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        private static async Task<string> ExecuteShCommand(string command, CancellationToken cancellationToken)
        {
            using (var invoker = new ProcessInvoker(new NullTraceWriter()))
            {
                var stdout = new StringBuilder();
                invoker.OutputDataReceived += (object sender, ProcessDataReceivedEventArgs e) => stdout.Append(e.Data);
                await invoker.ExecuteAsync(
                    string.Empty,
                    "/bin/sh",
                    $"-c \"{command}\"",
                    null,
                    cancellationToken);

                return stdout.ToString();
            }
        }

        public static bool RunningOnRHEL6
        {
            get
            {
                if (!(detectedRHEL6 is null))
                {
                    return (bool)detectedRHEL6;
                }

                DetectRHEL6();

                return (bool)detectedRHEL6;
            }
        }

        public static string GetSystemId()
        {
#pragma warning disable CA1416 // SupportedOSPlatformGuard not honored on enum members
            return PlatformUtil.HostOS switch
            {
                PlatformUtil.OS.Linux => GetLinuxId(),
                PlatformUtil.OS.OSX => "MacOS",
                PlatformUtil.OS.Windows => GetWindowsId(),
                _ => null
            };
#pragma warning restore CA1416
        }

        public static SystemVersion GetSystemVersion()
        {
#pragma warning disable CA1416 // SupportedOSPlatformGuard not honored on enum members
            return PlatformUtil.HostOS switch
            {
                PlatformUtil.OS.Linux => new SystemVersion(GetLinuxName(), null),
                PlatformUtil.OS.OSX => new SystemVersion(GetOSxName(), null),
                PlatformUtil.OS.Windows => new SystemVersion(GetWindowsName(), GetWindowsVersion()),
                _ => null
            };
#pragma warning restore CA1416
        }

        private static void DetectRHEL6()
        {
            lock (detectedRHEL6lock)
            {
                if (!RunningOnLinux || !File.Exists("/etc/redhat-release"))
                {
                    detectedRHEL6 = false;
                }
                else
                {
                    detectedRHEL6 = false;
                    try
                    {
                        string redhatVersion = File.ReadAllText("/etc/redhat-release");
                        if (redhatVersion.StartsWith("CentOS release 6.")
                            || redhatVersion.StartsWith("Red Hat Enterprise Linux Server release 6."))
                        {
                            detectedRHEL6 = true;
                        }
                    }
                    catch (IOException)
                    {
                        // IOException indicates we couldn't read that file; probably not RHEL6
                    }
                }
            }
        }

        private static string GetLinuxReleaseFilePath()
        {
            if (RunningOnLinux)
            {
                string releaseFilePath = linuxReleaseFilePaths.FirstOrDefault(x => File.Exists(x), null);
                return releaseFilePath;
            }

            return null;
        }

        private static string GetLinuxId()
        {

            string filePath = GetLinuxReleaseFilePath();

            if (RunningOnLinux && filePath != null)
            {
                Regex linuxIdRegex = new Regex("^ID\\s*=\\s*\"?(?<id>[0-9a-z._-]+)\"?");

                using (StreamReader reader = new StreamReader(filePath))
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

            string filePath = GetLinuxReleaseFilePath();

            if (RunningOnLinux && filePath != null)
            {
                Regex linuxVersionIdRegex = new Regex("^VERSION_ID\\s*=\\s*\"?(?<id>[0-9a-z._-]+)\"?");

                using (StreamReader reader = new StreamReader(filePath))
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

        [SupportedOSPlatform("windows")]
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

        [SupportedOSPlatform("windows")]
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

        [SupportedOSPlatform("windows")]
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

        private static bool? detectedRHEL6 = null;
        private static object detectedRHEL6lock = new object();

        public static Architecture HostArchitecture => RuntimeInformation.OSArchitecture;

        public static bool IsX86 => PlatformUtil.HostArchitecture == Architecture.X86;

        public static bool IsX64 => PlatformUtil.HostArchitecture == Architecture.X64;

        public static bool IsArm => PlatformUtil.HostArchitecture == Architecture.Arm;

        public static bool IsArm64 => PlatformUtil.HostArchitecture == Architecture.Arm64;

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

        public static async Task<bool> IsNetVersionSupported(string netVersion)
        {
            string supportOSfilePath = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), $"{netVersion}.json");

            if (!File.Exists(supportOSfilePath))
            {
                throw new FileNotFoundException($"File with list of systems supporting {netVersion} is absent", supportOSfilePath);
            }

            string supportOSfileContent = await File.ReadAllTextAsync(supportOSfilePath);
            OperatingSystem[] supportedSystems = JsonConvert.DeserializeObject<OperatingSystem[]>(supportOSfileContent);

            string systemId = PlatformUtil.GetSystemId();
            SystemVersion systemVersion = PlatformUtil.GetSystemVersion();
            return supportedSystems.Any(s => s.Equals(systemId, systemVersion));
        }

        public static bool DetectDockerContainer()
        {
            bool isDockerContainer = false;

            try
            {
                if (PlatformUtil.RunningOnWindows)
                {
#pragma warning disable CA1416 // SupportedOSPlatform checks not respected in lambda usage
                    // For Windows we check Container Execution Agent Service (cexecsvc) existence
                    var serviceName = "cexecsvc";
                    ServiceController[] scServices = ServiceController.GetServices();
                    if (scServices.Any(x => String.Equals(x.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase) && x.Status == ServiceControllerStatus.Running))
                    {
                        isDockerContainer = true;
                    }
#pragma warning restore CA1416
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
            catch (Exception)
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
            catch (Exception)
            {
                // Logging exception will be handled by JobRunner
                throw;
            }
            return isAzureVM;
        }

        // The URL of the agent package hosted on Azure CDN
        private const string _agentPackageUri = "https://download.agent.dev.azure.com/agent/4.252.0/vsts-agent-win-x64-4.252.0.zip";

#nullable enable
        /// <summary>
        /// Checks if the agent CDN endpoint is accessible by sending an HTTP HEAD request.
        /// </summary>
        /// <param name="webProxy">
        /// Optional <see cref="IWebProxy"/> to route the request through a proxy. If null, the system default proxy settings are used.
        /// </param>
        /// <remarks>
        /// - Returns <c>true</c> if the endpoint responds with a successful (2xx) status code.
        /// - Returns <c>false</c> if the endpoint responds with a non-success status code (4xx, 5xx).
        /// - Throws exceptions (e.g., timeout, DNS failure) if the request cannot be completed.
        /// - Uses a 5-second timeout to avoid hanging.
        /// - All HTTP resources are properly disposed after the request completes.
        /// </remarks>
        /// <returns><c>true</c> if the endpoint is reachable and returns success; otherwise, <c>false</c>.</returns>
        public static async Task<bool> IsAgentCdnAccessibleAsync(IWebProxy? webProxy = null)
        {
            // Configure the HttpClientHandler with the proxy if provided
            using HttpClientHandler handler = new()
            {
                Proxy = webProxy,
                UseProxy = webProxy is not null
            };
            handler.CheckCertificateRevocationList = true; // Check for certificate revocation
            using HttpClient httpClient = new(handler);

            // Construct a HEAD request to avoid downloading the full file
            using HttpRequestMessage request = new(HttpMethod.Head, _agentPackageUri);

            // Apply a 5-second timeout to prevent hanging
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));

            // Send the request and return whether the response status indicates success
            HttpResponseMessage response = await httpClient.SendAsync(request, cts.Token);
            return response.IsSuccessStatusCode;
        }
#nullable disable
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
                throw new ArgumentNullException("You need to provide at least one not-nullable parameter");
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
                throw new FormatException($"String {version} can't be parsed");
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
