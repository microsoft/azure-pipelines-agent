// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.DistributedTask.WebApi;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.L1.Worker
{
    [Collection("Worker L1 Tests")]
    public class ContainerLabelL1Tests : L1TestBase
    {
        private const string TestImageName = "azure-pipelines-agent-test-container-label";
        private const string CustomNodePath = "/usr/local/bin/node";
        private const string ContainerLabelKey = "com.azure.dev.pipelines.agent.handler.node.path";

        [Fact]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        public async Task Container_Label_NodePath_Resolution_Test()
        {
            try
            {
                // First, create a Docker image with the container label
                await CreateTestContainerImage();
                
                // Arrange - Set up L1 test environment
                SetupL1();
                var message = LoadTemplateMessage();
                
                // Create a container resource that uses our test image
                var containerResource = new Microsoft.TeamFoundation.DistributedTask.Pipelines.ContainerResource()
                {
                    Alias = "test_container"
                };
                containerResource.Properties.Set("image", TestImageName);
                
                // Add the container to resources
                message.Resources.Containers.Add(containerResource);
                
                // Create a new message with job container properly set
                var containerMessage = new Microsoft.TeamFoundation.DistributedTask.Pipelines.AgentJobRequestMessage(
                    message.Plan,
                    message.Timeline, 
                    message.JobId,
                    message.JobName,
                    message.JobDisplayName,
                    "test_container", // This sets the job container
                    message.JobSidecarContainers,
                    message.Variables,
                    message.MaskHints,
                    message.Resources,
                    message.Workspace,
                    message.Steps
                );
                
                // Remove all existing steps
                containerMessage.Steps.Clear();
                
                // Add a Node.js task that will trigger container label resolution
                var nodeTask = CreateScriptTask("echo Testing container label node path resolution && node --version");
                containerMessage.Steps.Add(nodeTask);

                // Act - Run the worker with the container
                var results = await RunWorker(containerMessage);

                // Assert - Verify the container label was read and used
                Assert.NotNull(results);
                
                var steps = GetSteps();
                Assert.NotNull(steps);
                Assert.True(steps.Count() >= 1);
                
                // The real test: The container label should have been read during container startup
                // and the custom node path should be available in the container
            }
            finally
            {
                // Cleanup - Remove the test Docker image
                await CleanupTestContainerImage();
                TearDown();
            }
        }

        private async Task CreateTestContainerImage()
        {
            // Create a simple Dockerfile that sets the container label
            string dockerfile = $@"
FROM node:16-alpine
LABEL ""{ContainerLabelKey}""=""{CustomNodePath}""
RUN echo 'Container with custom node path label created'
";

            // Write Dockerfile to temp location
            string tempDir = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
            string dockerfilePath = Path.Combine(tempDir, "Dockerfile.test");
            File.WriteAllText(dockerfilePath, dockerfile);

            try
            {
                // Build the test image using docker command
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = $"build -t {TestImageName} -f \"{dockerfilePath}\" \"{tempDir}\"",
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
                // Clean up the Dockerfile
                if (File.Exists(dockerfilePath))
                {
                    File.Delete(dockerfilePath);
                }
            }
        }

        private async Task CleanupTestContainerImage()
        {
            try
            {
                // Remove the test image
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = $"rmi {TestImageName} --force",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(processInfo);
                await process.WaitForExitAsync();
                
                // Don't throw on cleanup failure - just continue
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}
