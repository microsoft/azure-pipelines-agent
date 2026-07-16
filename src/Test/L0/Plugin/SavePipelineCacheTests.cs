// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Agent.Plugins.PipelineCache;
using Agent.Sdk;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.PipelineCache
{
    public class SavePipelineCacheTests
    {
        private const string SaveOnPartialSuccessInputName = "saveOnPartialSuccess";
        private const string JobStatusVariableName = "agent.jobstatus";

        private sealed class CapturingTraceWriter : ITraceWriter
        {
            private readonly StringBuilder _output = new StringBuilder();

            public string Output => _output.ToString();

            public void Info(string message, string operation = "") => _output.AppendLine(message);

            public void Verbose(string message, string operation = "") => _output.AppendLine(message);
        }

        // The restore step variable is intentionally left unset so RunAsync always returns early
        // (never invoking the base upload logic), which lets us assert on the skip reason.
        private static async Task<string> RunSaveAsync(string jobStatus, string saveOnPartialSuccess)
        {
            var trace = new CapturingTraceWriter();
            var context = new AgentTaskPluginExecutionContext(trace);
            context.Variables[JobStatusVariableName] = jobStatus;
            if (saveOnPartialSuccess != null)
            {
                context.Inputs[SaveOnPartialSuccessInputName] = saveOnPartialSuccess;
            }

            var plugin = new SavePipelineCacheV0();
            await plugin.RunAsync(context, CancellationToken.None);
            return trace.Output;
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task Save_Succeeded_PassesJobStatusCheck()
        {
            string output = await RunSaveAsync(TaskResult.Succeeded.ToString(), saveOnPartialSuccess: null);

            Assert.DoesNotContain("job status was not", output);
            Assert.Contains("restore step did not run", output);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task Save_SucceededWithIssues_SkipsByDefault()
        {
            string output = await RunSaveAsync(TaskResult.SucceededWithIssues.ToString(), saveOnPartialSuccess: null);

            Assert.Contains("job status was not 'Succeeded'.", output);
            Assert.DoesNotContain("restore step did not run", output);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task Save_SucceededWithIssues_SavesWhenPartialSuccessEnabled()
        {
            string output = await RunSaveAsync(TaskResult.SucceededWithIssues.ToString(), saveOnPartialSuccess: "true");

            Assert.DoesNotContain("job status was not", output);
            Assert.Contains("restore step did not run", output);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task Save_Failed_SkipsEvenWhenPartialSuccessEnabled()
        {
            string output = await RunSaveAsync(TaskResult.Failed.ToString(), saveOnPartialSuccess: "true");

            Assert.Contains("job status was not 'Succeeded' or 'SucceededWithIssues'.", output);
            Assert.DoesNotContain("restore step did not run", output);
        }
    }
}
