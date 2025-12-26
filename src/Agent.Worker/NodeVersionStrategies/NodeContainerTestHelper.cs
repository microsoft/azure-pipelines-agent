// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
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

            try
            {
                executionContext.Debug($"[{strategyName}] Testing {nodeVersion} availability in container {container.ContainerId}");
                
                // Use the provided Docker manager instance
                var hostContext = executionContext.GetHostContext();
                
                // Build the node path for the specified version
                string nodeFolder = NodeVersionHelper.GetFolderName(nodeVersion);
                string externalsPath = hostContext.GetDirectory(WellKnownDirectory.Externals);
                string hostNodePath = Path.Combine(externalsPath, nodeFolder, "bin", $"node{IOUtil.ExeExtension}");
                string containerNodePath = container.TranslateToContainerPath(hostNodePath);
                
                // Execute node --version command in the container
                var output = new List<string>();
                string testCommand = $"'{containerNodePath}' --version";
                
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
    }
}