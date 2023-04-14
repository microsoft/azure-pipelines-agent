using System;
using System.Threading.Tasks;
using Xunit;
using System.Threading;
using Agent.Sdk;
using Agent.Plugins.PipelineCache;
using System.Diagnostics;

namespace Microsoft.VisualStudio.Services.Agent.Tests.PipelineCaching
{
    public class TarUtilsL0
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task UnavailableProcessDependency_ThrowsNiceError()
        {
            var startInfo = new ProcessStartInfo();
            startInfo.FileName = "ThisProcessObviouslyWillFail";
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await TarUtils.RunProcessAsync(new AgentTaskPluginExecutionContext(), startInfo, (p, ct) => null, () => { }, new CancellationToken()));
        }
    }
}