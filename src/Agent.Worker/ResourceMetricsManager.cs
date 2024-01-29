// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker.Telemetry;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    [ServiceLocator(Default = typeof(ResourceMetricsManager))]
    public interface IResourceMetricsManager : IAgentService, IDisposable
    {
        Task RunDebugResourceMonitor();
        Task RunMemoryUtilizationMonitor();
        Task RunDiskSpaceUtilizationMonitor();
        Task RunCpuUtilizationMonitor(string taskId);
        void Setup(IExecutionContext context);
        void SetContext(IExecutionContext context);
    }

    public sealed class ResourceMetricsManager : AgentService, IResourceMetricsManager
    {
        const int ACTIVE_MODE_INTERVAL = 5000;
        const int WARNING_MESSAGE_INTERVAL = 10000;
        const int AVAILABLE_DISK_SPACE_PERCENTAGE_THRESHOLD = 5;
        const int AVAILABLE_MEMORY_PERCENTAGE_THRESHOLD = 5;
        const int CPU_UTILIZATION_PERCENTAGE_THRESHOLD = 95;

        IExecutionContext _context;

        public void Setup(IExecutionContext context)
        {
            //initial context
            ArgUtil.NotNull(context, nameof(context));

            _context = context;

            _currentProcess = Process.GetCurrentProcess();
        }

        public void SetContext(IExecutionContext context)
        {
            ArgUtil.NotNull(context, nameof(context));
            _context = context;
        }
        public async Task RunDebugResourceMonitor()
        {
            while (!_context.CancellationToken.IsCancellationRequested)
            {
                _context.Debug(StringUtil.Loc("ResourceMonitorAgentEnvironmentResource", GetDiskInfoString(), GetMemoryInfoString(), GetCpuInfoString()));
                await Task.Delay(ACTIVE_MODE_INTERVAL, _context.CancellationToken);
            }
        }

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

        public async Task RunDiskSpaceUtilizationMonitor() 
        {
            while (!_context.CancellationToken.IsCancellationRequested)
            {
                try
                {
                    var diskInfo = GetDiskInfo();

                    var freeDiskSpacePercentage = Math.Round(((diskInfo.FreeDiskSpaceMB / (double)diskInfo.TotalDiskSpaceMB) * 100.0), 2);
                    var usedDiskSpacePercentage = 100.0 - freeDiskSpacePercentage;

                    if (freeDiskSpacePercentage <= AVAILABLE_DISK_SPACE_PERCENTAGE_THRESHOLD)
                    {
                        _context.Warning(StringUtil.Loc("ResourceMonitorFreeDiskSpaceIsLowerThanThreshold", diskInfo.VolumeLabel, AVAILABLE_DISK_SPACE_PERCENTAGE_THRESHOLD, $"{usedDiskSpacePercentage:0.00}"));
                        
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

        public async Task RunMemoryUtilizationMonitor()
        {
            while (!_context.CancellationToken.IsCancellationRequested)
            {
                try
                {
                    var memoryInfo = GetMemoryInfo();

                    var usedMemoryPercentage = Math.Round(((memoryInfo.UsedMemoryMB / (double)memoryInfo.TotalMemoryMB) * 100.0), 2);
                    var freeMemoryPercentage = 100.0 - usedMemoryPercentage;

                    if (freeMemoryPercentage <= AVAILABLE_MEMORY_PERCENTAGE_THRESHOLD)
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

        public async Task RunCpuUtilizationMonitor(string taskId)
        {
            while (!_context.CancellationToken.IsCancellationRequested)
            {
                try
                {
                    var usedCpuPercentage = GetCpuInfo();

                    if (usedCpuPercentage >= CPU_UTILIZATION_PERCENTAGE_THRESHOLD)
                    {
                        string message = $"CPU utilization is higher than {CPU_UTILIZATION_PERCENTAGE_THRESHOLD}%; currently used: {usedCpuPercentage:0.00}%";

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

        private struct DiskInfo
        {
            public long TotalDiskSpaceMB;
            public long FreeDiskSpaceMB;
            public string VolumeLabel;
        }

        private DiskInfo GetDiskInfo()
        {
            DiskInfo diskInfo = new();

            string root = Path.GetPathRoot(System.Reflection.Assembly.GetEntryAssembly().Location);
            var driveInfo = new DriveInfo(root);

            diskInfo.TotalDiskSpaceMB = driveInfo.TotalSize / 1048576;
            diskInfo.FreeDiskSpaceMB = driveInfo.AvailableFreeSpace / 1048576;

            if (PlatformUtil.RunningOnWindows)
            {
                diskInfo.VolumeLabel = $"{root} {driveInfo.VolumeLabel}";
            }

            return diskInfo;
        }

        private string GetDiskInfoString()
        {
            try
            {
                var diskInfo = GetDiskInfo();

                return StringUtil.Loc("ResourceMonitorDiskInfo", diskInfo.VolumeLabel, $"{diskInfo.FreeDiskSpaceMB:0.00}", $"{diskInfo.TotalDiskSpaceMB:0.00}");

            }
            catch (Exception ex)
            {
                return StringUtil.Loc("ResourceMonitorDiskInfoError", ex.Message);
            }
        }

        private Process _currentProcess;

        private double GetCpuInfo()
        {            
            TimeSpan totalCpuTime = _currentProcess.TotalProcessorTime;
            TimeSpan elapsedTime = DateTime.Now - _currentProcess.StartTime;

            double cpuUsage = (totalCpuTime.TotalMilliseconds / elapsedTime.TotalMilliseconds) * 100.0;

            return cpuUsage;
        }

        private string GetCpuInfoString()
        {
            try
            {
                return StringUtil.Loc("ResourceMonitorCPUInfo", $"{GetCpuInfo():0.00}");
            }
            catch (Exception ex)
            {
                return StringUtil.Loc("ResourceMonitorCPUInfoError", ex.Message);
            }
        }

        public struct MemoryInfo
        {
            public long TotalMemoryMB;
            public long UsedMemoryMB;
        }

        private MemoryInfo GetMemoryInfo()
        {
            MemoryInfo memoryInfo = new();

            ProcessStartInfo processStartInfo = new ProcessStartInfo();
            var processStartInfoOutput = "";

            if (PlatformUtil.RunningOnWindows)
            {
                processStartInfo.FileName = "wmic";
                processStartInfo.Arguments = "OS GET FreePhysicalMemory,TotalVisibleMemorySize /Value";
                processStartInfo.RedirectStandardOutput = true;

                using (var process = Process.Start(processStartInfo))
                {
                    processStartInfoOutput = process.StandardOutput.ReadToEnd();
                }

                var processStartInfoOutputString = processStartInfoOutput.Trim().Split("\n");

                var freeMemory = Int32.Parse(processStartInfoOutputString[0].Split("=", StringSplitOptions.RemoveEmptyEntries)[1]);
                var totalMemory = Int32.Parse(processStartInfoOutputString[1].Split("=", StringSplitOptions.RemoveEmptyEntries)[1]);

                memoryInfo.TotalMemoryMB = totalMemory / 1024;
                memoryInfo.UsedMemoryMB = (totalMemory - freeMemory) / 1024;
            }

            if (PlatformUtil.RunningOnLinux)
            {
                // Some compact Linux distributions like UBI may not have "free" utility installed, or it may have a custom output
                // We don't want to break currently existing pipelines with ADO warnings
                // so related errors thrown here will be sent to the trace or debug logs by caller methods

                try
                {
                    processStartInfo.FileName = "free";
                    processStartInfo.Arguments = "-m";
                    processStartInfo.RedirectStandardOutput = true;

                    using (var process = Process.Start(processStartInfo))
                    {
                        processStartInfoOutput = process.StandardOutput.ReadToEnd();
                    }

                    var processStartInfoOutputString = processStartInfoOutput.Split("\n");
                    var memoryInfoString = processStartInfoOutputString[1].Split(" ", StringSplitOptions.RemoveEmptyEntries);

                    if (memoryInfoString.Length != 7)
                    {
                        throw new Exception("\"free\" utility has non-default output");
                    }

                    memoryInfo.TotalMemoryMB = Int32.Parse(memoryInfoString[1]);
                    memoryInfo.UsedMemoryMB = Int32.Parse(memoryInfoString[2]);
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

                processStartInfo.FileName = "vm_stat";
                processStartInfo.RedirectStandardOutput = true;

                using (var process = Process.Start(processStartInfo))
                {
                    processStartInfoOutput = process.StandardOutput.ReadToEnd();
                }
                    
                var processStartInfoOutputString = processStartInfoOutput.Split("\n");

                var pageSize = Int32.Parse(processStartInfoOutputString[0].Split(" ", StringSplitOptions.RemoveEmptyEntries)[7]);

                var pagesFree = Int64.Parse(processStartInfoOutputString[1].Split(" ", StringSplitOptions.RemoveEmptyEntries)[2].Trim('.'));
                var pagesActive = Int64.Parse(processStartInfoOutputString[2].Split(" ", StringSplitOptions.RemoveEmptyEntries)[2].Trim('.'));
                var pagesInactive = Int64.Parse(processStartInfoOutputString[3].Split(" ", StringSplitOptions.RemoveEmptyEntries)[2].Trim('.'));
                var pagesSpeculative = Int64.Parse(processStartInfoOutputString[4].Split(" ", StringSplitOptions.RemoveEmptyEntries)[2].Trim('.'));
                var pagesWiredDown = Int64.Parse(processStartInfoOutputString[6].Split(" ", StringSplitOptions.RemoveEmptyEntries)[3].Trim('.'));
                var pagesOccupied = Int64.Parse(processStartInfoOutputString[16].Split(" ", StringSplitOptions.RemoveEmptyEntries)[4].Trim('.'));

                var freeMemory = (pagesFree + pagesInactive) * pageSize;
                var usedMemory = (pagesActive + pagesSpeculative + pagesWiredDown + pagesOccupied) * pageSize;

                memoryInfo.TotalMemoryMB = (freeMemory + usedMemory) / 1048576;
                memoryInfo.UsedMemoryMB = usedMemory / 1048576;
            }

            return memoryInfo;
        }

        private string GetMemoryInfoString()
        {
            try
            {
                var memoryInfo = GetMemoryInfo();

                return StringUtil.Loc("ResourceMonitorMemoryInfo", $"{memoryInfo.UsedMemoryMB:0.00}", $"{memoryInfo.TotalMemoryMB:0.00}");
            }
            catch (Exception ex)
            {
                return StringUtil.Loc("ResourceMonitorMemoryInfoError", ex.Message);
            }
        }

        public void Dispose()
        {
            _currentProcess?.Dispose();
        }
    }
}
