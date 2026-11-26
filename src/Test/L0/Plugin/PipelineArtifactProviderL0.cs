// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading;
using Agent.Plugins;
using Agent.Sdk;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Xunit;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;

namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    public sealed class PipelineArtifactProviderL0
    {
        [Theory]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        [InlineData(true)]
        [InlineData(false)]
        public void DownloadTargetIsCheckedOnlyWhenBoundaryEnforced(bool enforce)
        {
            string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            try
            {
                string mount = Directory.CreateDirectory(Path.Combine(root, "mount")).FullName;
                string outside = Directory.CreateDirectory(Path.Combine(root, "outside")).FullName;
                string target = Directory.CreateDirectory(Path.Combine(mount, "download")).FullName;
                Util.PluginFileAccessGuardL0.CreateDirectoryLink(Path.Combine(target, "drop"), outside);

                using (var hostContext = new TestHostContext(this))
                {
                    var context = new AgentTaskPluginExecutionContext(hostContext.GetTrace());
                    context.Variables["DistributedTask.Agent.EnforceContainerBoundary"] = new VariableValue(enforce ? "true" : "false");
                    context.Container = new ContainerInfo(new Pipelines.ContainerResource() { Alias = "container", Image = "foo" }, isJobContainer: false);
                    context.Container.MountVolumes.Add(new MountVolume(mount, "/mount"));

                    if (enforce)
                    {
                        Assert.Throws<InvalidOperationException>(() => PipelineArtifactProvider.EnsureTargetWithinMounts(context, target));
                    }
                    else
                    {
                        Assert.Equal(target, PipelineArtifactProvider.EnsureTargetWithinMounts(context, target));
                    }
                }
            }
            finally
            {
                IOUtil.DeleteDirectory(root, CancellationToken.None);
            }
        }
    }
}
