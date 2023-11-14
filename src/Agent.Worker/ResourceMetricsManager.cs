// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Agent.Sdk;

using Microsoft.VisualStudio.Services.Agent.Util;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    [ServiceLocator(Default = typeof(ResourceMetricsManager))]
    public interface IResourceMetricsManager : IAgentService, IDisposable
    {
        Task Run();
        void Setup(IExecutionContext context);
        void SetContext(IExecutionContext context);
    }

    public sealed class ResourceMetricsManager : AgentService, IResourceMetricsManager
    {
        const int ACTIVE_MODE_INTERVAL = 5000;
        IExecutionContext _context;
        private ITerminal _terminal;

        public void Setup(IExecutionContext context)
        {
            //initializa context
            ArgUtil.NotNull(context, nameof(context));
            _context = context;

            try
            {
                _currentProcess = Process.GetCurrentProcess();
            }
            catch (Exception ex)
            {
                _context.Warning($"Unable to get current process, ex:{ex.Message}");
            }
        }

        public void SetContext(IExecutionContext context)
        {
            ArgUtil.NotNull(context, nameof(context));
            _context = context;
        }

        public async Task Run()
        {
            while (!_context.CancellationToken.IsCancellationRequested)
            {
                _context.Debug($"Agent running environment resource - {GetDiskInfo()}, {GetMemoryInfo(_terminal)}, {GetCpuInfo()}");
                await Task.Delay(ACTIVE_MODE_INTERVAL, _context.CancellationToken);
            }
        }
        public string GetDiskInfo()
        {
            try
            {
                string root = Path.GetPathRoot(System.Reflection.Assembly.GetEntryAssembly().Location);

                var s = new DriveInfo(root);
                var diskLabel = string.Empty;

                if (PlatformUtil.RunningOnWindows)
                    diskLabel = $"{root} {s.VolumeLabel}";

                return $"Disk:{diskLabel} available:{s.AvailableFreeSpace / c_mb:0.00}MB out of {s.TotalSize / c_mb:0.00}MB";

            }
            catch (Exception ex)
            {
                return $"Unable to get Disk info, ex:{ex.Message}";
            }
        }

        private const int c_mb = 1024 * 1024;

        private Process _currentProcess;

        public string GetCpuInfo()
        {
            if (_currentProcess == null)
                return $"Unable to get CPU info";
            try
            {
                TimeSpan totalCpuTime = _currentProcess.TotalProcessorTime;
                TimeSpan elapsedTime = DateTime.Now - _currentProcess.StartTime;
                double cpuUsage = totalCpuTime.TotalMilliseconds / elapsedTime.TotalMilliseconds * 100.0;

                return $"CPU: usage {cpuUsage:0.00}";
            }
            catch (Exception ex)
            {
                return $"Unable to get CPU info, ex:{ex.Message}";
            }
        }

        public string GetMemoryInfo(ITerminal terminal)
        {
            try
            {
                var processes = Process.GetProcesses();


                var gcMemoryInfo = GC.GetGCMemoryInfo();
                var installedMemory = (int)(gcMemoryInfo.TotalAvailableMemoryBytes / c_mb);

                // Since Agent contains multiple processes, we need to sum up all the memory usage
                ulong totalUsedMemory = 0;
                
                var siblingProcesses = WindowsProcessUtil.GetProcessList(_currentProcess);
                foreach (Process proc in siblingProcesses)
                {
                    totalUsedMemory += (ulong)(proc.WorkingSet64 / c_mb);
                }

                return $"Memory: used {totalUsedMemory}MB out of {installedMemory}MB";
            }
            catch (Exception ex)
            {
                return $"Unable to get Memory info, ex:{ex.Message}";
            }
        }

        public void Dispose()
        {
            _currentProcess?.Dispose();
        }
    }
}
