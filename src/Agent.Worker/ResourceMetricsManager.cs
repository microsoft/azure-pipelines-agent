// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker.Telemetry;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    [ServiceLocator(Default = typeof(ResourceMetricsManager))]
    public interface IResourceMetricsManager : IAgentService
    {
        void StartMonitoring(IExecutionContext jobContext, bool enableDebugOutput);
        IDisposable AttachTask(IExecutionContext taskContext, Guid taskId, bool enableWarnings);
        Task StopMonitoringAsync();
    }

    public sealed class ResourceMetricsManager : AgentService, IResourceMetricsManager, IDisposable
    {
        private const int AvailableDiskSpacePercentageThreshold = 5;
        private const int AvailableMemoryPercentageThreshold = 5;
        private const int CpuUtilizationPercentageThreshold = 95;

        private readonly object _lifecycleLock = new object();
        private readonly object _taskLock = new object();
        private readonly SemaphoreSlim _cpuSignal = new SemaphoreSlim(0, 1);
        private readonly SemaphoreSlim _diskSignal = new SemaphoreSlim(0, 1);
        private readonly SemaphoreSlim _memorySignal = new SemaphoreSlim(0, 1);

        private CancellationTokenSource _monitorCancellation;
        private Task[] _monitorTasks = Array.Empty<Task>();
        private ActiveTask _activeTask;
        private int _disposed;
        private long _taskGeneration;

        internal ResourceMetricsProviderBase MetricsProvider { get; set; }
        internal TimeSpan MetricsTimeout { get; set; } = TimeSpan.FromSeconds(5);
        internal TimeSpan MonitorInterval { get; set; } = TimeSpan.FromSeconds(5);
        internal TimeSpan MonitorStopTimeout { get; set; } = TimeSpan.FromSeconds(5);

        public override void Initialize(IHostContext hostContext)
        {
            base.Initialize(hostContext);
            MetricsProvider ??= new ResourceMetricsProvider(hostContext);
        }

        public void StartMonitoring(IExecutionContext jobContext, bool enableDebugOutput)
        {
            try
            {
                if (Volatile.Read(ref _disposed) != 0)
                {
                    return;
                }

                if (jobContext == null)
                {
                    Trace.Warning("Resource monitoring was not started because the job context is null.");
                    return;
                }

                CancellationToken jobCancellationToken = jobContext.CancellationToken;
                string workFolder = jobContext.GetVariableValueOrDefault(Constants.Variables.Agent.WorkFolder);

                lock (_lifecycleLock)
                {
                    if (_monitorCancellation != null)
                    {
                        return;
                    }

                    _monitorCancellation = CancellationTokenSource.CreateLinkedTokenSource(jobCancellationToken);
                    CancellationToken monitoringToken = _monitorCancellation.Token;

                    var monitorTasks = new List<Task>
                    {
                        Task.Run(() => RunMemoryMonitorAsync(monitoringToken)),
                        Task.Run(() => RunDiskMonitorAsync(workFolder, monitoringToken)),
                        Task.Run(() => RunCpuMonitorAsync(monitoringToken)),
                    };

                    if (enableDebugOutput)
                    {
                        monitorTasks.Add(Task.Run(() => RunDebugMonitorAsync(workFolder, monitoringToken)));
                    }

                    _monitorTasks = monitorTasks.ToArray();
                }
            }
            catch (Exception ex)
            {
                TraceMonitoringFailure("start resource monitoring", ex);
            }
        }

        public IDisposable AttachTask(IExecutionContext taskContext, Guid taskId, bool enableWarnings)
        {
            try
            {
                if (Volatile.Read(ref _disposed) != 0)
                {
                    return NoopTaskScope.Instance;
                }

                if (taskContext == null)
                {
                    Trace.Warning("Resource monitoring task output was not attached because the task context is null.");
                    return NoopTaskScope.Instance;
                }

                long generation;
                lock (_taskLock)
                {
                    generation = ++_taskGeneration;
                    _activeTask = new ActiveTask(generation, taskContext, taskId, enableWarnings);
                }

                SignalTaskAttached();
                return new TaskScope(this, generation);
            }
            catch (Exception ex)
            {
                TraceMonitoringFailure("attach resource monitoring task output", ex);
                return NoopTaskScope.Instance;
            }
        }

        public async Task StopMonitoringAsync()
        {
            try
            {
                CancellationTokenSource monitorCancellation;
                Task[] monitorTasks;

                lock (_taskLock)
                {
                    _activeTask = null;
                }

                lock (_lifecycleLock)
                {
                    monitorCancellation = _monitorCancellation;
                    monitorTasks = _monitorTasks;
                    _monitorCancellation = null;
                    _monitorTasks = Array.Empty<Task>();
                }

                if (monitorCancellation == null)
                {
                    return;
                }

                try
                {
                    monitorCancellation.Cancel();
                }
                catch (Exception ex)
                {
                    TraceMonitoringFailure("cancel resource monitoring", ex);
                }

                Task monitorCompletion = CompleteStopAsync(Task.WhenAll(monitorTasks), monitorCancellation);
                if (await Task.WhenAny(monitorCompletion, Task.Delay(MonitorStopTimeout)) != monitorCompletion)
                {
                    Trace.Warning($"Resource monitoring did not stop within {MonitorStopTimeout}. Job execution will continue without waiting for it.");
                    return;
                }

                await monitorCompletion;
            }
            catch (Exception ex)
            {
                TraceMonitoringFailure("stop resource monitoring", ex);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                StopMonitoringAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                TraceMonitoringFailure("dispose resource monitoring", ex);
            }

            try
            {
                _cpuSignal.Dispose();
                _diskSignal.Dispose();
                _memorySignal.Dispose();

                if (MetricsProvider is IDisposable disposableProvider)
                {
                    disposableProvider.Dispose();
                }
            }
            catch (Exception ex)
            {
                TraceMonitoringFailure("dispose resource monitoring resources", ex);
            }
        }

        private async Task RunMemoryMonitorAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (TryGetActiveTaskGeneration(requireWarnings: true, out long taskGeneration))
                {
                    try
                    {
                        using CancellationTokenSource timeout = CreateMetricsTimeout(cancellationToken);
                        ResourceMemoryInfo memoryInfo = await MetricsProvider.GetMemoryInfoAsync(timeout.Token);
                        double usedMemoryPercentage = Math.Round(memoryInfo.UsedMemoryMB / (double)memoryInfo.TotalMemoryMB * 100.0, 2);

                        if (100.0 - usedMemoryPercentage <= AvailableMemoryPercentageThreshold)
                        {
                            TryWriteWarning(
                                taskGeneration,
                                ResourceWarning.Memory,
                                StringUtil.Loc("ResourceMonitorMemorySpaceIsLowerThanThreshold",
                                    AvailableMemoryPercentageThreshold,
                                    $"{usedMemoryPercentage:0.00}"));
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        TraceMonitoringFailure("collect memory utilization", ex);
                    }
                }

                if (!await WaitForNextCheckAsync(_memorySignal, cancellationToken))
                {
                    return;
                }
            }
        }

        private async Task RunDiskMonitorAsync(string workFolder, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (TryGetActiveTaskGeneration(requireWarnings: true, out long taskGeneration))
                {
                    try
                    {
                        ResourceDiskInfo diskInfo = MetricsProvider.GetDiskInfo(workFolder);
                        double freeDiskSpacePercentage = Math.Round(diskInfo.FreeDiskSpaceMB / diskInfo.TotalDiskSpaceMB * 100.0, 2);
                        double usedDiskSpacePercentage = 100.0 - freeDiskSpacePercentage;

                        if (freeDiskSpacePercentage <= AvailableDiskSpacePercentageThreshold)
                        {
                            TryWriteWarning(
                                taskGeneration,
                                ResourceWarning.Disk,
                                StringUtil.Loc("ResourceMonitorFreeDiskSpaceIsLowerThanThreshold",
                                    diskInfo.VolumeRoot,
                                    AvailableDiskSpacePercentageThreshold,
                                    $"{usedDiskSpacePercentage:0.00}"));
                        }
                    }
                    catch (Exception ex)
                    {
                        TraceMonitoringFailure("collect disk utilization", ex);
                    }
                }

                if (!await WaitForNextCheckAsync(_diskSignal, cancellationToken))
                {
                    return;
                }
            }
        }

        private async Task RunCpuMonitorAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (TryGetActiveTaskGeneration(requireWarnings: true, out long taskGeneration))
                {
                    try
                    {
                        using CancellationTokenSource timeout = CreateMetricsTimeout(cancellationToken);
                        ResourceCpuInfo cpuInfo = await MetricsProvider.GetCpuInfoAsync(timeout.Token);

                        if (cpuInfo.Usage >= CpuUtilizationPercentageThreshold)
                        {
                            TryPublishCpuTelemetry(
                                taskGeneration,
                                $"CPU utilization is higher than {CpuUtilizationPercentageThreshold}%; currently used: {cpuInfo.Usage:0.00}%");
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        TraceMonitoringFailure("collect CPU utilization", ex);
                    }
                }

                if (!await WaitForNextCheckAsync(_cpuSignal, cancellationToken))
                {
                    return;
                }
            }
        }

        private async Task RunDebugMonitorAsync(string workFolder, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (TryGetActiveTaskGeneration(requireWarnings: false, out long taskGeneration))
                {
                    try
                    {
                        string diskInfo = GetDiskInfoString(workFolder);
                        string memoryInfo = await GetMemoryInfoStringAsync(cancellationToken);
                        string cpuInfo = await GetCpuInfoStringAsync(cancellationToken);

                        TryWriteDebug(taskGeneration, StringUtil.Loc("ResourceMonitorAgentEnvironmentResource",
                            diskInfo,
                            memoryInfo,
                            cpuInfo));
                    }
                    catch (Exception ex)
                    {
                        TraceMonitoringFailure("collect resource monitoring debug output", ex);
                    }
                }

                if (!await WaitForNextIntervalAsync(cancellationToken))
                {
                    return;
                }
            }
        }

        private CancellationTokenSource CreateMetricsTimeout(CancellationToken cancellationToken)
        {
            var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(MetricsTimeout);
            return timeout;
        }

        private async Task<bool> WaitForNextCheckAsync(SemaphoreSlim signal, CancellationToken cancellationToken)
        {
            try
            {
                await signal.WaitAsync(MonitorInterval, cancellationToken);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception ex)
            {
                TraceMonitoringFailure("wait for the next resource monitoring interval", ex);
                return true;
            }
        }

        private async Task<bool> WaitForNextIntervalAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(MonitorInterval, cancellationToken);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception ex)
            {
                TraceMonitoringFailure("wait for the next resource monitoring interval", ex);
                return true;
            }
        }

        private bool TryGetActiveTaskGeneration(bool requireWarnings, out long generation)
        {
            lock (_taskLock)
            {
                if (_activeTask != null && (!requireWarnings || _activeTask.EnableWarnings))
                {
                    generation = _activeTask.Generation;
                    return true;
                }
            }

            generation = 0;
            return false;
        }

        private void TryWriteWarning(long taskGeneration, ResourceWarning warning, string message)
        {
            lock (_taskLock)
            {
                ActiveTask task = _activeTask;
                if (task?.Generation != taskGeneration || !task.EnableWarnings || task.IsReported(warning))
                {
                    return;
                }

                task.MarkReported(warning);
                try
                {
                    task.Context.Write(WellKnownTags.Warning, message);
                }
                catch (Exception ex)
                {
                    TraceMonitoringFailure("write resource utilization warning to the task log", ex);
                }
            }
        }

        private void TryWriteDebug(long taskGeneration, string message)
        {
            lock (_taskLock)
            {
                if (_activeTask?.Generation != taskGeneration)
                {
                    return;
                }

                try
                {
                    _activeTask.Context.Debug(message);
                }
                catch (Exception ex)
                {
                    TraceMonitoringFailure("write resource monitoring debug output to the task log", ex);
                }
            }
        }

        private void TryPublishCpuTelemetry(long taskGeneration, string message)
        {
            lock (_taskLock)
            {
                ActiveTask task = _activeTask;
                if (task?.Generation != taskGeneration || !task.EnableWarnings || task.IsReported(ResourceWarning.Cpu))
                {
                    return;
                }

                task.MarkReported(ResourceWarning.Cpu);
                try
                {
                    var telemetryData = new Dictionary<string, string>
                    {
                        { "TaskId", task.TaskId.ToString() },
                        { "JobId", task.Context.Variables.System_JobId.ToString() },
                        { "PlanId", task.Context.Variables.Get(Constants.Variables.System.PlanId) },
                        { "Warning", message },
                    };

                    var command = new Command("telemetry", "publish")
                    {
                        Data = JsonConvert.SerializeObject(telemetryData, Formatting.None),
                    };
                    command.Properties.Add("area", "AzurePipelinesAgent");
                    command.Properties.Add("feature", "ResourceUtilization");

                    var publishTelemetryCommand = new TelemetryCommandExtension();
                    publishTelemetryCommand.Initialize(HostContext);
                    publishTelemetryCommand.ProcessCommand(task.Context, command);
                }
                catch (Exception ex)
                {
                    TraceMonitoringFailure("publish resource utilization telemetry", ex);
                }
            }
        }

        private string GetDiskInfoString(string workFolder)
        {
            try
            {
                ResourceDiskInfo diskInfo = MetricsProvider.GetDiskInfo(workFolder);
                return StringUtil.Loc("ResourceMonitorDiskInfo",
                    diskInfo.VolumeRoot,
                    $"{diskInfo.FreeDiskSpaceMB:0.00}",
                    $"{diskInfo.TotalDiskSpaceMB:0.00}");
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
                using CancellationTokenSource timeout = CreateMetricsTimeout(cancellationToken);
                ResourceMemoryInfo memoryInfo = await MetricsProvider.GetMemoryInfoAsync(timeout.Token);
                return StringUtil.Loc("ResourceMonitorMemoryInfo",
                    $"{memoryInfo.UsedMemoryMB:0.00}",
                    $"{memoryInfo.TotalMemoryMB:0.00}");
            }
            catch (Exception ex)
            {
                return StringUtil.Loc("ResourceMonitorMemoryInfoError", ex.Message);
            }
        }

        private async Task<string> GetCpuInfoStringAsync(CancellationToken cancellationToken)
        {
            try
            {
                using CancellationTokenSource timeout = CreateMetricsTimeout(cancellationToken);
                ResourceCpuInfo cpuInfo = await MetricsProvider.GetCpuInfoAsync(timeout.Token);
                return StringUtil.Loc("ResourceMonitorCPUInfo", $"{cpuInfo.Usage:0.00}");
            }
            catch (Exception ex)
            {
                return StringUtil.Loc("ResourceMonitorCPUInfoError", ex.Message);
            }
        }

        private void DetachTask(long generation)
        {
            try
            {
                lock (_taskLock)
                {
                    if (_activeTask?.Generation == generation)
                    {
                        _activeTask = null;
                    }
                }
            }
            catch (Exception ex)
            {
                TraceMonitoringFailure("detach resource monitoring task output", ex);
            }
        }

        private void SignalTaskAttached()
        {
            Signal(_cpuSignal);
            Signal(_diskSignal);
            Signal(_memorySignal);
        }

        private static void Signal(SemaphoreSlim signal)
        {
            try
            {
                if (signal.CurrentCount == 0)
                {
                    signal.Release();
                }
            }
            catch (SemaphoreFullException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private async Task CompleteStopAsync(Task monitorCompletion, CancellationTokenSource monitorCancellation)
        {
            try
            {
                await monitorCompletion;
            }
            catch (Exception ex)
            {
                TraceMonitoringFailure("stop resource monitoring", ex);
            }
            finally
            {
                monitorCancellation.Dispose();
            }
        }

        private void TraceMonitoringFailure(string operation, Exception ex)
        {
            try
            {
                Trace.Warning($"Failed to {operation}. Resource monitoring will continue without affecting job execution. Exception: {ex.Message}");
                Trace.Warning(ex.ToString());
            }
            catch (Exception)
            {
                // Resource monitoring diagnostics must never affect job execution.
            }
        }

        private enum ResourceWarning
        {
            Cpu,
            Disk,
            Memory,
        }

        private sealed class ActiveTask
        {
            private bool _cpuReported;
            private bool _diskReported;
            private bool _memoryReported;

            public ActiveTask(long generation, IExecutionContext context, Guid taskId, bool enableWarnings)
            {
                Generation = generation;
                Context = context;
                TaskId = taskId;
                EnableWarnings = enableWarnings;
            }

            public IExecutionContext Context { get; }
            public bool EnableWarnings { get; }
            public long Generation { get; }
            public Guid TaskId { get; }

            public bool IsReported(ResourceWarning warning)
            {
                return warning switch
                {
                    ResourceWarning.Cpu => _cpuReported,
                    ResourceWarning.Disk => _diskReported,
                    ResourceWarning.Memory => _memoryReported,
                    _ => true,
                };
            }

            public void MarkReported(ResourceWarning warning)
            {
                switch (warning)
                {
                    case ResourceWarning.Cpu:
                        _cpuReported = true;
                        break;
                    case ResourceWarning.Disk:
                        _diskReported = true;
                        break;
                    case ResourceWarning.Memory:
                        _memoryReported = true;
                        break;
                }
            }
        }

        private sealed class TaskScope : IDisposable
        {
            private readonly long _generation;
            private ResourceMetricsManager _manager;

            public TaskScope(ResourceMetricsManager manager, long generation)
            {
                _manager = manager;
                _generation = generation;
            }

            public void Dispose()
            {
                ResourceMetricsManager manager = Interlocked.Exchange(ref _manager, null);
                manager?.DetachTask(_generation);
            }
        }

        private sealed class NoopTaskScope : IDisposable
        {
            public static readonly NoopTaskScope Instance = new NoopTaskScope();

            private NoopTaskScope()
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
