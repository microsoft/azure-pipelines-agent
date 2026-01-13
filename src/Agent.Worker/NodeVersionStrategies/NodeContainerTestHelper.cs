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
    /// </summary>
    public static class NodeContainerTestHelper
    {
        /// <summary>
        /// Tests if a specific Node version can execute in the container.
        /// Cross-platform scenarios are handled earlier in the orchestrator.
        /// </summary>
        public static bool CanExecuteNodeInContainer(TaskContext context, IExecutionContext executionContext, IDockerCommandManager dockerManager, NodeVersion nodeVersion, string strategyName)
        {
            var container = context.Container;
            ArgUtil.NotNull(container, nameof(container));
            ArgUtil.NotNull(container.ContainerId, nameof(container.ContainerId));
            
            try
            {
                executionContext.Debug($"[{strategyName}] Testing {nodeVersion} availability in container {container.ContainerId}");
                
                var hostContext = executionContext.GetHostContext();
                string nodeFolder = NodeVersionHelper.GetFolderName(nodeVersion);
                string hostPath = Path.Combine(hostContext.GetDirectory(WellKnownDirectory.Externals), nodeFolder, "bin", $"node{IOUtil.ExeExtension}");                
                string containerNodePath = container.TranslateToContainerPath(hostPath);
                
                // Fix path and extension for target container OS
                if (container.ImageOS == PlatformUtil.OS.Linux)
                {
                    containerNodePath = containerNodePath.Replace('\\', '/');
                    if (containerNodePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        containerNodePath = containerNodePath.Substring(0, containerNodePath.Length - 4);
                    }
                }
                executionContext.Debug($"[{strategyName}] Testing path: {containerNodePath}");
                
                bool result = ExecuteNodeTestCommand(context, executionContext, dockerManager, containerNodePath, strategyName, $"agent {nodeVersion} binaries");
                return result;
            }
            catch (Exception ex)
            {
                executionContext.Debug($"[{strategyName}] Exception testing {nodeVersion}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Executes the node --version command in the container to test Node.js availability.
        /// </summary>
        private static bool ExecuteNodeTestCommand(TaskContext context, IExecutionContext executionContext, IDockerCommandManager dockerManager, string nodePath, string strategyName, string nodeDescription)
        {
            var container = context.Container;
            
            try
            {
                var output = new List<string>();
                
                // Format command following the same pattern as ContainerOperationProvider startup commands
                string testCommand;
                if (PlatformUtil.RunningOnWindows)
                {
                    if (container.ImageOS == PlatformUtil.OS.Windows)
                    {
                        testCommand = $"cmd.exe /c \"\"{nodePath}\" --version\"";
                    }
                    else
                    {
                        testCommand = $"bash -c \"{nodePath} --version\"";
                    }
                }
                else
                {
                    testCommand = $"bash -c \"{nodePath} --version\"";
                }
                
                executionContext.Debug($"[{strategyName}] Testing {nodeDescription} with command: {testCommand}");
                int exitCode = dockerManager.DockerExec(executionContext, container.ContainerId, string.Empty, testCommand, output).Result;
                
                if (exitCode == 0 && output.Count > 0)
                {
                    executionContext.Debug($"[{strategyName}] {nodeDescription} test successful: {output[0]}");
                    return true;
                }
                else
                {
                    executionContext.Debug($"[{strategyName}] {nodeDescription} test failed with exit code {exitCode}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                executionContext.Debug($"[{strategyName}] Exception testing {nodeDescription}: {ex.Message}");
                return false;
            }
        }
    }
}