// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using Agent.Sdk;
using Agent.Sdk.Knob;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.Container;

namespace Microsoft.VisualStudio.Services.Agent.Worker.NodeVersionStrategies
{
    /// <summary>
    /// Helper class for testing Node.js availability in containers.
    /// Provides shared functionality across all node version strategies.
    /// </summary>
    public static class NodeContainerTestHelper
    {
        /// <summary>
        /// Tests if a specific Node version can execute in the container by running node --version command.
        /// Uses Docker manager to execute actual commands in the running container.
        /// </summary>
        /// <param name="context">Task context with container information</param>
        /// <param name="executionContext">Execution context for logging and service access</param>
        /// <param name="dockerManager">Docker command manager instance</param>
        /// <param name="nodeVersion">Node version to test</param>
        /// <param name="strategyName">Name of the calling strategy for logging purposes</param>
        /// <returns>True if the node version can execute in the container, false otherwise</returns>
        public static bool CanExecuteNodeInContainer(TaskContext context, IExecutionContext executionContext, IDockerCommandManager dockerManager, NodeVersion nodeVersion, string strategyName)
        {
            var container = context.Container;
            if (string.IsNullOrEmpty(container.ContainerId))
            {
                executionContext.Debug($"[{strategyName}] Container not started yet, cannot test {nodeVersion}");
                return false;
            }

            // Check for incompatible Windows agent + Linux container scenario
            if (PlatformUtil.RunningOnWindows && container.ImageOS == PlatformUtil.OS.Linux)
            {
                executionContext.Debug($"[{strategyName}] Windows agent with Linux container detected - agent's Windows Node.js binaries cannot run in Linux container");
                executionContext.Debug($"[{strategyName}] This scenario requires the container to have its own Node.js installation");
                return false;
            }

            try
            {
                executionContext.Debug($"[{strategyName}] Testing {nodeVersion} availability in container {container.ContainerId}");
                executionContext.Debug($"[{strategyName}] Container ImageOS: {container.ImageOS}");
                
                // Use the provided Docker manager instance
                var hostContext = executionContext.GetHostContext();
                
                // Build the node path for the specified version
                string nodeFolder = NodeVersionHelper.GetFolderName(nodeVersion);
                
                // Build host path (always with Windows .exe since we're on Windows host)
                string hostPath = Path.Combine(hostContext.GetDirectory(WellKnownDirectory.Externals), nodeFolder, "bin", $"node{IOUtil.ExeExtension}");
                
                // Translate to container path
                string containerNodePath = container.TranslateToContainerPath(hostPath);
                
                // Fix path and extension for target container OS
                if (container.ImageOS == PlatformUtil.OS.Linux)
                {
                    // Convert Windows backslashes to Linux forward slashes
                    containerNodePath = containerNodePath.Replace('\\', '/');
                    
                    // Remove .exe extension for Linux containers
                    if (containerNodePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        containerNodePath = containerNodePath.Substring(0, containerNodePath.Length - 4);
                    }
                }

                executionContext.Debug($"[{strategyName}] hostPath: {hostPath}");
                executionContext.Debug($"[{strategyName}] containerNodePath: {containerNodePath}");
                
                // Execute node --version command in the container
                var output = new List<string>();
                
                // Format command following the same pattern as ContainerOperationProvider startup commands
                // Use HOST OS to determine command format, just like the original code
                string testCommand;
                if (PlatformUtil.RunningOnWindows)
                {
                    if (container.ImageOS == PlatformUtil.OS.Windows)
                    {
                        // Windows host + Windows container: use cmd.exe wrapper
                        testCommand = $"cmd.exe /c \"\"{containerNodePath}\" --version\"";
                    }
                    else
                    {
                        // Windows host + Linux container: use bash wrapper (matching original pattern)
                        testCommand = $"bash -c \"{containerNodePath} --version\"";
                    }
                }
                else
                {
                    // Linux/Mac host: use bash wrapper
                    testCommand = $"bash -c \"{containerNodePath} --version\"";
                }
                
                executionContext.Debug($"[{strategyName}] Executing test command: {testCommand}");
                int exitCode = dockerManager.DockerExec(executionContext, container.ContainerId, string.Empty, testCommand, output).Result;
                
                if (exitCode == 0 && output.Count > 0)
                {
                    executionContext.Debug($"[{strategyName}] {nodeVersion} test successful: {output[0]}");
                    return true;
                }
                else
                {
                    executionContext.Debug($"[{strategyName}] {nodeVersion} test failed with exit code {exitCode}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                executionContext.Debug($"[{strategyName}] Exception testing {nodeVersion} in container: {ex.Message}");
                return false;
            }
        }

        // static string GetContainerNodePath(string nodeFolder, ContainerInfo container, IHostContext hostContext)
        // {
        //     // Container execution: use container's OS to determine executable name
        //     string containerExeExtension = container.ImageOS == PlatformUtil.OS.Windows ? ".exe" : "";
        //     string hostPath = Path.Combine(hostContext.GetDirectory(WellKnownDirectory.Externals), nodeFolder, "bin", $"node{IOUtil.ExeExtension}");
        //     string containerNodePath = container.TranslateToContainerPath(hostPath);
        //     executionContext.Debug($"[{strategyName}] hostPath: {hostPath}");
        //     executionContext.Debug($"[{strategyName}] containerNodePath: {containerNodePath}");
        //     // Fix the executable extension for the container OS
        //     // string finalPath = containerNodePath.Replace($"node{IOUtil.ExeExtension}", $"node{containerExeExtension}");
        //     executionContext.Debug($"[{strategyName}] finalPath: {finalPath}");
        //     return finalPath;
        // }
    }
}