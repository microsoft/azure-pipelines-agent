// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    internal readonly struct ResourceCpuInfo
    {
        public ResourceCpuInfo(double usage)
        {
            Usage = usage;
        }

        public double Usage { get; }
    }

    internal readonly struct ResourceDiskInfo
    {
        public ResourceDiskInfo(string volumeRoot, double totalDiskSpaceMB, double freeDiskSpaceMB)
        {
            VolumeRoot = volumeRoot;
            TotalDiskSpaceMB = totalDiskSpaceMB;
            FreeDiskSpaceMB = freeDiskSpaceMB;
        }

        public string VolumeRoot { get; }
        public double TotalDiskSpaceMB { get; }
        public double FreeDiskSpaceMB { get; }
    }

    internal readonly struct ResourceMemoryInfo
    {
        public ResourceMemoryInfo(long totalMemoryMB, long usedMemoryMB)
        {
            TotalMemoryMB = totalMemoryMB;
            UsedMemoryMB = usedMemoryMB;
        }

        public long TotalMemoryMB { get; }
        public long UsedMemoryMB { get; }
    }

    internal abstract class ResourceMetricsProviderBase
    {
        public abstract Task<ResourceCpuInfo> GetCpuInfoAsync(CancellationToken cancellationToken);
        public abstract ResourceDiskInfo GetDiskInfo(string workFolder);
        public abstract Task<ResourceMemoryInfo> GetMemoryInfoAsync(CancellationToken cancellationToken);
    }

    internal sealed class ResourceMetricsProvider : ResourceMetricsProviderBase, IDisposable
    {
        private static readonly TimeSpan _cacheDuration = TimeSpan.FromSeconds(5);

        private readonly IHostContext _hostContext;
        [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Tracing is owned by the host context.")]
        private readonly Tracing _trace;
        private readonly SemaphoreSlim _cpuSemaphore = new SemaphoreSlim(1, 1);
        private readonly object _diskLock = new object();
        private readonly SemaphoreSlim _memorySemaphore = new SemaphoreSlim(1, 1);

        private ResourceCpuInfo _cpuInfo;
        private DateTime _cpuUpdated;
        private ResourceDiskInfo _diskInfo;
        private DateTime _diskUpdated;
        private int _disposed;
        private ResourceMemoryInfo _memoryInfo;
        private DateTime _memoryUpdated;

        public ResourceMetricsProvider(IHostContext hostContext)
        {
            _hostContext = hostContext;
            _trace = hostContext.GetTrace(nameof(ResourceMetricsProvider));
        }

        public override async Task<ResourceCpuInfo> GetCpuInfoAsync(CancellationToken cancellationToken)
        {
            await _cpuSemaphore.WaitAsync(cancellationToken);
            try
            {
                if (IsFresh(_cpuUpdated))
                {
                    return _cpuInfo;
                }

                _cpuInfo = new ResourceCpuInfo(await GetCpuUsageAsync(cancellationToken));
                _cpuUpdated = DateTime.UtcNow;
                return _cpuInfo;
            }
            finally
            {
                _cpuSemaphore.Release();
            }
        }

        public override ResourceDiskInfo GetDiskInfo(string workFolder)
        {
            lock (_diskLock)
            {
                if (IsFresh(_diskUpdated))
                {
                    return _diskInfo;
                }

                string root = Path.GetPathRoot(workFolder);
                var driveInfo = new DriveInfo(root);
                _diskInfo = new ResourceDiskInfo(
                    root,
                    (double)driveInfo.TotalSize / 1048576,
                    (double)driveInfo.AvailableFreeSpace / 1048576);
                _diskUpdated = DateTime.UtcNow;
                return _diskInfo;
            }
        }

        public override async Task<ResourceMemoryInfo> GetMemoryInfoAsync(CancellationToken cancellationToken)
        {
            await _memorySemaphore.WaitAsync(cancellationToken);
            try
            {
                if (IsFresh(_memoryUpdated))
                {
                    return _memoryInfo;
                }

                _memoryInfo = await GetMemoryUsageAsync(cancellationToken);
                _memoryUpdated = DateTime.UtcNow;
                return _memoryInfo;
            }
            finally
            {
                _memorySemaphore.Release();
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _cpuSemaphore.Dispose();
            _memorySemaphore.Dispose();
        }

        private static bool IsFresh(DateTime updated)
        {
            return updated >= DateTime.UtcNow - _cacheDuration;
        }

        private async Task<double> GetCpuUsageAsync(CancellationToken cancellationToken)
        {
            if (PlatformUtil.RunningOnWindows)
            {
                return await Task.Run(() =>
                {
                    using var query = new ManagementObjectSearcher("SELECT PercentIdleTime FROM Win32_PerfFormattedData_PerfOS_Processor WHERE Name=\"_Total\"");
                    ManagementObject cpuInfo = query.Get().OfType<ManagementObject>().FirstOrDefault() ?? throw new Exception("Failed to execute WMI query");
                    return 100 - Convert.ToDouble(cpuInfo["PercentIdleTime"]);
                }, cancellationToken);
            }

            if (PlatformUtil.RunningOnLinux)
            {
                var samples = new List<float[]>();
                const int samplesCount = 10;

                for (int i = 0; i < samplesCount + 1; i++)
                {
                    string[] lines = await File.ReadAllLinesAsync("/proc/stat", cancellationToken);
                    samples.Add(lines[0]
                        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Skip(1)
                        .Select(float.Parse)
                        .ToArray());

                    await Task.Delay(100, cancellationToken);
                }

                double cpuUsage = 0.0;
                for (int i = 1; i < samplesCount + 1; i++)
                {
                    double idle = samples[i][3] - samples[i - 1][3];
                    double total = samples[i].Sum() - samples[i - 1].Sum();
                    cpuUsage += 1.0 - (idle / total);
                }

                return (cpuUsage / samplesCount) * 100;
            }

            if (PlatformUtil.RunningOnMacOS)
            {
                using var processInvoker = _hostContext.CreateService<IProcessInvoker>();
                var outputs = new List<string>();
                processInvoker.OutputDataReceived += delegate (object sender, ProcessDataReceivedEventArgs message)
                {
                    outputs.Add(message.Data);
                };
                processInvoker.ErrorDataReceived += delegate (object sender, ProcessDataReceivedEventArgs message)
                {
                    _trace.Error($"Error on receiving CPU info: {message.Data}");
                };

                await processInvoker.ExecuteAsync(
                    workingDirectory: string.Empty,
                    fileName: "/bin/bash",
                    arguments: "-c \"top -l 2 -o cpu | grep ^CPU\"",
                    environment: null,
                    requireExitCodeZero: true,
                    outputEncoding: null,
                    killProcessOnCancel: true,
                    cancellationToken: cancellationToken);

                double cpuInfoIdle = double.Parse(outputs[1].Split(' ', StringSplitOptions.RemoveEmptyEntries)[6].Trim('%'));
                return 100 - cpuInfoIdle;
            }

            return 0;
        }

        private async Task<ResourceMemoryInfo> GetMemoryUsageAsync(CancellationToken cancellationToken)
        {
            if (PlatformUtil.RunningOnWindows)
            {
                return await Task.Run(() =>
                {
                    using var query = new ManagementObjectSearcher("SELECT FreePhysicalMemory, TotalVisibleMemorySize FROM CIM_OperatingSystem");
                    ManagementObject memoryInfo = query.Get().OfType<ManagementObject>().FirstOrDefault() ?? throw new Exception("Failed to execute WMI query");
                    long freeMemory = Convert.ToInt64(memoryInfo["FreePhysicalMemory"]);
                    long totalMemory = Convert.ToInt64(memoryInfo["TotalVisibleMemorySize"]);
                    return new ResourceMemoryInfo(totalMemory / 1024, (totalMemory - freeMemory) / 1024);
                }, cancellationToken);
            }

            if (PlatformUtil.RunningOnLinux)
            {
                string[] memoryInfo = await File.ReadAllLinesAsync("/proc/meminfo", cancellationToken);
                int totalMemory = int.Parse(memoryInfo[0].Split(" ", StringSplitOptions.RemoveEmptyEntries)[1]);
                int availableMemory = int.Parse(memoryInfo[2].Split(" ", StringSplitOptions.RemoveEmptyEntries)[1]);
                return new ResourceMemoryInfo(totalMemory / 1024, (totalMemory - availableMemory) / 1024);
            }

            if (PlatformUtil.RunningOnMacOS)
            {
                using var processInvoker = _hostContext.CreateService<IProcessInvoker>();
                var outputs = new List<string>();
                processInvoker.OutputDataReceived += delegate (object sender, ProcessDataReceivedEventArgs message)
                {
                    outputs.Add(message.Data);
                };
                processInvoker.ErrorDataReceived += delegate (object sender, ProcessDataReceivedEventArgs message)
                {
                    _trace.Error($"Error on receiving memory info: {message.Data}");
                };

                await processInvoker.ExecuteAsync(
                    workingDirectory: string.Empty,
                    fileName: "vm_stat",
                    arguments: string.Empty,
                    environment: null,
                    requireExitCodeZero: true,
                    outputEncoding: null,
                    killProcessOnCancel: true,
                    cancellationToken: cancellationToken);

                int pageSize = int.Parse(outputs[0].Split(" ", StringSplitOptions.RemoveEmptyEntries)[7]);
                long pagesFree = long.Parse(outputs[1].Split(" ", StringSplitOptions.RemoveEmptyEntries)[2].Trim('.'));
                long pagesActive = long.Parse(outputs[2].Split(" ", StringSplitOptions.RemoveEmptyEntries)[2].Trim('.'));
                long pagesInactive = long.Parse(outputs[3].Split(" ", StringSplitOptions.RemoveEmptyEntries)[2].Trim('.'));
                long pagesSpeculative = long.Parse(outputs[4].Split(" ", StringSplitOptions.RemoveEmptyEntries)[2].Trim('.'));
                long pagesWiredDown = long.Parse(outputs[6].Split(" ", StringSplitOptions.RemoveEmptyEntries)[3].Trim('.'));
                long pagesOccupied = long.Parse(outputs[16].Split(" ", StringSplitOptions.RemoveEmptyEntries)[4].Trim('.'));

                long freeMemory = (pagesFree + pagesInactive) * pageSize;
                long usedMemory = (pagesActive + pagesSpeculative + pagesWiredDown + pagesOccupied) * pageSize;
                return new ResourceMemoryInfo((freeMemory + usedMemory) / 1048576, usedMemory / 1048576);
            }

            return default;
        }
    }
}
