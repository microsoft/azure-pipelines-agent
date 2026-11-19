// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Agent.Plugins.PipelineCache;
using Agent.Sdk;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Xunit;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;

namespace Microsoft.VisualStudio.Services.Agent.Tests.PipelineCache
{
    public sealed class PipelineCacheServerL0 : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        private readonly string _mount;
        private readonly string _cacheDirectory;

        public PipelineCacheServerL0()
        {
            _mount = Directory.CreateDirectory(Path.Combine(_root, "mount")).FullName;
            string outside = Directory.CreateDirectory(Path.Combine(_root, "outside")).FullName;
            _cacheDirectory = Directory.CreateDirectory(Path.Combine(_mount, "cache")).FullName;
            Util.PluginFileAccessGuardL0.CreateDirectoryLink(Path.Combine(_cacheDirectory, "sub"), outside);
        }

        public void Dispose()
        {
            IOUtil.DeleteDirectory(_root, CancellationToken.None);
        }

        [Theory]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        [InlineData(true)]
        [InlineData(false)]
        public async Task SaveChecksFolderContentsOnlyWhenBoundaryEnforced(bool enforce)
        {
            using (var hostContext = new TestHostContext(this))
            {
                AgentTaskPluginExecutionContext context = CreateContext(hostContext, enforce);
                var server = new PipelineCacheServer(context);

                if (enforce)
                {
                    await Assert.ThrowsAsync<InvalidOperationException>(() =>
                        server.GetUploadPathAsync(ContentFormat.Files, context, _cacheDirectory, CancellationToken.None));
                }
                else
                {
                    Assert.Equal(_cacheDirectory, await server.GetUploadPathAsync(ContentFormat.Files, context, _cacheDirectory, CancellationToken.None));
                }
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task RestoreChecksFolderContentsBeforeDownload()
        {
            using (var hostContext = new TestHostContext(this))
            {
                AgentTaskPluginExecutionContext context = CreateContext(hostContext, enforce: true);
                var server = new PipelineCacheServer(context);

                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    server.DownloadPipelineCacheAsync(context, dedupManifestClient: null, manifestId: null, _cacheDirectory, ContentFormat.Files, CancellationToken.None));
            }
        }

        private AgentTaskPluginExecutionContext CreateContext(TestHostContext hostContext, bool enforce)
        {
            var context = new AgentTaskPluginExecutionContext(hostContext.GetTrace());
            context.Variables["AGENT_ENFORCE_CONTAINER_BOUNDARY"] = new VariableValue(enforce ? "true" : "false");
            context.Container = new ContainerInfo(new Pipelines.ContainerResource() { Alias = "container", Image = "foo" }, isJobContainer: false);
            context.Container.MountVolumes.Add(new MountVolume(_mount, "/mount"));
            return context;
        }
    }
}
