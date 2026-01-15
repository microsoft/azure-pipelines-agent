// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

// using Microsoft.TeamFoundation.DistributedTask.WebApi;
// using System;
// using System.Linq;
// using System.Runtime.InteropServices;
// using System.Threading.Tasks;
// using Xunit;

// namespace Microsoft.VisualStudio.Services.Agent.Tests.L1.Worker
// {
//     [Collection("Worker L1 Tests")]
//     public class NodeSelectionL1TestsLinux : L1TestBase
//     {
//         [Theory]
//         [Trait("Level", "L1")]
//         [Trait("Category", "Worker")]
//         [Trait("SkipOn", "windows")] // Only run on Linux/macOS
//         [InlineData("AGENT_USE_NODE24", "true", "node24")]
//         [InlineData("AGENT_USE_NODE20_1", "true", "node20_1")]  
//         [InlineData("AGENT_USE_NODE16", "true", "node16")]
//         public async Task NodeSelection_EnvironmentKnobs_SelectsCorrectVersion_Linux(string knob, string value, string expectedNodeFolder)
//         {
//             try
//             {
//                 // Arrange
//                 SetupL1();
//                 Environment.SetEnvironmentVariable(knob, value);
                
//                 var message = LoadTemplateMessage();
//                 message.Steps.Clear();
                
//                 // CmdLine task uses Node.js runtime on Linux - test node selection integration
//                 message.Steps.Add(CreateScriptTask("echo 'Testing node selection on Linux'"));

//                 // Act
//                 var results = await RunWorker(message);

//                 // Assert
//                 AssertJobCompleted();
//                 Assert.Equal(TaskResult.Succeeded, results.Result);

//                 var steps = GetSteps();
//                 var taskStep = steps.FirstOrDefault(s => s.Name == "CmdLine");
//                 Assert.NotNull(taskStep);

//                 // On Linux, CmdLine MUST use Node.js - validate node selection logs
//                 var log = GetTimelineLogLines(taskStep);
                
//                 // Should contain log indicating which node version was selected
//                 Assert.True(log.Any(x => x.Contains("Using node path:") && x.Contains(expectedNodeFolder)), 
//                     $"Expected to find node selection log with '{expectedNodeFolder}' on Linux in: {string.Join("\n", log)}");
//             }
//             finally
//             {
//                 TearDown();
//             }
//         }

//         [Fact]
//         [Trait("Level", "L1")]
//         [Trait("Category", "Worker")]
//         [Trait("SkipOn", "windows")] // Only run on Linux/macOS
//         public async Task NodeSelection_DefaultBehavior_UsesNode20_Linux()
//         {
//             try
//             {
//                 // Arrange
//                 SetupL1();
//                 // No special environment variables - test default behavior on Linux
                
//                 var message = LoadTemplateMessage();
//                 message.Steps.Clear();
//                 message.Steps.Add(CreateScriptTask("echo 'Testing default node selection on Linux'"));

//                 // Act
//                 var results = await RunWorker(message);

//                 // Assert
//                 AssertJobCompleted();
//                 Assert.Equal(TaskResult.Succeeded, results.Result);

//                 var steps = GetSteps();
//                 var taskStep = steps.FirstOrDefault(s => s.Name == "CmdLine");
//                 Assert.NotNull(taskStep);

//                 var log = GetTimelineLogLines(taskStep);
                
//                 // On Linux, should ALWAYS have node selection logging
//                 bool hasNodeSelection = log.Any(x => x.Contains("Using node path:"));
//                 Assert.True(hasNodeSelection, "Should have node selection logging on Linux for default behavior");
                
//                 // Default should typically be node20_1 
//                 bool usesDefaultVersion = log.Any(x => x.Contains("Using node path:") && x.Contains("node20"));
//                 Assert.True(usesDefaultVersion, "Default behavior on Linux should use node20 series");
//             }
//             finally
//             {
//                 TearDown();
//             }
//         }

//         [Theory]
//         [Trait("Level", "L1")]
//         [Trait("Category", "Worker")]
//         [Trait("SkipOn", "windows")] // Only run on Linux/macOS
//         [InlineData(true)]
//         [InlineData(false)]
//         public async Task NodeSelection_StrategyVsLegacy_Linux(bool useStrategy)
//         {
//             try
//             {
//                 // Arrange
//                 SetupL1();
//                 Environment.SetEnvironmentVariable("AGENT_USE_NODE_STRATEGY", useStrategy.ToString().ToLower());
//                 Environment.SetEnvironmentVariable("AGENT_USE_NODE24", "true");
                
//                 var message = LoadTemplateMessage();
//                 message.Steps.Clear();
//                 message.Steps.Add(CreateScriptTask("echo 'Testing strategy vs legacy on Linux'"));

//                 // Act
//                 var results = await RunWorker(message);

//                 // Assert
//                 AssertJobCompleted();
//                 Assert.Equal(TaskResult.Succeeded, results.Result);

//                 var steps = GetSteps();
//                 var taskStep = steps.FirstOrDefault(s => s.Name == "CmdLine");
//                 var log = GetTimelineLogLines(taskStep);

//                 // On Linux, MUST have node selection regardless of strategy mode
//                 bool hasNodeSelection = log.Any(x => x.Contains("Using node path:"));
//                 Assert.True(hasNodeSelection, $"Expected node selection log on Linux for {(useStrategy ? "strategy" : "legacy")} mode");

//                 // Should use node24 based on environment variable
//                 bool usesNode24 = log.Any(x => x.Contains("Using node path:") && x.Contains("node24"));
//                 Assert.True(usesNode24, "Should use node24 based on AGENT_USE_NODE24=true on Linux");
//             }
//             finally
//             {
//                 TearDown();
//             }
//         }
//     }
// }