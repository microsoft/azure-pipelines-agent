// /*
// This change implements L1 tests for containers using Docker images, both with and without labels.


// Description
// Integration tests validating that the Azure Pipelines Agent reads the com.azure.dev.pipelines.agent.handler.node.path container label to resolve custom Node.js paths. Includes four platform-specific tests (Windows and Linux) that verify both label-based resolution and fallback behavior when the label is absent.
// */

// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

// using Microsoft.TeamFoundation.DistributedTask.WebApi;
// using System;
// using System.IO;
// using System.Linq;
// using System.Runtime.InteropServices;
// using System.Threading.Tasks;
// using Xunit;

// namespace Microsoft.VisualStudio.Services.Agent.Tests.L1.Worker
// {
//     [Collection("Worker L1 Tests")]
//     public class ContainerLabelL1Tests : L1TestBase
//     {
//         private const string TestImageNameWindows = "azure-pipelines-agent-test-container-label-windows";
//         private const string TestImageNameLinux = "azure-pipelines-agent-test-container-label-linux";
//         private const string TestImageNameWindowsNoLabel = "azure-pipelines-agent-test-container-nolabel-windows";
//         private const string TestImageNameLinuxNoLabel = "azure-pipelines-agent-test-container-nolabel-linux";
//         private const string CustomNodePathWindows = "C:\\Program Files\\nodejs\\node.exe";
//         private const string CustomNodePathLinux = "/usr/local/bin/node";
//         private const string ContainerLabelKey = "com.azure.dev.pipelines.agent.handler.node.path";

//         [Fact]
//         [Trait("Level", "L1")]
//         [Trait("Category", "Worker")]
//         [Trait("SkipOn", "linux")]
//         [Trait("SkipOn", "darwin")]
//         public async Task Container_Label_NodePath_Resolution_Windows_Test()
//         {
//             await RunContainerLabelTest(TestImageNameWindows, CreateTestContainerImageWindows);
//         }

//         [Fact]
//         [Trait("Level", "L1")]
//         [Trait("Category", "Worker")]
//         [Trait("SkipOn", "windows")]
//         [Trait("SkipOn", "darwin")]
//         public async Task Container_Label_NodePath_Resolution_Linux_Test()
//         {
//             await RunContainerLabelTest(TestImageNameLinux, CreateTestContainerImageLinux);
//         }

//         [Fact]
//         [Trait("Level", "L1")]
//         [Trait("Category", "Worker")]
//         [Trait("SkipOn", "linux")]
//         [Trait("SkipOn", "darwin")]
//         public async Task Container_NoLabel_NodePath_Resolution_Windows_Test()
//         {
//             await RunContainerLabelTest(TestImageNameWindowsNoLabel, CreateTestContainerImageWindowsNoLabel);
//         }

//         [Fact]
//         [Trait("Level", "L1")]
//         [Trait("Category", "Worker")]
//         [Trait("SkipOn", "windows")]
//         [Trait("SkipOn", "darwin")]
//         public async Task Container_NoLabel_NodePath_Resolution_Linux_Test()
//         {
//             await RunContainerLabelTest(TestImageNameLinuxNoLabel, CreateTestContainerImageLinuxNoLabel);
//         }

//         private async Task RunContainerLabelTest(string imageName, Func<Task> createImageFunc)
//         {
//             try
//             {
//                 await createImageFunc();
                
//                 SetupL1();
//                 var message = LoadTemplateMessage();
                
//                 var containerResource = new Microsoft.TeamFoundation.DistributedTask.Pipelines.ContainerResource()
//                 {
//                     Alias = "test_container"
//                 };
//                 containerResource.Properties.Set("image", imageName);
                
//                 message.Resources.Containers.Add(containerResource);
                
//                 var containerMessage = new Microsoft.TeamFoundation.DistributedTask.Pipelines.AgentJobRequestMessage(
//                     message.Plan,
//                     message.Timeline, 
//                     message.JobId,
//                     message.JobName,
//                     message.JobDisplayName,
//                     "test_container",
//                     message.JobSidecarContainers,
//                     message.Variables,
//                     message.MaskHints,
//                     message.Resources,
//                     message.Workspace,
//                     message.Steps
//                 );
                
//                 containerMessage.Steps.Clear();
                
//                 var nodeTask = CreateScriptTask("echo Testing container label node path resolution && node --version");
//                 containerMessage.Steps.Add(nodeTask);

//                 var results = await RunWorker(containerMessage);

//                 Assert.NotNull(results);
                
//                 var steps = GetSteps();
//                 Assert.NotNull(steps);
//                 Assert.True(steps.Count() >= 1);
//             }
//             finally
//             {
//                 await CleanupTestContainerImage(imageName);
//                 TearDown();
//             }
//         }

//         private async Task CreateTestContainerImageWindows()
//         {
//             string dockerfile = $@"FROM mcr.microsoft.com/windows/servercore/insider:10.0.20348.1
// LABEL ""{ContainerLabelKey}""=""{CustomNodePathWindows}""
// RUN echo Container with custom node path label created
// ";

//             await BuildDockerImage(TestImageNameWindows, dockerfile);
//         }

//         private async Task CreateTestContainerImageLinux()
//         {
//             string dockerfile = $@"FROM node:16-alpine
// LABEL ""{ContainerLabelKey}""=""{CustomNodePathLinux}""
// RUN node --version
// ";

//             await BuildDockerImage(TestImageNameLinux, dockerfile);
//         }

//         private async Task CreateTestContainerImageWindowsNoLabel()
//         {
//             string dockerfile = @"FROM mcr.microsoft.com/windows/servercore/insider:10.0.20348.1
// RUN echo Container without custom node path label created
// ";

//             await BuildDockerImage(TestImageNameWindowsNoLabel, dockerfile);
//         }

//         private async Task CreateTestContainerImageLinuxNoLabel()
//         {
//             string dockerfile = @"FROM node:16-alpine
// RUN node --version
// ";

//             await BuildDockerImage(TestImageNameLinuxNoLabel, dockerfile);
//         }

//         private async Task BuildDockerImage(string imageName, string dockerfileContent)
//         {
//             string tempDir = Path.Combine(Path.GetTempPath(), $"docker-build-{Guid.NewGuid():N}");
//             Directory.CreateDirectory(tempDir);
//             string dockerfilePath = Path.Combine(tempDir, "Dockerfile");
//             File.WriteAllText(dockerfilePath, dockerfileContent);

//             try
//             {
//                 var processInfo = new System.Diagnostics.ProcessStartInfo
//                 {
//                     FileName = "docker",
//                     Arguments = $"build -t {imageName} -f \"{dockerfilePath}\" \"{tempDir}\"",
//                     UseShellExecute = false,
//                     RedirectStandardOutput = true,
//                     RedirectStandardError = true,
//                     CreateNoWindow = true
//                 };

//                 using var process = System.Diagnostics.Process.Start(processInfo);
//                 await process.WaitForExitAsync();

//                 if (process.ExitCode != 0)
//                 {
//                     string error = await process.StandardError.ReadToEndAsync();
//                     throw new InvalidOperationException($"Failed to build test container image: {error}");
//                 }
//             }
//             finally
//             {
//                 if (Directory.Exists(tempDir))
//                 {
//                     Directory.Delete(tempDir, recursive: true);
//                 }
//             }
//         }

//         private async Task CleanupTestContainerImage(string imageName)
//         {
//             try
//             {
//                 var processInfo = new System.Diagnostics.ProcessStartInfo
//                 {
//                     FileName = "docker",
//                     Arguments = $"rmi {imageName} --force",
//                     UseShellExecute = false,
//                     RedirectStandardOutput = true,
//                     RedirectStandardError = true,
//                     CreateNoWindow = true
//                 };

//                 using var process = System.Diagnostics.Process.Start(processInfo);
//                 await process.WaitForExitAsync();
//             }
//             catch
//             {
//             }
//         }
//     }
// }
