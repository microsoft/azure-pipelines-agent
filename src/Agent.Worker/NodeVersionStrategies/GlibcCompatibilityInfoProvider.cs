// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using Agent.Sdk;
using Agent.Sdk.Knob;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Microsoft.VisualStudio.Services.Agent.Worker.NodeVersionStrategies
{
    /// <summary>
    /// Utility class for checking glibc compatibility with Node.js versions on Linux systems.
    /// </summary>
    public class GlibcCompatibilityInfoProvider : AgentService, IGlibcCompatibilityInfoProvider
    {
        private readonly IExecutionContext _executionContext;
        private readonly IHostContext _hostContext;        
        private static bool? _supportsNode20;
        private static bool? _supportsNode24;

        public GlibcCompatibilityInfoProvider(IExecutionContext executionContext, IHostContext hostContext)
        {
            ArgUtil.NotNull(executionContext, nameof(executionContext));
            ArgUtil.NotNull(hostContext, nameof(hostContext));
            _executionContext = executionContext;
            _hostContext = hostContext;
        }

        /// <summary>
        /// Checks glibc compatibility for both Node20 and Node24.
        /// This method combines the behavior from NodeHandler for both Node versions.
        /// </summary>
        /// <returns>GlibcCompatibilityInfo containing compatibility results for both Node versions</returns>
        public virtual async Task<GlibcCompatibilityInfo> CheckGlibcCompatibilityAsync()
        {
            bool useNode20InUnsupportedSystem = AgentKnobs.UseNode20InUnsupportedSystem.GetValue(_executionContext).AsBoolean();
            bool useNode24InUnsupportedSystem = AgentKnobs.UseNode24InUnsupportedSystem.GetValue(_executionContext).AsBoolean();

            bool node20HasGlibcError = false;
            bool node24HasGlibcError = false;

            // Only perform glibc compatibility checks on Linux systems
            if (!IsLinuxPlatform())
            {
                // Non-Linux systems (Windows, macOS) don't have glibc compatibility issues
                return GlibcCompatibilityInfo.Create(node24HasGlibcError: false, node20HasGlibcError: false);
            }

            if (!useNode20InUnsupportedSystem)
            {
                if (_supportsNode20.HasValue)
                {
                    node20HasGlibcError = !_supportsNode20.Value;
                }
                else
                {
                    node20HasGlibcError = await CheckIfNodeResultsInGlibCErrorAsync("node20_1");
                    _executionContext.EmitHostNode20FallbackTelemetry(node20HasGlibcError);
                    _supportsNode20 = !node20HasGlibcError;
                }
            }

            if (!useNode24InUnsupportedSystem)
            {
                if (_supportsNode24.HasValue)
                {
                    node24HasGlibcError = !_supportsNode24.Value;
                }
                else
                {
                    node24HasGlibcError = await CheckIfNodeResultsInGlibCErrorAsync("node24");
                    _executionContext.EmitHostNode24FallbackTelemetry(node24HasGlibcError);
                    _supportsNode24 = !node24HasGlibcError;
                }
            }

            return GlibcCompatibilityInfo.Create(node24HasGlibcError, node20HasGlibcError);
        }

        /// <summary>
        /// Gets glibc compatibility information based on the execution context (host vs container).
        /// </summary>
        /// <param name="context">The task context containing container and handler information</param>
        /// <returns>Glibc compatibility information for the current execution environment</returns>
        public virtual async Task<GlibcCompatibilityInfo> GetGlibcCompatibilityAsync(TaskContext context)
        {
            ArgUtil.NotNull(context, nameof(context));

            string environmentType = context.Container != null ? "Container" : "Host";

            if (context.Container == null)
            {
                // Host execution - check actual glibc compatibility
                var glibcInfo = await CheckGlibcCompatibilityAsync();
                
                _executionContext.Debug($"[{environmentType}] Host glibc compatibility - Node24: {!glibcInfo.Node24HasGlibcError}, Node20: {!glibcInfo.Node20HasGlibcError}");
                
                return glibcInfo;
            }
            else
            {
                // Container execution - use container-specific redirect information
                var glibcInfo = GlibcCompatibilityInfo.Create(
                    node24HasGlibcError: context.Container.NeedsNode20Redirect, 
                    node20HasGlibcError: context.Container.NeedsNode16Redirect);
                
                _executionContext.Debug($"[{environmentType}] Container glibc compatibility - Node24: {!glibcInfo.Node24HasGlibcError}, Node20: {!glibcInfo.Node20HasGlibcError}");
                
                return glibcInfo;
            }
        }

        /// <summary>
        /// Checks if the specified Node.js version results in glibc compatibility errors.
        /// </summary>
        /// <param name="nodeFolder">The node folder name (e.g., "node20_1", "node24")</param>
        /// <returns>True if glibc error is detected, false otherwise</returns>
        public virtual async Task<bool> CheckIfNodeResultsInGlibCErrorAsync(string nodeFolder)
        {
            var nodePath = Path.Combine(_hostContext.GetDirectory(WellKnownDirectory.Externals), nodeFolder, "bin", $"node{IOUtil.ExeExtension}");
            List<string> nodeVersionOutput = await ExecuteCommandAsync(_executionContext, nodePath, "-v", requireZeroExitCode: false, showOutputOnFailureOnly: true);
            var nodeResultsInGlibCError = WorkerUtilities.IsCommandResultGlibcError(_executionContext, nodeVersionOutput, out string nodeInfoLine);

            return nodeResultsInGlibCError;
        }

        /// <summary>
        /// Determines if the current platform is Linux. Virtual for testing override.
        /// </summary>
        /// <returns>True if running on Linux, false otherwise</returns>
        protected virtual bool IsLinuxPlatform()
        {
            return PlatformUtil.HostOS == PlatformUtil.OS.Linux;
        }

        private async Task<List<string>> ExecuteCommandAsync(IExecutionContext context, string command, string arg, bool requireZeroExitCode, bool showOutputOnFailureOnly)
        {
            string commandLog = $"{command} {arg}";
            if (!showOutputOnFailureOnly)
            {
                context.Command(commandLog);
            }

            List<string> outputs = new List<string>();
            object outputLock = new object();
            var processInvoker = _hostContext.CreateService<IProcessInvoker>();
            processInvoker.OutputDataReceived += delegate (object sender, ProcessDataReceivedEventArgs message)
            {
                if (!string.IsNullOrEmpty(message.Data))
                {
                    lock (outputLock)
                    {
                        outputs.Add(message.Data);
                    }
                }
            };

            processInvoker.ErrorDataReceived += delegate (object sender, ProcessDataReceivedEventArgs message)
            {
                if (!string.IsNullOrEmpty(message.Data))
                {
                    lock (outputLock)
                    {
                        outputs.Add(message.Data);
                    }
                }
            };

            var exitCode = await processInvoker.ExecuteAsync(
                            workingDirectory: _hostContext.GetDirectory(WellKnownDirectory.Work),
                            fileName: command,
                            arguments: arg,
                            environment: null,
                            requireExitCodeZero: requireZeroExitCode,
                            outputEncoding: null,
                            cancellationToken: System.Threading.CancellationToken.None);

            if (!showOutputOnFailureOnly || exitCode != 0)
            {
                if (showOutputOnFailureOnly)
                {
                    context.Command(commandLog);
                }

                foreach (var outputLine in outputs)
                {
                    context.Debug(outputLine);
                }
            }

            return outputs;
        }
    }
}