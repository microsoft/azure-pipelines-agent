// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.DistributedTask.WebApi;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;

namespace Microsoft.VisualStudio.Services.Agent.Tests.L1.Worker
{
    [Collection("Worker L1 Tests")]
    public class ContainerLabelL1Tests : L1TestBase
    {
        private const string TestImageNameWindows = "azure-pipelines-agent-test-container-label-windows";
        private const string TestImageNameLinux = "azure-pipelines-agent-test-container-label-linux";
        private const string TestImageNameWindowsNoLabel = "azure-pipelines-agent-test-container-nolabel-windows";
        private const string TestImageNameLinuxNoLabel = "azure-pipelines-agent-test-container-nolabel-linux";
        private const string CustomNodePathWindows = "C:\\Program Files\\nodejs\\node.exe";
        private const string CustomNodePathLinux = "/usr/local/bin/node";
        private const string AgentBundledNodePath = "externals";
        private const string ContainerLabelKey = "com.azure.dev.pipelines.agent.handler.node.path";

        [Fact]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "linux")]
        [Trait("SkipOn", "darwin")]
        public async Task Container_Label_NodePath_Resolution_Windows_Test()
        {
            await RunContainerLabelTest(TestImageNameWindows, CreateTestContainerImageWindows, CustomNodePathWindows);
        }

        [Fact]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "windows")]
        [Trait("SkipOn", "darwin")]
        public async Task Container_Label_NodePath_Resolution_Linux_Test()
        {
            await RunContainerLabelTest(TestImageNameLinux, CreateTestContainerImageLinux, CustomNodePathLinux);
        }

        [Fact]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "linux")]
        [Trait("SkipOn", "darwin")]
        public async Task Container_NoLabel_NodePath_Resolution_Windows_Test()
        {
            // When no label, agent uses bundled node from externals directory
            await RunContainerLabelTest(TestImageNameWindowsNoLabel, CreateTestContainerImageWindowsNoLabel, AgentBundledNodePath);
        }

        [Fact]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "windows")]
        [Trait("SkipOn", "darwin")]
        public async Task Container_NoLabel_NodePath_Resolution_Linux_Test()
        {
            // When no label, agent uses bundled node from externals directory
            await RunContainerLabelTest(TestImageNameLinuxNoLabel, CreateTestContainerImageLinuxNoLabel, AgentBundledNodePath);
        }

        private async Task RunContainerLabelTest(string imageName, Func<Task> createImageFunc, string expectedNodePath)
        {
            try
            {
                await createImageFunc();

                SetupL1();
                var message = LoadTemplateMessage();

                var containerResource = new Pipelines.ContainerResource()
                {
                    Alias = "test_container"
                };
                containerResource.Properties.Set("image", imageName);

                message.Resources.Containers.Add(containerResource);

                var containerMessage = new Pipelines.AgentJobRequestMessage(
                    message.Plan,
                    message.Timeline,
                    message.JobId,
                    message.JobName,
                    message.JobDisplayName,
                    "test_container",
                    message.JobSidecarContainers,
                    message.Variables,
                    message.MaskHints,
                    message.Resources,
                    message.Workspace,
                    message.Steps
                );

                containerMessage.Steps.Clear();

                var nodeTask = CreateScriptTask("echo Testing container label node path resolution && node --version");
                containerMessage.Steps.Add(nodeTask);

                var results = await RunWorker(containerMessage);

                Assert.NotNull(results);
                AssertJobCompleted();
                Assert.Equal(TaskResult.Succeeded, results.Result);

                var steps = GetSteps();
                Assert.NotNull(steps);
                Assert.True(steps.Count() >= 1, "Expected at least one step to execute");

                // Verify node path appears in logs
                ValidateNodePathUsage(steps, expectedNodePath);
            }
            finally
            {
                await CleanupTestContainerImage(imageName);
                TearDown();
            }
        }

        private void ValidateNodePathUsage(IList<TimelineRecord> steps, string expectedPath)
        {
            var allLogs = steps
                .Where(step => step.Log != null)
                .SelectMany(step => GetTimelineLogLines(step))
                .ToList();

            string combinedLogs = string.Join(Environment.NewLine, allLogs);

            Assert.Contains(expectedPath, combinedLogs, StringComparison.OrdinalIgnoreCase);
        }

        private async Task CreateTestContainerImageWindows()
        {
            string dockerfile = $@"FROM mcr.microsoft.com/windows/servercore/insider:10.0.20348.1
            LABEL ""{ContainerLabelKey}""=""{CustomNodePathWindows}""
            RUN echo Container with custom node path label created
            ";

            await BuildDockerImage(TestImageNameWindows, dockerfile);
        }

        private async Task CreateTestContainerImageLinux()
        {
            string dockerfile = $@"FROM node:16-alpine
            LABEL ""{ContainerLabelKey}""=""{CustomNodePathLinux}""
            RUN node --version
            ";

            await BuildDockerImage(TestImageNameLinux, dockerfile);
        }

        private async Task CreateTestContainerImageWindowsNoLabel()
        {
            string dockerfile = @"FROM mcr.microsoft.com/windows/servercore/insider:10.0.20348.1
            RUN echo Container without custom node path label created
            ";

            await BuildDockerImage(TestImageNameWindowsNoLabel, dockerfile);
        }

        private async Task CreateTestContainerImageLinuxNoLabel()
        {
            string dockerfile = @"FROM node:16-alpine
            RUN node --version
            ";

            await BuildDockerImage(TestImageNameLinuxNoLabel, dockerfile);
        }

        private async Task BuildDockerImage(string imageName, string dockerfileContent)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"docker-build-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            string dockerfilePath = Path.Combine(tempDir, "Dockerfile");
            File.WriteAllText(dockerfilePath, dockerfileContent);

            try
            {
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = $"build -t {imageName} -f \"{dockerfilePath}\" \"{tempDir}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(processInfo);
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    string error = await process.StandardError.ReadToEndAsync();
                    throw new InvalidOperationException($"Failed to build test container image: {error}");
                }
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }

        private async Task CleanupTestContainerImage(string imageName)
        {
            try
            {
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = $"rmi {imageName} --force",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(processInfo);
                await process.WaitForExitAsync();
            }
            catch
            {
            }
        }
    }
}
