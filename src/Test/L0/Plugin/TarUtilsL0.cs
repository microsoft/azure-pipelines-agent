using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using System.Threading;
using Agent.Sdk;
using Agent.Plugins.PipelineCache;
using System.Diagnostics;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.BlobStore.WebApi;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;

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

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task DownloadAndExtractTar_DoesNotCreateFolderThroughLinkOutOfContainerMounts()
        {
            string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            try
            {
                string mount = Directory.CreateDirectory(Path.Combine(root, "mount")).FullName;
                string outside = Directory.CreateDirectory(Path.Combine(root, "outside")).FullName;
                string link = Path.Combine(mount, "cache");
                Util.PluginFileAccessGuardL0.CreateDirectoryLink(link, outside);

                var context = new AgentTaskPluginExecutionContext();
                context.Variables["DistributedTask.Agent.EnforceContainerBoundary"] = new VariableValue("true");
                context.Container = new ContainerInfo(new Pipelines.ContainerResource() { Alias = "container", Image = "foo" }, isJobContainer: false);
                context.Container.MountVolumes.Add(new MountVolume(mount, "/mount"));
                var manifest = new Manifest(new List<ManifestItem> { new ManifestItem("/archive.tar", new DedupInfo("unused", 0)) });

                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    TarUtils.DownloadAndExtractTarAsync(context, manifest, dedupManifestClient: null, Path.Combine(link, "restored"), CancellationToken.None));

                // The check must run before the folder is created, not after.
                Assert.False(Directory.Exists(Path.Combine(outside, "restored")));
            }
            finally
            {
                IOUtil.DeleteDirectory(root, CancellationToken.None);
            }
        }
    }
}