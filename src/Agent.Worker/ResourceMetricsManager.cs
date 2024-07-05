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
        private async Task GetCpuInfoAsync()
        {
            if (_cpuInfo.Updated >= DateTime.Now - TimeSpan.FromMilliseconds(METRICS_UPDATE_INTERVAL))
            {
                return;
            }

            if (PlatformUtil.RunningOnWindows)
            {
                using var timeoutTokenSource = new CancellationTokenSource();
                timeoutTokenSource.CancelAfter(TimeSpan.FromMilliseconds(METRICS_UPDATE_INTERVAL));

                using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                    _context.CancellationToken,
                    timeoutTokenSource.Token);

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
                }, linkedTokenSource.Token);
            }

            if (PlatformUtil.RunningOnLinux)
            {
                using var processInvoker = HostContext.CreateService<IProcessInvoker>();

                using var timeoutTokenSource = new CancellationTokenSource();
                timeoutTokenSource.CancelAfter(TimeSpan.FromMilliseconds(METRICS_UPDATE_INTERVAL));

                using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                    _context.CancellationToken,
                    timeoutTokenSource.Token);

                processInvoker.OutputDataReceived += delegate (object sender, ProcessDataReceivedEventArgs message)
                {
                    var processInvokerOutput = message.Data;

                    var cpuInfoNice = int.Parse(processInvokerOutput.Split(' ', (char)StringSplitOptions.RemoveEmptyEntries)[2]);
                    var cpuInfoIdle = int.Parse(processInvokerOutput.Split(' ', (char)StringSplitOptions.RemoveEmptyEntries)[4]);
                    var cpuInfoIOWait = int.Parse(processInvokerOutput.Split(' ', (char)StringSplitOptions.RemoveEmptyEntries)[5]);

                    lock (_cpuInfoLock)
                    {
                        _cpuInfo.Updated = DateTime.Now;
                        _cpuInfo.Usage = (double)(cpuInfoNice + cpuInfoIdle) * 100 / (cpuInfoNice + cpuInfoIdle + cpuInfoIOWait);
                    }
                };

                processInvoker.ErrorDataReceived += delegate (object sender, ProcessDataReceivedEventArgs message)
                {
                    Trace.Error($"Error on receiving CPU info: {message.Data}");
                };

                var filePath = "grep";
                var arguments = "\"cpu \" /proc/stat";
                await processInvoker.ExecuteAsync(
                        workingDirectory: string.Empty,
                        fileName: filePath,
                        arguments: arguments,
                        environment: null,
                        requireExitCodeZero: true,
                        outputEncoding: null,
                        killProcessOnCancel: true,
                        cancellationToken: linkedTokenSource.Token);
            }

            if (PlatformUtil.RunningOnMacOS)
            {
                List<string> outputs = new List<string>();

                using var processInvoker = HostContext.CreateService<IProcessInvoker>();

                using var timeoutTokenSource = new CancellationTokenSource();
                timeoutTokenSource.CancelAfter(TimeSpan.FromMilliseconds(METRICS_UPDATE_INTERVAL));

                using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                    _context.CancellationToken,
                    timeoutTokenSource.Token);

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
                await processInvoker.ExecuteAsync(
                        workingDirectory: string.Empty,
                        fileName: filePath,
                        arguments: arguments,
                        environment: null,
                        requireExitCodeZero: true,
                        outputEncoding: null,
                        killProcessOnCancel: true,
                        cancellationToken: linkedTokenSource.Token);

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

        private async Task GetMemoryInfoAsync()
        {
            if (_memoryInfo.Updated >= DateTime.Now - TimeSpan.FromMilliseconds(METRICS_UPDATE_INTERVAL))
            {
                return;
            }

            if (PlatformUtil.RunningOnWindows)
            {
                using var timeoutTokenSource = new CancellationTokenSource();
                timeoutTokenSource.CancelAfter(TimeSpan.FromMilliseconds(METRICS_UPDATE_INTERVAL));

                using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                    _context.CancellationToken,
                    timeoutTokenSource.Token);

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
                }, linkedTokenSource.Token);
            }

            if (PlatformUtil.RunningOnLinux)
            {
                // Some compact Linux distributions like UBI may not have "free" utility installed, or it may have a custom output
                // We don't want to break currently existing pipelines with ADO warnings
                // so related errors thrown here will be sent to the trace or debug logs by caller methods

                using var processInvoker = HostContext.CreateService<IProcessInvoker>();

                using var timeoutTokenSource = new CancellationTokenSource();
                timeoutTokenSource.CancelAfter(TimeSpan.FromMilliseconds(METRICS_UPDATE_INTERVAL));

                using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                    _context.CancellationToken,
                    timeoutTokenSource.Token);

                processInvoker.OutputDataReceived += delegate (object sender, ProcessDataReceivedEventArgs message)
                {
                    if (!message.Data.StartsWith("Mem:"))
                    {
                        return;
                    }

                    var processInvokerOutputString = message.Data;
                    var memoryInfoString = processInvokerOutputString.Split(" ", StringSplitOptions.RemoveEmptyEntries);

                    if (memoryInfoString.Length != 7)
                    {
                        throw new Exception("\"free\" utility has non-default output");
                    }

                    lock (_memoryInfoLock)
                    {
                        _memoryInfo.Updated = DateTime.Now;
                        _memoryInfo.TotalMemoryMB = long.Parse(memoryInfoString[1]);
                        _memoryInfo.UsedMemoryMB = long.Parse(memoryInfoString[2]);
                    }
                };

                processInvoker.ErrorDataReceived += delegate (object sender, ProcessDataReceivedEventArgs message)
                {
                    Trace.Error($"Error on receiving memory info: {message.Data}");
                };

                try
                {
                    var filePath = "free";
                    var arguments = "-m";
                    await processInvoker.ExecuteAsync(
                            workingDirectory: string.Empty,
                            fileName: filePath,
                            arguments: arguments,
                            environment: null,
                            requireExitCodeZero: true,
                            outputEncoding: null,
                            killProcessOnCancel: true,
                            cancellationToken: linkedTokenSource.Token);
                }
                catch (Win32Exception ex)
                {
                    throw new Exception($"\"free\" utility is unavailable. Exception: {ex.Message}");
                }
            }

            if (PlatformUtil.RunningOnMacOS)
            {
                // vm_stat allows to get the most detailed information about memory usage on MacOS
                // but unfortunately it returns values in pages and has no built-in arguments for custom output
                // so we need to parse and cast the output manually

                List<string> outputs = new List<string>();

                using var processInvoker = HostContext.CreateService<IProcessInvoker>();

                using var timeoutTokenSource = new CancellationTokenSource();
                timeoutTokenSource.CancelAfter(TimeSpan.FromMilliseconds(METRICS_UPDATE_INTERVAL));

                using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                    _context.CancellationToken,
                    timeoutTokenSource.Token);

                processInvoker.OutputDataReceived += delegate (object sender, ProcessDataReceivedEventArgs message)
                {
                    outputs.Add(message.Data);
                };

                processInvoker.ErrorDataReceived += delegate (object sender, ProcessDataReceivedEventArgs message)
                {
                    Trace.Error($"Error on receiving memory info: {message.Data}");
                };

                var filePath = "vm_stat";
                await processInvoker.ExecuteAsync(
                        workingDirectory: string.Empty,
                        fileName: filePath,
                        arguments: string.Empty,
                        environment: null,
                        requireExitCodeZero: true,
                        outputEncoding: null,
                        killProcessOnCancel: true,
                        cancellationToken: linkedTokenSource.Token);

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
        private async Task<string> GetCpuInfoStringAsync()
        {
            try
            {
                await GetCpuInfoAsync();

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
                GetDiskInfo();

                return StringUtil.Loc("ResourceMonitorDiskInfo", _diskInfo.VolumeRoot, $"{_diskInfo.FreeDiskSpaceMB:0.00}", $"{_diskInfo.TotalDiskSpaceMB:0.00}");
            }
            catch (Exception ex)
            {
                return StringUtil.Loc("ResourceMonitorDiskInfoError", ex.Message);
            }
        }

        private async Task<string> GetMemoryInfoStringAsync()
        {
            try
            {
                await GetMemoryInfoAsync();

                return StringUtil.Loc("ResourceMonitorMemoryInfo", $"{_memoryInfo.UsedMemoryMB:0.00}", $"{_memoryInfo.TotalMemoryMB:0.00}");
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
            while (!_context.CancellationToken.IsCancellationRequested)
            {
                _context.Debug(StringUtil.Loc("ResourceMonitorAgentEnvironmentResource", GetDiskInfoString(), await GetMemoryInfoStringAsync(), await GetCpuInfoStringAsync()));

                await Task.Delay(ACTIVE_MODE_INTERVAL, _context.CancellationToken);
            }
        }

        public async Task RunDiskSpaceUtilizationMonitorAsync()
        {
            while (!_context.CancellationToken.IsCancellationRequested)
            {
                try
                {
                    GetDiskInfo();

                    var freeDiskSpacePercentage = Math.Round(((_diskInfo.FreeDiskSpaceMB / (double)_diskInfo.TotalDiskSpaceMB) * 100.0), 2);
                    var usedDiskSpacePercentage = 100.0 - freeDiskSpacePercentage;

                    if (freeDiskSpacePercentage <= AVAILABLE_DISK_SPACE_PERCENTAGE_THRESHOLD)
                    {
                        _context.Warning(StringUtil.Loc("ResourceMonitorFreeDiskSpaceIsLowerThanThreshold", _diskInfo.VolumeRoot, AVAILABLE_DISK_SPACE_PERCENTAGE_THRESHOLD, $"{usedDiskSpacePercentage:0.00}"));

                        break;
                    }
                }
                catch (Exception ex)
                {
                    Trace.Warning($"Unable to get disk info. Exception: {ex.Message}");

                    break;
                }

                await Task.Delay(WARNING_MESSAGE_INTERVAL, _context.CancellationToken);
            }
        }

        public async Task RunMemoryUtilizationMonitorAsync()
        {
            while (!_context.CancellationToken.IsCancellationRequested)
            {
                try
                {
                    await GetMemoryInfoAsync();

                    var usedMemoryPercentage = Math.Round(((_memoryInfo.UsedMemoryMB / (double)_memoryInfo.TotalMemoryMB) * 100.0), 2);

                    if (100.0 - usedMemoryPercentage <= AVAILABLE_MEMORY_PERCENTAGE_THRESHOLD)
                    {
                        _context.Warning(StringUtil.Loc("ResourceMonitorMemorySpaceIsLowerThanThreshold", AVAILABLE_MEMORY_PERCENTAGE_THRESHOLD, $"{usedMemoryPercentage:0.00}"));

                        break;
                    }
                }
                catch (Exception ex)
                {
                    Trace.Warning($"Unable to get memory info. Exception: {ex.Message}");

                    break;
                }

                await Task.Delay(WARNING_MESSAGE_INTERVAL, _context.CancellationToken);
            }
        }

        public async Task RunCpuUtilizationMonitorAsync(string taskId)
        {
            while (!_context.CancellationToken.IsCancellationRequested)
            {
                try
                {
                    await GetCpuInfoAsync();

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

                await Task.Delay(WARNING_MESSAGE_INTERVAL, _context.CancellationToken);
            }
        }
        #endregion
    }
}
