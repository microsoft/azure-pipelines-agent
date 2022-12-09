// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker.Maintenance;

namespace Microsoft.VisualStudio.Services.Agent.Worker.Build
{
    /// <summary>
    /// This class provides 2 types of maintenance for the agent workspaces (subfolders of _work).
    /// The first thing is does is mark configurations for garbage collection based on the last time 
    /// they were used. And then it triggers garbage collection.
    /// The second thing it does is to delegate maintenance to each workspace's source provider. This
    /// gives the source provider a chance to do anything special that it needs to do.
    /// </summary>
    public class WorkspaceMaintenanceProvider : AgentService, IMaintenanceServiceProvider
    {
        public string MaintenanceDescription => StringUtil.Loc("DeleteUnusedBuildDir");

        public Type ExtensionType => typeof(IMaintenanceServiceProvider);

        public async Task RunMaintenanceOperation(IExecutionContext executionContext)
        {
            Trace.Entering();
            ArgUtil.NotNull(executionContext, nameof(executionContext));

            // this might be not accurate when the agent is configured for old TFS server
            int totalAvailableTimeInMinutes = executionContext.Variables.GetInt(Constants.Variables.Maintenance.JobTimeout) ?? 60;

            // start a timer to track how much time we used
            Stopwatch totalTimeSpent = Stopwatch.StartNew();

            // Attempt to do the garbage collection
            var trackingManager = HostContext.GetService<ITrackingManager>();
            if (!TryHandleGarbageCollection(executionContext, trackingManager))
            {
                // Something horrible has gone wrong; don't continue
                return;
            }

            var trackingConfigs = GetTrackingConfigsForMaintenance(executionContext, trackingManager);

            // Sort the all tracking file ASC by last maintenance attempted time
            foreach (var trackingConfig in trackingConfigs.OrderBy(x => x.LastMaintenanceAttemptedOn))
            {
                // Check for cancellation.
                executionContext.CancellationToken.ThrowIfCancellationRequested();

                if (ShouldRunMaintenance(executionContext, trackingConfig, totalAvailableTimeInMinutes, totalTimeSpent))
                {
                    await RunSourceProviderMaintenance(executionContext, trackingManager, trackingConfig);
                }
            }
        }

        private static List<TrackingConfig> GetTrackingConfigsForMaintenance(IExecutionContext executionContext, ITrackingManager trackingManager)
        {
            var trackingConfigs = new List<TrackingConfig>();
            foreach (var config in trackingManager.EnumerateAllTrackingConfigs(executionContext))
            {
                executionContext.Output(StringUtil.Loc("EvaluateTrackingFile", config.FileLocation));
                if (string.IsNullOrEmpty(config.RepositoryType))
                {
                    // repository not been set.
                    executionContext.Output(StringUtil.Loc("SkipTrackingFileWithoutRepoType", config.FileLocation));
                }
                else
                {
                    trackingConfigs.Add(config);
                }
            }

            return trackingConfigs;
        }

        private bool TryHandleGarbageCollection(IExecutionContext executionContext, ITrackingManager trackingManager)
        {
            int staleBuildDirThreshold = executionContext.Variables.GetInt(Constants.Variables.Maintenance.DeleteWorkingDirectoryDaysThreshold) ?? 0;
            if (staleBuildDirThreshold > 0)
            {
                // scan unused build directories
                executionContext.Output(StringUtil.Loc("DiscoverBuildDir", staleBuildDirThreshold));
                trackingManager.MarkExpiredForGarbageCollection(executionContext, TimeSpan.FromDays(staleBuildDirThreshold));

                executionContext.Output(StringUtil.Loc("GCBuildDir"));

                // delete unused build directories
                trackingManager.DisposeCollectedGarbage(executionContext);
            }
            else
            {
                executionContext.Output(StringUtil.Loc("GCBuildDirNotEnabled"));
            }

            // give source provider a chance to run maintenance operation
            Trace.Info("Scan all SourceFolder tracking files.");
            string searchRoot = Path.Combine(HostContext.GetDirectory(WellKnownDirectory.Work), Constants.Build.Path.SourceRootMappingDirectory);
            if (!Directory.Exists(searchRoot))
            {
                executionContext.Output(StringUtil.Loc("GCDirNotExist", searchRoot));
                return false;
            }

            return true;
        }

        private static bool ShouldRunMaintenance(IExecutionContext executionContext,  TrackingConfig trackingConfig, int totalAvailableTimeInMinutes, Stopwatch totalTimeSpent)
        {
            bool runMainenance = false;

            if (trackingConfig.LastMaintenanceAttemptedOn == null)
            {
                // this folder never run maintenance before, we will do maintenance if there is more than half of the time remains.
                if (totalTimeSpent.Elapsed.TotalMinutes < totalAvailableTimeInMinutes / 2)  // 50% time left
                {
                    runMainenance = true;
                }
                else
                {
                    executionContext.Output($"Working directory '{trackingConfig.BuildDirectory}' has never run maintenance before. Skip since we may not have enough time.");
                }
            }
            else if (trackingConfig.LastMaintenanceCompletedOn == null)
            {
                // this folder did finish maintenance last time, this might indicate we need more time for this working directory
                if (totalTimeSpent.Elapsed.TotalMinutes < totalAvailableTimeInMinutes / 4)  // 75% time left
                {
                    runMainenance = true;
                }
                else
                {
                    executionContext.Output($"Working directory '{trackingConfig.BuildDirectory}' didn't finish maintenance last time. Skip since we may not have enough time.");
                }
            }
            else
            {
                // estimate time for running maintenance
                TimeSpan estimateTime = trackingConfig.LastMaintenanceCompletedOn.Value - trackingConfig.LastMaintenanceAttemptedOn.Value;

                // there is more than 10 mins left after we run maintenance on this repository directory
                if (totalAvailableTimeInMinutes > totalTimeSpent.Elapsed.TotalMinutes + estimateTime.TotalMinutes + 10)
                {
                    runMainenance = true;
                }
                else
                {
                    executionContext.Output($"Working directory '{trackingConfig.BuildDirectory}' may take about '{estimateTime.TotalMinutes}' mins to finish maintenance. It's too risky since we only have '{totalAvailableTimeInMinutes - totalTimeSpent.Elapsed.TotalMinutes}' mins left for maintenance.");
                }
            }

            return runMainenance;
        }

        private async Task RunSourceProviderMaintenance(IExecutionContext executionContext, ITrackingManager trackingManager, TrackingConfig trackingConfig)
        {
            var extensionManager = HostContext.GetService<IExtensionManager>();
            ISourceProvider sourceProvider = extensionManager.GetExtensions<ISourceProvider>().FirstOrDefault(x => string.Equals(x.RepositoryType, trackingConfig.RepositoryType, StringComparison.OrdinalIgnoreCase));
            if (sourceProvider != null)
            {
                try
                {
                    trackingManager.MaintenanceStarted(trackingConfig);
                    string repositoryPath = Path.Combine(HostContext.GetDirectory(WellKnownDirectory.Work), trackingConfig.SourcesDirectory);
                    await sourceProvider.RunMaintenanceOperations(executionContext, repositoryPath);
                    trackingManager.MaintenanceCompleted(trackingConfig);
                }
                catch (Exception ex)
                {
                    executionContext.Error(StringUtil.Loc("ErrorDuringBuildGC", trackingConfig.FileLocation));
                    executionContext.Error(ex);
                }
            }
        }
    }
}
