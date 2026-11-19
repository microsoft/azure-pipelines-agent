// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.DistributedTask.Pipelines;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.L1.Worker
{
    [Collection("Worker L1 Tests")]
    public sealed class Node6WarningL1Tests : L1TestBase
    {
        [Theory]
        [InlineData(false, false, 24)]
        [InlineData(false, true, 24)]
        [InlineData(false, false, 20)]
        [InlineData(false, true, 20)]
        [InlineData(true, false, 0)]
        [InlineData(true, true, 0)]
        [InlineData(true, false, 24)]
        [InlineData(true, true, 24)]
        [Trait("Level", "L1")]
        [Trait("Category", "Worker")]
        public async Task Node6Handler_WarnsWhenRedirected(bool eolPolicy, bool enhancedSelection, int forcedNodeVersion)
        {
            string taskZip = null;
            try
            {
                SetupL1();
                Guid taskId = Guid.NewGuid();
                string externalsDirectory = _l1HostContext.GetDirectory(WellKnownDirectory.Externals);
                string node24Path = Path.Combine(externalsDirectory, "node24", "bin", $"node{IOUtil.ExeExtension}");
                bool expectMissingLegacyRunner = enhancedSelection && !eolPolicy &&
                    forcedNodeVersion == 24 && !File.Exists(node24Path);
                if (expectMissingLegacyRunner)
                {
                    Assert.False(File.Exists(Path.Combine(externalsDirectory, "node", "bin", $"node{IOUtil.ExeExtension}")),
                        "This scenario requires a layout without a manually installed Node 6 runner.");
                }
                bool expectEolFailure = eolPolicy && !File.Exists(node24Path);
                int expectedNodeVersion = forcedNodeVersion == 20 || !File.Exists(node24Path) ? 20 : 24;
                if (expectedNodeVersion == 20 && !expectEolFailure)
                {
                    string node20Path = Path.Combine(externalsDirectory, "node20_1", "bin", $"node{IOUtil.ExeExtension}");
                    Assert.True(File.Exists(node20Path), "The Node 20 runtime is required for this test.");
                }

                string taskDirectory = Path.Combine(externalsDirectory, "Tasks");
                Directory.CreateDirectory(taskDirectory);
                string path = Path.Combine(taskDirectory, $"{taskId}.zip");
                using (var stream = new FileStream(path, FileMode.CreateNew))
                {
                    taskZip = path;
                    WriteNode6TaskZip(stream, taskId, expectedNodeVersion);
                }

                var message = LoadTemplateMessage();
                message.Steps.Clear();
                message.Steps.Add(new TaskStep
                {
                    Reference = new TaskStepDefinitionReference
                    {
                        Id = taskId,
                        Name = "Node6WarningProbe",
                        Version = "1.0.0"
                    },
                    Name = "Node6WarningProbe",
                    DisplayName = "Node 6 warning probe",
                    Id = Guid.NewGuid(),
                    Enabled = true
                });
                message.Variables["DISABLE_NODE6_DEPRECATION_WARNING"] = "true";
                message.Variables["VSTSAGENT_ENABLE_NODE_WARNINGS"] = "false";
                message.Variables["AGENT_RESTRICT_EOL_NODE_VERSIONS"] = eolPolicy.ToString();
                message.Variables["AGENT_USE_ENHANCED_NODE_SELECTION"] = enhancedSelection.ToString();
                message.Variables["AGENT_USE_NODE24"] = (forcedNodeVersion == 24).ToString();
                message.Variables["AGENT_USE_NODE20_1"] = (forcedNodeVersion == 20).ToString();

                var result = await RunWorker(message);

                AssertJobCompleted();
                Assert.Equal(expectMissingLegacyRunner || expectEolFailure ? TaskResult.Failed : TaskResult.Succeeded, result.Result);
                var step = GetSteps().Single(record => record.Name == "Node 6 warning probe");
                string warning = StringUtil.Loc("DeprecatedNodeRunner", "Node 6 warning probe", "1.0.0", "Node6WarningProbe", "6");
                Assert.Contains("(6)", warning);
                Assert.Contains("end-of-life", warning);
                Assert.Contains(step.Issues, issue => issue.Type == IssueType.Warning && issue.Message == warning);
                if (expectMissingLegacyRunner)
                {
                    Assert.Contains(step.Issues, issue => issue.Type == IssueType.Error &&
                        issue.Message.Contains("No compatible Node.js version available for host execution."));
                    Assert.DoesNotContain(GetTimelineLogLines(step), line => line.Contains("NODE6_HANDLER_RAN_ON_NODE_"));
                }
                else if (expectEolFailure)
                {
                    Assert.Contains(step.Issues, issue => issue.Type == IssueType.Error);
                    Assert.DoesNotContain(GetTimelineLogLines(step), line => line.Contains("NODE6_HANDLER_RAN_ON_NODE_"));
                }
                else
                {
                    Assert.Contains(GetTimelineLogLines(step), line => line.Contains($"NODE6_HANDLER_RAN_ON_NODE_{expectedNodeVersion}"));
                }
            }
            finally
            {
                if (taskZip != null && File.Exists(taskZip))
                {
                    File.Delete(taskZip);
                }
                TearDown();
            }
        }

        private static void WriteNode6TaskZip(Stream stream, Guid taskId, int expectedNodeVersion)
        {
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                var definition = new
                {
                    id = taskId,
                    name = "Node6WarningProbe",
                    friendlyName = "Node 6 warning probe",
                    description = "Checks the Node 6 handler warning.",
                    author = "Agent tests",
                    version = new { Major = 1, Minor = 0, Patch = 0 },
                    minimumAgentVersion = "2.0.0",
                    inputs = Array.Empty<object>(),
                    execution = new Dictionary<string, object>
                    {
                        ["Node"] = new { target = "index.js" }
                    }
                };
                using (var writer = new StreamWriter(archive.CreateEntry("task.json").Open()))
                {
                    writer.Write(JsonConvert.SerializeObject(definition));
                }
                using (var writer = new StreamWriter(archive.CreateEntry("index.js").Open()))
                {
                    writer.Write($"if (process.versions.node.split('.')[0] !== '{expectedNodeVersion}') throw new Error('Unexpected Node version');\nconsole.log('NODE6_HANDLER_RAN_ON_NODE_{expectedNodeVersion}');");
                }
            }
        }
    }
}
