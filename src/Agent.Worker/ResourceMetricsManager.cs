// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker.Telemetry;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    [ServiceLocator(Default = typeof(ResourceMetricsManager))]
    public interface IResourceMetricsManager : IAgentService
    {
        Task RunDebugResourceMonitorAsync();
        Task RunMemoryUtilizationMonitorAsync();
        Task RunDiskSpaceUtilizationMonitorAsync();
        Task RunCpuUtilizationMonitorAsync(string taskId);
        void SetContext(IExecutionContext context);
    }

    public sealed class ResourceMetricsManager : AgentService, IResourceMetricsManager
    {
        #region MonitorProperties
        private IExecutionContext _context;

        private const int METRICS_UPDATE_INTERVAL = 5000;
        private const int ACTIVE_MODE_INTERVAL = 5000;
        private const int WARNING_MESSAGE_INTERVAL = 5000;
        private const int AVAILABLE_DISK_SPACE_PERCENTAGE_THRESHOLD = 5;
        private const int AVAILABLE_MEMORY_PERCENTAGE_THRESHOLD = 5;
        private const int CPU_UTILIZATION_PERCENTAGE_THRESHOLD = 95;

        private static CpuInfo _cpuInfo;
        private static DiskInfo _diskInfo;
        private static MemoryInfo _memoryInfo;

        private static readonly object _cpuInfoLock = new object();
        private static readonly object _diskInfoLock = new object();
        private static readonly object _memoryInfoLock = new object();
        #endregion

        #region MetricStructs
        private struct CpuInfo
        {
            public DateTime Updated;
            public double Usage;
        }

        private struct DiskInfo
        {
            public DateTime Updated;
            public double TotalDiskSpaceMB;
            public double FreeDiskSpaceMB;
            public string VolumeRoot;
        }

        public struct MemoryInfo
        {
            public DateTime Updated;
            public long TotalMemoryMB;
            public long UsedMemoryMB;
        }
        #endregion

        #region InitMethods
        public void SetContext(IExecutionContext context)
        {
            ArgUtil.NotNull(context, nameof(context));
            _context = context;
        }
        #endregion

        #region MiscMethods
        private void PublishTelemetry(string message, string taskId)
        {
            try
            {
                Dictionary<string, string> telemetryData = new Dictionary<string, string>
                        {
                            { "TaskId", taskId },
                            { "JobId", _context.Variables.System_JobId.ToString() },
                            { "PlanId", _context.Variables.Get(Constants.Variables.System.PlanId) },
                            { "Warning", message }
                        };

                var cmd = new Command("telemetry", "publish")
                {
                    Data = JsonConvert.SerializeObject(telemetryData, Formatting.None)
                };

                cmd.Properties.Add("area", "AzurePipelinesAgent");
                cmd.Properties.Add("feature", "ResourceUtilization");

                var publishTelemetryCmd = new TelemetryCommandExtension();
                publishTelemetryCmd.Initialize(HostContext);
                publishTelemetryCmd.ProcessCommand(_context, cmd);
            }
            catch (Exception ex)
            {
                Trace.Warning($"Unable to publish resource utilization telemetry data. Exception: {ex.Message}");
            }
        }
        #endregion

        #region MetricMethods
        private async Task GetCpuInfoAsync(CancellationToken cancellationToken)
        {
            if (_cpuInfo.Updated >= DateTime.Now - TimeSpan.FromMilliseconds(METRICS_UPDATE_INTERVAL))
            {
                return;
            }

            if (PlatformUtil.RunningOnWindows)
            {
                await Task.Run(() =>
                {
                    using var query = new ManagementObjectSearcher("SELECT PercentIdleTime FROM Win32_PerfFormattedData_PerfOS_Processor WHERE Name=\"_Total\"");

                    ManagementObject cpuInfo = query.Get().OfType<ManagementObject>().FirstOrDefault() ?? throw new Exception("Failed to execute WMI query");
                    var cpuInfoIdle = Convert.ToDouble(cpuInfo["PercentIdleTime"]);

                    lock (_cpuInfoLock)
                    {
                        _cpuInfo.Updated = DateTime.Now;
                        _cpuInfo.Usage = 100 - cpuInfoIdle;
                    }
                }, cancellationToken);
            }

            if (PlatformUtil.RunningOnLinux)
            {
                List<float[]> samples = new();
                int samplesCount = 10;

                // /proc/stat updates linearly in real time and shows CPU time counters during the whole system uptime
                // so we need to collect multiple samples to calculate CPU usage
                for (int i = 0; i < samplesCount + 1; i++)
                {
                    string[] strings = await File.ReadAllLinesAsync("/proc/stat", cancellationToken);
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    samples.Add(strings[0]
                            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                            .Skip(1)
                            .Select(float.Parse)
                            .ToArray());

                    await Task.Delay(100, cancellationToken);
                }

                // The CPU time counters in the /proc/stat are:
                // user, nice, system, idle, iowait, irq, softirq, steal, guest, guest_nice
                //
                // We need to get deltas for idle and total CPU time using the gathered samples
                // and calculate the average to provide the CPU utilization in the moment
                double cpuUsage = 0.0;
                for (int i = 1; i < samplesCount + 1; i++)
                {
                    double idle = samples[i][3] - samples[i - 1][3];
                    double total = samples[i].Sum() - samples[i - 1].Sum();

                    cpuUsage += 1.0 - (idle / total);
                }

                lock (_cpuInfoLock)
                {
                    _cpuInfo.Updated = DateTime.Now;
                    _cpuInfo.Usage = (cpuUsage / samplesCount) * 100;
                }
            }
            if (PlatformUtil.RunningOnMacOS)
            {
                Trace.Info($"##DEBUG_SB: CPU info - Getting CPU usage on MacOS from source: ");
                using var processInvoker = HostContext.CreateService<IProcessInvoker>();

                List<string> outputs = new List<string>();
                processInvoker.OutputDataReceived += delegate (object sender, ProcessDataReceivedEventArgs message)
                {
                    outputs.Add(message.Data);
                };

                processInvoker.ErrorDataReceived += delegate (object sender, ProcessDataReceivedEventArgs message)
                {
                    Trace.Error($"Error on receiving CPU info: {message.Data}");
                };

                var filePath = "/bin/bash";
                var arguments = "-c \"top -l 2 -o cpu | grep ^CPU\"";
                Trace.Info($"##DEBUG_SB: CPU info - Executing {filePath} {arguments}; source: ");
                await processInvoker.ExecuteAsync(
                        workingDirectory: string.Empty,
                        fileName: filePath,
                        arguments: arguments,
                        environment: null,
                        requireExitCodeZero: true,
                        outputEncoding: null,
                        killProcessOnCancel: true,
                        cancellationToken: cancellationToken);

                // Use second sample for more accurate calculation
                var cpuInfoIdle = double.Parse(outputs[1].Split(' ', (char)StringSplitOptions.RemoveEmptyEntries)[6].Trim('%'));

                lock (_cpuInfoLock)
                {
                    _cpuInfo.Updated = DateTime.Now;
                    _cpuInfo.Usage = 100 - cpuInfoIdle;
                }
            }
        }

        private void GetDiskInfo()
        {
            Trace.Info($"##DEBUG_SB: Getting disk info from source: ");
            if (_diskInfo.Updated >= DateTime.Now - TimeSpan.FromMilliseconds(METRICS_UPDATE_INTERVAL))
            {
                return;
            }

            string root = Path.GetPathRoot(_context.GetVariableValueOrDefault(Constants.Variables.Agent.WorkFolder));
            var driveInfo = new DriveInfo(root);

            lock (_diskInfoLock)
            {
                _diskInfo.Updated = DateTime.Now;
                _diskInfo.TotalDiskSpaceMB = (double)driveInfo.TotalSize / 1048576;
                _diskInfo.FreeDiskSpaceMB = (double)driveInfo.AvailableFreeSpace / 1048576;
                _diskInfo.VolumeRoot = root;
            }
        }

        private async Task GetMemoryInfoAsync(CancellationToken cancellationToken)
        {
            if (_memoryInfo.Updated >= DateTime.Now - TimeSpan.FromMilliseconds(METRICS_UPDATE_INTERVAL))
            {
                return;
            }

            if (PlatformUtil.RunningOnWindows)
            {
                await Task.Run(() =>
                {
                    using var query = new ManagementObjectSearcher("SELECT FreePhysicalMemory, TotalVisibleMemorySize FROM CIM_OperatingSystem");

                    ManagementObject memoryInfo = query.Get().OfType<ManagementObject>().FirstOrDefault() ?? throw new Exception("Failed to execute WMI query");
                    var freeMemory = Convert.ToInt64(memoryInfo["FreePhysicalMemory"]);
                    var totalMemory = Convert.ToInt64(memoryInfo["TotalVisibleMemorySize"]);

                    lock (_memoryInfoLock)
                    {
                        _memoryInfo.Updated = DateTime.Now;
                        _memoryInfo.TotalMemoryMB = totalMemory / 1024;
                        _memoryInfo.UsedMemoryMB = (totalMemory - freeMemory) / 1024;
                    }
                }, cancellationToken);
            }

            if (PlatformUtil.RunningOnLinux)
            {
                string[] memoryInfo = await File.ReadAllLinesAsync("/proc/meminfo", cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                // The /proc/meminfo file contains several memory counters. To calculate the available memory
                // we need to get the total memory and the available memory counters
                // The available memory contains the sum of free, cached, and buffer memory
                // it shows more accurate information about the memory usage than the free memory counter
                int totalMemory = int.Parse(memoryInfo[0].Split(" ", StringSplitOptions.RemoveEmptyEntries)[1]);
                int availableMemory = int.Parse(memoryInfo[2].Split(" ", StringSplitOptions.RemoveEmptyEntries)[1]);

                lock (_memoryInfoLock)
                {
                    _memoryInfo.Updated = DateTime.Now;
                    _memoryInfo.TotalMemoryMB = totalMemory / 1024;
                    _memoryInfo.UsedMemoryMB = (totalMemory - availableMemory) / 1024;
                }
            }

            if (PlatformUtil.RunningOnMacOS)
            {
                // vm_stat allows to get the most detailed information about memory usage on MacOS
                // but unfortunately it returns values in pages and has no built-in arguments for custom output
                // so we need to parse and cast the output manually

                Trace.Info($"##DEBUG_SB: Getting memory info on MacOS from source: ");
                using var processInvoker = HostContext.CreateService<IProcessInvoker>();

                List<string> outputs = new List<string>();
                processInvoker.OutputDataReceived += delegate (object sender, ProcessDataReceivedEventArgs message)
                {
                    outputs.Add(message.Data);
                };

                processInvoker.ErrorDataReceived += delegate (object sender, ProcessDataReceivedEventArgs message)
                {
                    Trace.Error($"Error on receiving memory info: {message.Data}");
                };

                var filePath = "vm_stat";
                Trace.Info($"##DEBUG_SB: Memory info - Executing {filePath}; statement source: ");

                await processInvoker.ExecuteAsync(
                        workingDirectory: string.Empty,
                        fileName: filePath,
                        arguments: string.Empty,
                        environment: null,
                        requireExitCodeZero: true,
                        outputEncoding: null,
                        killProcessOnCancel: true,
                        cancellationToken: cancellationToken);

                var pageSize = int.Parse(outputs[0].Split(" ", StringSplitOptions.RemoveEmptyEntries)[7]);

                var pagesFree = long.Parse(outputs[1].Split(" ", StringSplitOptions.RemoveEmptyEntries)[2].Trim('.'));
                var pagesActive = long.Parse(outputs[2].Split(" ", StringSplitOptions.RemoveEmptyEntries)[2].Trim('.'));
                var pagesInactive = long.Parse(outputs[3].Split(" ", StringSplitOptions.RemoveEmptyEntries)[2].Trim('.'));
                var pagesSpeculative = long.Parse(outputs[4].Split(" ", StringSplitOptions.RemoveEmptyEntries)[2].Trim('.'));
                var pagesWiredDown = long.Parse(outputs[6].Split(" ", StringSplitOptions.RemoveEmptyEntries)[3].Trim('.'));
                var pagesOccupied = long.Parse(outputs[16].Split(" ", StringSplitOptions.RemoveEmptyEntries)[4].Trim('.'));

                var freeMemory = (pagesFree + pagesInactive) * pageSize;
                var usedMemory = (pagesActive + pagesSpeculative + pagesWiredDown + pagesOccupied) * pageSize;

                lock (_memoryInfoLock)
                {
                    _memoryInfo.Updated = DateTime.Now;
                    _memoryInfo.TotalMemoryMB = (freeMemory + usedMemory) / 1048576;
                    _memoryInfo.UsedMemoryMB = usedMemory / 1048576;
                }
            }
        }
        #endregion

        #region StringMethods
        private async Task<string> GetCpuInfoStringAsync(CancellationToken cancellationToken)
        {
            try
            {
                await GetCpuInfoAsync(cancellationToken, source);

                return StringUtil.Loc("ResourceMonitorCPUInfo", $"{_cpuInfo.Usage:0.00}");
            }
            catch (Exception ex)
            {
                return StringUtil.Loc("ResourceMonitorCPUInfoError", ex.Message);
            }
        }

        private string GetDiskInfoString()
        {
            try
            {
                GetDiskInfo(source);

                return StringUtil.Loc("ResourceMonitorDiskInfo",
                    _diskInfo.VolumeRoot,
                    $"{_diskInfo.FreeDiskSpaceMB:0.00}",
                    $"{_diskInfo.TotalDiskSpaceMB:0.00}");
            }
            catch (Exception ex)
            {
                return StringUtil.Loc("ResourceMonitorDiskInfoError", ex.Message);
            }
        }

        private async Task<string> GetMemoryInfoStringAsync(CancellationToken cancellationToken)
        {
            try
            {
                await GetMemoryInfoAsync(cancellationToken, source);

                return StringUtil.Loc("ResourceMonitorMemoryInfo",
                    $"{_memoryInfo.UsedMemoryMB:0.00}", 
                    $"{_memoryInfo.TotalMemoryMB:0.00}");
            }
            catch (Exception ex)
            {
                return StringUtil.Loc("ResourceMonitorMemoryInfoError", ex.Message);
            }
        }
        #endregion

        #region MonitorLoops
        public async Task RunDebugResourceMonitorAsync()
        {
            Trace.Info("##DEBUG_SB: Starting debug resource monitor");
            while (!_context.CancellationToken.IsCancellationRequested)
            {
                using var timeoutTokenSource = new CancellationTokenSource();
                timeoutTokenSource.CancelAfter(TimeSpan.FromMilliseconds(METRICS_UPDATE_INTERVAL));

                using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                    _context.CancellationToken,
                    timeoutTokenSource.Token);

                Trace.Info("##DEBUG_SB: Debug Resource Monitor");
                _context.Debug(StringUtil.Loc("ResourceMonitorAgentEnvironmentResource",
                    GetDiskInfoString(),
                    await GetMemoryInfoStringAsync(linkedTokenSource.Token),
                    await GetCpuInfoStringAsync(linkedTokenSource.Token)));

                Trace.Info($"##DEBUG_SB: Waiting for {ACTIVE_MODE_INTERVAL} ms");
                await Task.Delay(ACTIVE_MODE_INTERVAL, _context.CancellationToken);
                Trace.Info($"##DEBUG_SB: Done waiting for {ACTIVE_MODE_INTERVAL} ms");
            }
            Trace.Info("##DEBUG_SB: Exiting debug resource monitor");
        }

        public async Task RunDiskSpaceUtilizationMonitorAsync()
        {
            Trace.Info("##DEBUG_SB: Starting disk space utilization monitor");
            while (!_context.CancellationToken.IsCancellationRequested)
            {
                try
                {
                    GetDiskInfo();

                    var freeDiskSpacePercentage = Math.Round(((_diskInfo.FreeDiskSpaceMB / (double)_diskInfo.TotalDiskSpaceMB) * 100.0), 2);
                    var usedDiskSpacePercentage = 100.0 - freeDiskSpacePercentage;

                    if (freeDiskSpacePercentage <= AVAILABLE_DISK_SPACE_PERCENTAGE_THRESHOLD)
                    {
                        _context.Warning(StringUtil.Loc("ResourceMonitorFreeDiskSpaceIsLowerThanThreshold",
                            _diskInfo.VolumeRoot,
                            AVAILABLE_DISK_SPACE_PERCENTAGE_THRESHOLD,
                            $"{usedDiskSpacePercentage:0.00}"));

                        break;
                    }
                }
                catch (Exception ex)
                {
                    Trace.Warning($"Unable to get disk info. Exception: {ex.Message}");

                    break;
                }

                Trace.Info($"##DEBUG_SB: Disk Space Utilization - Waiting for {WARNING_MESSAGE_INTERVAL} ms");
                await Task.Delay(WARNING_MESSAGE_INTERVAL, _context.CancellationToken);
                Trace.Info($"##DEBUG_SB: Disk Space Utilization - Done waiting for {WARNING_MESSAGE_INTERVAL} ms");
            }
            Trace.Info("##DEBUG_SB: Exiting disk space utilization monitor");
        }

        public async Task RunMemoryUtilizationMonitorAsync()
        {
            Trace.Info("##DEBUG_SB: Starting memory utilization monitor");
            while (!_context.CancellationToken.IsCancellationRequested)
            {
                using var timeoutTokenSource = new CancellationTokenSource();
                timeoutTokenSource.CancelAfter(TimeSpan.FromMilliseconds(METRICS_UPDATE_INTERVAL));

                using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                    _context.CancellationToken,
                    timeoutTokenSource.Token);

                // display cancellation token and properties
                Trace.Info($"##DEBUG_SB: Memory Utilization - CancellationToken: {linkedTokenSource.Token}");

                try
                {
                    await GetMemoryInfoAsync(linkedTokenSource.Token);

                    var usedMemoryPercentage = Math.Round(((_memoryInfo.UsedMemoryMB / (double)_memoryInfo.TotalMemoryMB) * 100.0), 2);

                    if (100.0 - usedMemoryPercentage <= AVAILABLE_MEMORY_PERCENTAGE_THRESHOLD)
                    {
                        _context.Warning(StringUtil.Loc("ResourceMonitorMemorySpaceIsLowerThanThreshold",
                            AVAILABLE_MEMORY_PERCENTAGE_THRESHOLD,
                            $"{usedMemoryPercentage:0.00}"));

                        break;
                    }
                }
                catch (Exception ex)
                {
                    Trace.Warning($"Unable to get memory info. Exception: {ex.Message}");

                    break;
                }

                Trace.Info($"##DEBUG_SB: Memory Utilization - Waiting for {WARNING_MESSAGE_INTERVAL} ms");
                await Task.Delay(WARNING_MESSAGE_INTERVAL, _context.CancellationToken);
                Trace.Info($"##DEBUG_SB: Memory Utilization - Done waiting for {WARNING_MESSAGE_INTERVAL} ms");
            }

            Trace.Info("##DEBUG_SB: Exiting memory utilization monitor");
        }

        public async Task RunCpuUtilizationMonitorAsync(string taskId)
        {
            Trace.Info("##DEBUG_SB: Starting CPU utilization monitor");
            while (!_context.CancellationToken.IsCancellationRequested)
            {
                using var timeoutTokenSource = new CancellationTokenSource();
                timeoutTokenSource.CancelAfter(TimeSpan.FromMilliseconds(METRICS_UPDATE_INTERVAL));

                using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                    _context.CancellationToken,
                    timeoutTokenSource.Token);

                Trace.Info($"##DEBUG_SB: CPU Utilization - CancellationToken: {linkedTokenSource.Token}");

                try
                {
                    await GetCpuInfoAsync(linkedTokenSource.Token);

                    if (_cpuInfo.Usage >= CPU_UTILIZATION_PERCENTAGE_THRESHOLD)
                    {
                        string message = $"CPU utilization is higher than {CPU_UTILIZATION_PERCENTAGE_THRESHOLD}%; currently used: {_cpuInfo.Usage:0.00}%";

                        PublishTelemetry(message, taskId);

                        break;
                    }

                }
                catch (Exception ex)
                {
                    Trace.Warning($"Unable to get CPU info. Exception: {ex.Message}");

                    break;
                }

                Trace.Info($"##DEBUG_SB: CPU Utilization - Waiting for {WARNING_MESSAGE_INTERVAL} ms");
                await Task.Delay(WARNING_MESSAGE_INTERVAL, _context.CancellationToken);
                Trace.Info($"##DEBUG_SB: CPU Utilization - Done waiting for {WARNING_MESSAGE_INTERVAL} ms");
            }
            Trace.Info("##DEBUG_SB: Exiting CPU utilization monitor");
        }
        #endregion
    }
}
