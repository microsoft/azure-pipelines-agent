// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Capabilities;
using Microsoft.Win32;
using System.Diagnostics;
using System.Linq;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Agent.Sdk.Knob;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    [ServiceLocator(Default = typeof(DiagnosticLogManager))]
    public interface IDiagnosticLogManager : IAgentService
    {
        Task UploadDiagnosticLogsAsync(IExecutionContext executionContext,
                                  Pipelines.AgentJobRequestMessage message,
                                  DateTime jobStartTimeUtc);
    }

    // This class manages gathering data for support logs, zipping the data, and uploading it.
    // The files are created with the following folder structure:
    // ..\_layout\_work\_temp
    //      \[job name]-support (supportRootFolder)
    //          \files (supportFolder)
    //              ...
    //          support.zip
    public sealed class DiagnosticLogManager : AgentService, IDiagnosticLogManager
    {
        public async Task UploadDiagnosticLogsAsync(IExecutionContext executionContext,
                                         Pipelines.AgentJobRequestMessage message,
                                         DateTime jobStartTimeUtc)
        {
            ArgUtil.NotNull(executionContext, nameof(executionContext));
            ArgUtil.NotNull(message, nameof(message));

            executionContext.Debug("Starting diagnostic file upload.");

            // Setup folders
            // \_layout\_work\_temp\[jobname-support]
            executionContext.Debug("Setting up diagnostic log folders.");
            string tempDirectory = HostContext.GetDirectory(WellKnownDirectory.Temp);
            ArgUtil.Directory(tempDirectory, nameof(tempDirectory));

            string supportRootFolder = Path.Combine(tempDirectory, message.JobName + "-support");
            Directory.CreateDirectory(supportRootFolder);

            // \_layout\_work\_temp\[jobname-support]\files
            executionContext.Debug("Creating diagnostic log files folder.");
            string supportFilesFolder = Path.Combine(supportRootFolder, "files");
            Directory.CreateDirectory(supportFilesFolder);

            // Create the environment file
            // \_layout\_work\_temp\[jobname-support]\files\environment.txt
            var configurationStore = HostContext.GetService<IConfigurationStore>();
            AgentSettings settings = configurationStore.GetSettings();
            int agentId = settings.AgentId;
            string agentName = settings.AgentName;
            int poolId = settings.PoolId;

            executionContext.Debug("Creating diagnostic log environment file.");
            string environmentFile = Path.Combine(supportFilesFolder, "environment.txt");
            string content = await GetEnvironmentContent(agentId, agentName, message.Steps);
            File.WriteAllText(environmentFile, content);

            // Create the capabilities file
            var capabilitiesManager = HostContext.GetService<ICapabilitiesManager>();
            Dictionary<string, string> capabilities = await capabilitiesManager.GetCapabilitiesAsync(configurationStore.GetSettings(), default(CancellationToken));
            executionContext.Debug("Creating capabilities file.");
            string capabilitiesFile = Path.Combine(supportFilesFolder, "capabilities.txt");
            string capabilitiesContent = GetCapabilitiesContent(capabilities);
            File.WriteAllText(capabilitiesFile, capabilitiesContent);

            // Copy worker diag log files
            List<string> workerDiagLogFiles = GetWorkerDiagLogFiles(HostContext.GetDiagDirectory(), jobStartTimeUtc);
            executionContext.Debug($"Copying {workerDiagLogFiles.Count()} worker diag logs from {HostContext.GetDiagDirectory()}.");

            foreach (string workerLogFile in workerDiagLogFiles)
            {
                ArgUtil.File(workerLogFile, nameof(workerLogFile));

                string destination = Path.Combine(supportFilesFolder, Path.GetFileName(workerLogFile));
                File.Copy(workerLogFile, destination);
            }

            // Copy agent diag log files - we are using the worker Host Context and we need the diag folder form the Agent.
            List<string> agentDiagLogFiles = GetAgentDiagLogFiles(HostContext.GetDiagDirectory(HostType.Agent), jobStartTimeUtc);
            executionContext.Debug($"Copying {agentDiagLogFiles.Count()} agent diag logs from {HostContext.GetDiagDirectory(HostType.Agent)}.");

            foreach (string agentLogFile in agentDiagLogFiles)
            {
                ArgUtil.File(agentLogFile, nameof(agentLogFile));

                string destination = Path.Combine(supportFilesFolder, Path.GetFileName(agentLogFile));
                File.Copy(agentLogFile, destination);
            }

            // Read and add to logs waagent.conf settings on Linux
            if (PlatformUtil.RunningOnLinux)
            {
                executionContext.Debug("Dumping of waagent.conf file");
                string waagentDumpFile = Path.Combine(supportFilesFolder, "waagentConf.txt");

                string configFileName = "waagent.conf";
                try
                {
                    string filePath = Directory.GetFiles("/etc", configFileName).FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(filePath))
                    {
                        string waagentContent = File.ReadAllText(filePath);

                        File.AppendAllText(waagentDumpFile, "waagent.conf settings");
                        File.AppendAllText(waagentDumpFile, Environment.NewLine);
                        File.AppendAllText(waagentDumpFile, waagentContent);

                        executionContext.Debug("Dumping waagent.conf file is completed.");
                    }
                    else
                    {
                        executionContext.Debug("waagent.conf file wasn't found. Dumping was not done.");
                    }
                }
                catch (Exception ex)
                {
                    string warningMessage = $"Dumping of waagent.conf was not completed successfully. Error message: {ex.Message}";
                    executionContext.Warning(warningMessage);
                }
            }

            // Copy cloud-init log files from linux machines
            if (PlatformUtil.RunningOnLinux)
            {
                executionContext.Debug("Dumping cloud-init logs.");

                string logsFilePath = $"{HostContext.GetDiagDirectory()}/cloudinit-{jobStartTimeUtc.ToString("yyyyMMdd-HHmmss")}-logs.tar.gz";
                string resultLogs = await DumpCloudInitLogs(logsFilePath);
                executionContext.Debug(resultLogs);

                if (File.Exists(logsFilePath))
                {
                    string destination = Path.Combine(supportFilesFolder, Path.GetFileName(logsFilePath));
                    File.Copy(logsFilePath, destination);
                    executionContext.Debug("Cloud-init logs added to the diagnostics archive.");
                }
                else
                {
                    executionContext.Debug("Cloud-init logs were not found.");
                }

                executionContext.Debug("Dumping cloud-init logs is ended.");
            }

            // Copy event logs for windows machines
            bool dumpJobEventLogs = AgentKnobs.DumpJobEventLogs.GetValue(executionContext).AsBoolean();
            if (dumpJobEventLogs && PlatformUtil.RunningOnWindows)
            {
                executionContext.Debug("Dumping event viewer logs for current job.");

                try
                {
                    string eventLogsFile = $"{HostContext.GetDiagDirectory()}/EventViewer-{ jobStartTimeUtc.ToString("yyyyMMdd-HHmmss") }.csv";
                    await DumpCurrentJobEventLogs(executionContext, eventLogsFile, jobStartTimeUtc);

                    string destination = Path.Combine(supportFilesFolder, Path.GetFileName(eventLogsFile));
                    File.Copy(eventLogsFile, destination);
                }
                catch (Exception ex)
                {
                    executionContext.Debug("Failed to dump event viewer logs. Skipping.");
                    executionContext.Debug($"Error message: {ex}");
                }
            }

            bool dumpPackagesVerificationResult = AgentKnobs.DumpPackagesVerificationResult.GetValue(executionContext).AsBoolean();
            if (dumpPackagesVerificationResult && PlatformUtil.RunningOnLinux && !PlatformUtil.RunningOnRHEL6) {
                executionContext.Debug("Dumping info about invalid MD5 sums of installed packages.");

                var debsums = WhichUtil.Which("debsums");
                if (debsums == null) {
                    executionContext.Debug("Debsums is not installed on the system. Skipping broken packages check.");
                } else {
                    try
                    {
                        string packageVerificationResults = await GetPackageVerificationResult(debsums);
                        IEnumerable<string> brokenPackagesInfo = packageVerificationResults
                            .Split("\n")
                            .Where((line) => !String.IsNullOrEmpty(line) && !line.EndsWith("OK"));

                        string brokenPackagesLogsPath = $"{HostContext.GetDiagDirectory()}/BrokenPackages-{ jobStartTimeUtc.ToString("yyyyMMdd-HHmmss") }.log";
                        File.AppendAllLines(brokenPackagesLogsPath, brokenPackagesInfo);

                        string destination = Path.Combine(supportFilesFolder, Path.GetFileName(brokenPackagesLogsPath));
                        File.Copy(brokenPackagesLogsPath, destination);
                    }
                    catch (Exception ex)
                    {
                        executionContext.Debug("Failed to dump broken packages logs. Skipping.");
                        executionContext.Debug($"Error message: {ex}");
                    }
                }
            } else {
                executionContext.Debug("The platform is not based on Debian - skipping debsums check.");
            }

            try
            {
                executionContext.Debug("Starting dumping Agent Azure VM extension logs.");
                bool logsSuccessfullyDumped = DumpAgentExtensionLogs(executionContext, supportFilesFolder, jobStartTimeUtc);
                if (logsSuccessfullyDumped)
                {
                    executionContext.Debug("Agent Azure VM extension logs successfully dumped.");
                }
                else
                {
                    executionContext.Debug("Agent Azure VM extension logs not found. Skipping.");
                }
            }
            catch (Exception ex)
            {
                executionContext.Debug("Failed to dump Agent Azure VM extension logs. Skipping.");
                executionContext.Debug($"Error message: {ex}");
            }

            executionContext.Debug("Zipping diagnostic files.");

            string buildNumber = executionContext.Variables.Build_Number ?? "UnknownBuildNumber";
            string buildName = $"Build {buildNumber}";
            string phaseName = executionContext.Variables.System_PhaseDisplayName ?? "UnknownPhaseName";

            // zip the files
            string diagnosticsZipFileName = $"{buildName}-{phaseName}.zip";
            string diagnosticsZipFilePath = Path.Combine(supportRootFolder, diagnosticsZipFileName);
            ZipFile.CreateFromDirectory(supportFilesFolder, diagnosticsZipFilePath);

            // upload the json metadata file
            executionContext.Debug("Uploading diagnostic metadata file.");
            string metadataFileName = $"diagnostics-{buildName}-{phaseName}.json";
            string metadataFilePath = Path.Combine(supportFilesFolder, metadataFileName);
            string phaseResult = GetTaskResultAsString(executionContext.Result);

            IOUtil.SaveObject(new DiagnosticLogMetadata(agentName, agentId, poolId, phaseName, diagnosticsZipFileName, phaseResult), metadataFilePath);

            executionContext.QueueAttachFile(type: CoreAttachmentType.DiagnosticLog, name: metadataFileName, filePath: metadataFilePath);

            executionContext.QueueAttachFile(type: CoreAttachmentType.DiagnosticLog, name: diagnosticsZipFileName, filePath: diagnosticsZipFilePath);

            executionContext.Debug("Diagnostic file upload complete.");
        }

        /// <summary>
        /// Dumping Agent Azure VM extension logs to the support files folder.
        /// </summary>
        /// <param name="executionContext">Execution context to write debug messages.</param>
        /// <param name="supportFilesFolder">Destination folder for files to be dumped.</param>
        /// <param name="jobStartTimeUtc">Date and time to create timestamp.</param>
        /// <returns>true, if logs have been dumped successfully; otherwise returns false.</returns>
        private bool DumpAgentExtensionLogs(IExecutionContext executionContext, string supportFilesFolder, DateTime jobStartTimeUtc)
        {
            string pathToLogs = String.Empty;
            string archiveName = String.Empty;
            string timestamp = jobStartTimeUtc.ToString("yyyyMMdd-HHmmss");

            if (PlatformUtil.RunningOnWindows)
            {
                // the extension creates a subfolder with a version number on Windows, and we're taking the latest one
                string pathToExtensionVersions = ExtensionPaths.WindowsPathToExtensionVersions;
                if (!Directory.Exists(pathToExtensionVersions))
                {
                    executionContext.Debug("Path to subfolders with Agent Azure VM Windows extension logs (of its different versions) does not exist.");
                    executionContext.Debug($"(directory \"{pathToExtensionVersions}\" not found)");
                    return false;
                }
                string[] subDirs = Directory.GetDirectories(pathToExtensionVersions).Select(dir => Path.GetFileName(dir)).ToArray();
                if (subDirs.Length == 0)
                {
                    executionContext.Debug("Path to Agent Azure VM Windows extension logs (of its different versions) does not contain subfolders.");
                    executionContext.Debug($"(directory \"{pathToExtensionVersions}\" does not contain subdirectories with logs)");
                    return false;
                }
                Version[] versions = subDirs.Select(dir => new Version(dir)).ToArray();
                Version maxVersion = versions.Max();
                pathToLogs = Path.Combine(pathToExtensionVersions, maxVersion.ToString());
                archiveName = $"AgentWindowsExtensionLogs-{timestamp}-utc.zip";
            }
            else if (PlatformUtil.RunningOnLinux)
            {
                // the extension does not create a subfolder with a version number on Linux, and we're just taking this folder
                pathToLogs = ExtensionPaths.LinuxPathToExtensionLogs;
                if (!Directory.Exists(pathToLogs))
                {
                    executionContext.Debug("Path to Agent Azure VM Linux extension logs does not exist.");
                    executionContext.Debug($"(directory \"{pathToLogs}\" not found)");
                    return false;
                }
                archiveName = $"AgentLinuxExtensionLogs-{timestamp}-utc.zip";
            }
            else
            {
                executionContext.Debug("Dumping Agent Azure VM extension logs implemented for Windows and Linux only.");
                return false;
            }

            executionContext.Debug($"Path to agent extension logs: {pathToLogs}");

            string archivePath = Path.Combine(HostContext.GetDiagDirectory(), archiveName);
            executionContext.Debug($"Archiving agent extension logs to: {archivePath}");
            ZipFile.CreateFromDirectory(pathToLogs, archivePath);

            string copyPath = Path.Combine(supportFilesFolder, archiveName);
            executionContext.Debug($"Copying archived agent extension logs to: {copyPath}");
            File.Copy(archivePath, copyPath);

            return true;
        }

        /// <summary>
        /// Dumping cloud-init logs to diag folder of agent if cloud-init is installed on current machine.
        /// </summary>
        /// <param name="logsFile">Path to collect cloud-init logs</param>
        /// <returns>Returns the method execution logs</returns>
        private async Task<string> DumpCloudInitLogs(string logsFile)
        {
            var builder = new StringBuilder();
            string cloudInit = WhichUtil.Which("cloud-init", trace: Trace);
            if (string.IsNullOrEmpty(cloudInit))
            {
                return "Cloud-init isn't found on current machine.";
            }

            string arguments = $"collect-logs -t \"{logsFile}\"";

            try
            {
                using (var processInvoker = HostContext.CreateService<IProcessInvoker>())
                {
                    processInvoker.OutputDataReceived += (object sender, ProcessDataReceivedEventArgs args) =>
                    {
                        builder.AppendLine(args.Data);
                    };

                    processInvoker.ErrorDataReceived += (object sender, ProcessDataReceivedEventArgs args) =>
                    {
                        builder.AppendLine(args.Data);
                    };

                    await processInvoker.ExecuteAsync(
                        workingDirectory: HostContext.GetDirectory(WellKnownDirectory.Bin),
                        fileName: cloudInit,
                        arguments: arguments,
                        environment: null,
                        requireExitCodeZero: false,
                        outputEncoding: null,
                        killProcessOnCancel: false,
                        cancellationToken: default(CancellationToken));
                }
            }
            catch (Exception ex)
            {
                builder.AppendLine(ex.Message);
            }
            return builder.ToString();
        }

        private string GetCapabilitiesContent(Dictionary<string, string> capabilities)
        {
            var builder = new StringBuilder();

            builder.AppendLine("Capabilities");
            builder.AppendLine("");

            foreach (string key in capabilities.Keys)
            {
                builder.Append(key);

                if (!string.IsNullOrEmpty(capabilities[key]))
                {
                    builder.Append($" = {capabilities[key]}");
                }

                builder.AppendLine();
            }

            return builder.ToString();
        }

        private string GetTaskResultAsString(TaskResult? taskResult)
        {
            if (!taskResult.HasValue) { return "Unknown"; }

            return taskResult.ToString();
        }

        // The current solution is a hack. We need to rethink this and find a better one.
        // The list of worker log files isn't available from the logger. It's also nested several levels deep.
        // For this solution we deduce the applicable worker log files by comparing their create time to the start time of the job.
        private List<string> GetWorkerDiagLogFiles(string diagFolder, DateTime jobStartTimeUtc)
        {
            // Get all worker log files with a timestamp equal or greater than the start of the job
            var workerLogFiles = new List<string>();
            var directoryInfo = new DirectoryInfo(diagFolder);

            // Sometimes the timing is off between the job start time and the time the worker log file is created.
            // This adds a small buffer that provides some leeway in case the worker log file was created slightly
            // before the time we log as job start time.
            int bufferInSeconds = -30;
            DateTime searchTimeUtc = jobStartTimeUtc.AddSeconds(bufferInSeconds);

            foreach (FileInfo file in directoryInfo.GetFiles().Where(f => f.Name.StartsWith($"{HostType.Worker}_")))
            {
                // The format of the logs is:
                // Worker_20171003-143110-utc.log
                DateTime fileCreateTime = DateTime.ParseExact(s: file.Name.Substring(startIndex: 7, length: 15), format: "yyyyMMdd-HHmmss", provider: CultureInfo.InvariantCulture);

                if (fileCreateTime >= searchTimeUtc)
                {
                    workerLogFiles.Add(file.FullName);
                }
            }

            return workerLogFiles;
        }

        private List<string> GetAgentDiagLogFiles(string diagFolder, DateTime jobStartTimeUtc)
        {
            // Get the newest agent log file that created just before the start of the job
            var agentLogFiles = new List<string>();
            var directoryInfo = new DirectoryInfo(diagFolder);

            // The agent log that record the start point of the job should created before the job start time.
            // The agent log may get paged if it reach size limit.
            // We will only need upload 1 agent log file in 99%.
            // There might be 1% we need to upload 2 agent log files.
            String recentLog = null;
            DateTime recentTimeUtc = DateTime.MinValue;

            foreach (FileInfo file in directoryInfo.GetFiles().Where(f => f.Name.StartsWith($"{HostType.Agent}_")))
            {
                // The format of the logs is:
                // Agent_20171003-143110-utc.log
                if (DateTime.TryParseExact(s: file.Name.Substring(startIndex: 6, length: 15), format: "yyyyMMdd-HHmmss", provider: CultureInfo.InvariantCulture, style: DateTimeStyles.None, result: out DateTime fileCreateTime))
                {
                    // always add log file created after the job start.
                    if (fileCreateTime >= jobStartTimeUtc)
                    {
                        agentLogFiles.Add(file.FullName);
                    }
                    else if (fileCreateTime > recentTimeUtc)
                    {
                        recentLog = file.FullName;
                        recentTimeUtc = fileCreateTime;
                    }
                }
            }

            if (!String.IsNullOrEmpty(recentLog))
            {
                agentLogFiles.Add(recentLog);
            }

            return agentLogFiles;
        }

        private async Task<string> GetEnvironmentContent(int agentId, string agentName, IList<Pipelines.JobStep> steps)
        {
            if (PlatformUtil.RunningOnWindows)
            {
                return await GetEnvironmentContentWindows(agentId, agentName, steps);
            }
            return await GetEnvironmentContentNonWindows(agentId, agentName, steps);
        }

        private async Task<string> GetEnvironmentContentWindows(int agentId, string agentName, IList<Pipelines.JobStep> steps)
        {
            var builder = new StringBuilder();

            builder.AppendLine($"Environment file created at(UTC): {DateTime.UtcNow}"); // TODO: Format this like we do in other places.
            builder.AppendLine($"Agent Version: {BuildConstants.AgentPackage.Version}");
            builder.AppendLine($"Agent Id: {agentId}");
            builder.AppendLine($"Agent Name: {agentName}");
            builder.AppendLine($"OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
            builder.AppendLine("Steps:");

            foreach (Pipelines.TaskStep task in steps.OfType<Pipelines.TaskStep>())
            {
                builder.AppendLine($"\tName: {task.Reference.Name} Version: {task.Reference.Version}");
            }

            // windows defender on/off
            builder.AppendLine($"Defender enabled: {IsDefenderEnabled()}");

            // firewall on/off
            builder.AppendLine($"Firewall enabled: {IsFirewallEnabled()}");

            // $psversiontable
            builder.AppendLine("Powershell Version Info:");
            builder.AppendLine(await GetPsVersionInfo());

            builder.AppendLine(await GetLocalGroupMembership());

            return builder.ToString();
        }

        // Returns whether or not Windows Defender is running.
        private bool IsDefenderEnabled()
        {
            return Process.GetProcessesByName("MsMpEng.exe").FirstOrDefault() != null;
        }

        // Returns whether or not the Windows firewall is enabled.
        private bool IsFirewallEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey("System\\CurrentControlSet\\Services\\SharedAccess\\Parameters\\FirewallPolicy\\StandardProfile"))
                {
                    if (key == null) { return false; }

                    Object o = key.GetValue("EnableFirewall");
                    if (o == null) { return false; }

                    int firewall = (int)o;
                    if (firewall == 1) { return true; }
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }

        private async Task<string> GetPsVersionInfo()
        {
            var builder = new StringBuilder();

            string powerShellExe = HostContext.GetService<IPowerShellExeUtil>().GetPath();
            string arguments = @"Write-Host ($PSVersionTable | Out-String)";
            using (var processInvoker = HostContext.CreateService<IProcessInvoker>())
            {
                processInvoker.OutputDataReceived += (object sender, ProcessDataReceivedEventArgs args) =>
                {
                    builder.AppendLine(args.Data);
                };

                processInvoker.ErrorDataReceived += (object sender, ProcessDataReceivedEventArgs args) =>
                {
                    builder.AppendLine(args.Data);
                };

                await processInvoker.ExecuteAsync(
                    workingDirectory: HostContext.GetDirectory(WellKnownDirectory.Bin),
                    fileName: powerShellExe,
                    arguments: arguments,
                    environment: null,
                    requireExitCodeZero: false,
                    outputEncoding: null,
                    killProcessOnCancel: false,
                    cancellationToken: default(CancellationToken));
            }

            return builder.ToString();
        }

        /// <summary>
        /// Gathers a list of local group memberships for the current user.
        /// </summary>
        private async Task<string> GetLocalGroupMembership()
        {
            var builder = new StringBuilder();

            string powerShellExe = HostContext.GetService<IPowerShellExeUtil>().GetPath();

            string scriptFile = Path.Combine(HostContext.GetDirectory(WellKnownDirectory.Bin), "powershell", "Get-LocalGroupMembership.ps1").Replace("'", "''");
            ArgUtil.File(scriptFile, nameof(scriptFile));
            string arguments = $@"-NoLogo -Sta -NoProfile -NonInteractive -ExecutionPolicy Unrestricted -Command "". '{scriptFile}'""";

            try
            {
                using (var processInvoker = HostContext.CreateService<IProcessInvoker>())
                {
                    processInvoker.OutputDataReceived += (object sender, ProcessDataReceivedEventArgs args) =>
                    {
                        builder.AppendLine(args.Data);
                    };

                    processInvoker.ErrorDataReceived += (object sender, ProcessDataReceivedEventArgs args) =>
                    {
                        builder.AppendLine(args.Data);
                    };

                    await processInvoker.ExecuteAsync(
                        workingDirectory: HostContext.GetDirectory(WellKnownDirectory.Bin),
                        fileName: powerShellExe,
                        arguments: arguments,
                        environment: null,
                        requireExitCodeZero: false,
                        outputEncoding: null,
                        killProcessOnCancel: false,
                        cancellationToken: default(CancellationToken));
                }
            }
            catch (Exception ex)
            {
                builder.AppendLine(ex.Message);
            }

            return builder.ToString();
        }

        private async Task<string> GetEnvironmentContentNonWindows(int agentId, string agentName, IList<Pipelines.JobStep> steps)
        {
            var builder = new StringBuilder();

            builder.AppendLine($"Environment file created at(UTC): {DateTime.UtcNow}"); // TODO: Format this like we do in other places.
            builder.AppendLine($"Agent Version: {BuildConstants.AgentPackage.Version}");
            builder.AppendLine($"Agent Id: {agentId}");
            builder.AppendLine($"Agent Name: {agentName}");
            builder.AppendLine($"OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
            builder.AppendLine($"User groups: {await GetUserGroupsOnNonWindows()}");
            builder.AppendLine("Steps:");

            foreach (Pipelines.TaskStep task in steps.OfType<Pipelines.TaskStep>())
            {
                builder.AppendLine($"\tName: {task.Reference.Name} Version: {task.Reference.Version}");
            }

            return builder.ToString();
        }

        /// <summary>
        ///  Get user groups on a non-windows platform using core utility "id".
        /// </summary>
        /// <returns>Returns the string with user groups</returns>
        private async Task<string> GetUserGroupsOnNonWindows()
        {
            var idUtil = WhichUtil.Which("id");
            var stringBuilder = new StringBuilder();
            try
            {
                using (var processInvoker = HostContext.CreateService<IProcessInvoker>())
                {
                    processInvoker.OutputDataReceived += (object sender, ProcessDataReceivedEventArgs mes) =>
                    {
                        stringBuilder.AppendLine(mes.Data);
                    };
                    processInvoker.ErrorDataReceived += (object sender, ProcessDataReceivedEventArgs mes) =>
                    {
                        stringBuilder.AppendLine(mes.Data);
                    };

                    await processInvoker.ExecuteAsync(
                        workingDirectory: HostContext.GetDirectory(WellKnownDirectory.Bin),
                        fileName: idUtil,
                        arguments: "-nG",
                        environment: null,
                        requireExitCodeZero: false,
                        outputEncoding: null,
                        killProcessOnCancel: false,
                        cancellationToken: default(CancellationToken)
                    );
                }
            }
            catch (Exception ex)
            {
                stringBuilder.AppendLine(ex.Message);
            }

            return stringBuilder.ToString();
        }

        // Collects Windows event logs that appeared during the job execution.
        // Dumps the gathered info into a separate file since the logs are long.
        private async Task DumpCurrentJobEventLogs(IExecutionContext executionContext, string logFile, DateTime jobStartTimeUtc)
        {
            string startDate = jobStartTimeUtc.ToString("u");
            string endDate = DateTime.UtcNow.ToString("u");

            string powerShellExe = HostContext.GetService<IPowerShellExeUtil>().GetPath();
            string arguments = $@"
                Get-WinEvent -ListLog * | where {{ $_.RecordCount -gt 0 }} `
                | ForEach-Object {{ Get-WinEvent -ErrorAction SilentlyContinue -FilterHashtable @{{ LogName=$_.LogName; StartTime='{startDate}'; EndTime='{endDate}'; }} }} `
                | Export-CSV {logFile}";
            using (var processInvoker = HostContext.CreateService<IProcessInvoker>())
            {
                await processInvoker.ExecuteAsync(
                    workingDirectory: HostContext.GetDirectory(WellKnownDirectory.Bin),
                    fileName: powerShellExe,
                    arguments: arguments,
                    environment: null,
                    requireExitCodeZero: false,
                    outputEncoding: null,
                    killProcessOnCancel: false,
                    cancellationToken: default(CancellationToken));
            }
        }

        /// <summary>
        ///  Git package verification result using the "debsums" utility.
        /// </summary>
        /// <returns>String with the "debsums" output</returns>
        private async Task<string> GetPackageVerificationResult(string debsumsPath)
        {
            var stringBuilder = new StringBuilder();
            using (var processInvoker = HostContext.CreateService<IProcessInvoker>())
            {
                processInvoker.OutputDataReceived += (object sender, ProcessDataReceivedEventArgs mes) =>
                {
                    stringBuilder.AppendLine(mes.Data);
                };
                processInvoker.ErrorDataReceived += (object sender, ProcessDataReceivedEventArgs mes) =>
                {
                    stringBuilder.AppendLine(mes.Data);
                };

                await processInvoker.ExecuteAsync(
                    workingDirectory: HostContext.GetDirectory(WellKnownDirectory.Bin),
                    fileName: debsumsPath,
                    arguments: string.Empty,
                    environment: null,
                    requireExitCodeZero: false,
                    outputEncoding: null,
                    killProcessOnCancel: false,
                    cancellationToken: default(CancellationToken)
                );
            }

            return stringBuilder.ToString();
        }
    }

    internal static class ExtensionPaths
    {
        public static readonly String WindowsPathToExtensionVersions = "C:\\WindowsAzure\\Logs\\Plugins\\Microsoft.VisualStudio.Services.TeamServicesAgent";
        public static readonly String LinuxPathToExtensionLogs = "/var/log/azure/Microsoft.VisualStudio.Services.TeamServicesAgentLinux";
    }
}
