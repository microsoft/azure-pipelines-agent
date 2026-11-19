// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Agent.Plugins;
using Agent.Sdk;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.FileContainer;
using Minimatch;
using Xunit;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;

namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    public class TestFileContainerProviderL0
    {
        [Theory]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        [InlineData(new string[] { "**" }, 7,
            new string[] { "ArtifactForTest", "ArtifactForTest/File1.txt", "ArtifactForTest/Folder1", "ArtifactForTest/Folder1/File2.txt",
                "ArtifactForTest/Folder1/File21.txt", "ArtifactForTest/Folder1/Folder2", "ArtifactForTest/Folder1/Folder2/File3.txt" })]
        [InlineData(new string[] { "**", "!**/File2.txt" }, 6,
            new string[] { "ArtifactForTest", "ArtifactForTest/File1.txt", "ArtifactForTest/Folder1", "ArtifactForTest/Folder1/File21.txt",
                "ArtifactForTest/Folder1/Folder2", "ArtifactForTest/Folder1/Folder2/File3.txt" })]
        [InlineData(new string[] { "**", "!**/File2*" }, 5,
            new string[] { "ArtifactForTest", "ArtifactForTest/File1.txt", "ArtifactForTest/Folder1", "ArtifactForTest/Folder1/Folder2",
                "ArtifactForTest/Folder1/Folder2/File3.txt" })]
        [InlineData(new string[] { "**", "!**/Folder2/**" }, 6,
            new string[] { "ArtifactForTest", "ArtifactForTest/File1.txt", "ArtifactForTest/Folder1", "ArtifactForTest/Folder1/File2.txt",
                "ArtifactForTest/Folder1/File21.txt", "ArtifactForTest/Folder1/Folder2" })]
        [InlineData(new string[] { "**/Folder1/**", "!**/File3.txt" }, 3,
            new string[] { "ArtifactForTest/Folder1/File2.txt", "ArtifactForTest/Folder1/File21.txt", "ArtifactForTest/Folder1/Folder2" })]
        [InlineData(new string[] { "**/File*.txt", "!**/File3.txt" }, 3,
            new string[] { "ArtifactForTest/File1.txt", "ArtifactForTest/Folder1/File2.txt", "ArtifactForTest/Folder1/File21.txt" })]
        [InlineData(new string[] { "**", "!**/Folder1/**", "!!**/File3.txt" }, 4,
            new string[] { "ArtifactForTest", "ArtifactForTest/File1.txt", "ArtifactForTest/Folder1", "ArtifactForTest/Folder1/Folder2/File3.txt" })]
        [InlineData(new string[] { "**", "   !**/Folder1/**  ", "!!**/File3.txt" }, 4,
            new string[] { "ArtifactForTest", "ArtifactForTest/File1.txt", "ArtifactForTest/Folder1", "ArtifactForTest/Folder1/Folder2/File3.txt" })]
        [InlineData(new string[] { "**", "!**/Folder1/**", "#!**/Folder2/**", "!!**/File3.txt" }, 4,
            new string[] { "ArtifactForTest", "ArtifactForTest/File1.txt", "ArtifactForTest/Folder1", "ArtifactForTest/Folder1/Folder2/File3.txt" })]
        [InlineData(new string[] { "**", "!**/Folder1/**", " ", "!!**/File3.txt" }, 4,
            new string[] { "ArtifactForTest", "ArtifactForTest/File1.txt", "ArtifactForTest/Folder1", "ArtifactForTest/Folder1/Folder2/File3.txt" })]
        [InlineData(new string[] { "ArtifactForTest/File1.txt" }, 1, new string[] { "ArtifactForTest/File1.txt" })]
        public void TestGettingArtifactItemsWithMinimatchPattern(string[] pttrn, int count, string[] expectedPaths)
        {
            using (TestHostContext hostContext = new TestHostContext(this))
            {
                AgentTaskPluginExecutionContext context = new AgentTaskPluginExecutionContext(hostContext.GetTrace());
                ArtifactItemFilters filters = new ArtifactItemFilters(null, context.CreateArtifactsTracer());

                List<FileContainerItem> items = new List<FileContainerItem>
                {
                    new FileContainerItem() { ItemType = ContainerItemType.Folder, Path = "ArtifactForTest" },
                    new FileContainerItem() { ItemType = ContainerItemType.File, Path = "ArtifactForTest/File1.txt" },
                    new FileContainerItem() { ItemType = ContainerItemType.Folder, Path = "ArtifactForTest/Folder1" },
                    new FileContainerItem() { ItemType = ContainerItemType.File, Path = "ArtifactForTest/Folder1/File2.txt" },
                    new FileContainerItem() { ItemType = ContainerItemType.File, Path = "ArtifactForTest/Folder1/File21.txt" },
                    new FileContainerItem() { ItemType = ContainerItemType.Folder, Path = "ArtifactForTest/Folder1/Folder2" },
                    new FileContainerItem() { ItemType = ContainerItemType.File, Path = "ArtifactForTest/Folder1/Folder2/File3.txt" }
                };

                List<string> paths = new List<string>();
                foreach (FileContainerItem item in items)
                {
                    paths.Add(item.Path);
                }

                string[] minimatchPatterns = pttrn;

                Options customMinimatchOptions = new Options()
                {
                    Dot = true,
                    NoBrace = true,
                    AllowWindowsPaths = PlatformUtil.RunningOnWindows
                };

                Hashtable map = filters.GetMapToFilterItems(paths, minimatchPatterns, customMinimatchOptions);
                List<FileContainerItem> resultItems = filters.ApplyPatternsMapToContainerItems(items, map);

                Assert.Equal(count, resultItems.Count);

                string listPaths = string.Join(", ", expectedPaths);
                List<string> resultPathsList = new List<string>();
                foreach (FileContainerItem item in resultItems)
                {
                    resultPathsList.Add(item.Path);
                }
                string resultPaths = string.Join(", ", resultPathsList);

                Assert.Equal(listPaths, resultPaths);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task DownloadDoesNotWriteThroughFileLinkOutOfContainerMounts()
        {
            string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            try
            {
                string mount = Directory.CreateDirectory(Path.Combine(root, "mount")).FullName;
                string outside = Directory.CreateDirectory(Path.Combine(root, "outside")).FullName;
                string outsideFile = Path.Combine(outside, "file.txt");
                File.WriteAllText(outsideFile, "content");
                Directory.CreateDirectory(Path.Combine(mount, "drop"));
                File.CreateSymbolicLink(Path.Combine(mount, "drop", "file.txt"), outsideFile);

                using (TestHostContext hostContext = new TestHostContext(this))
                {
                    AgentTaskPluginExecutionContext context = new AgentTaskPluginExecutionContext(hostContext.GetTrace());
                    context.Variables["AGENT_ENFORCE_CONTAINER_BOUNDARY"] = new VariableValue("true");
                    context.Container = new ContainerInfo(new Pipelines.ContainerResource() { Alias = "container", Image = "foo" }, isJobContainer: false);
                    context.Container.MountVolumes.Add(new MountVolume(mount, "/mount"));
                    var provider = new FileContainerProvider(null, context.CreateArtifactsTracer());
                    var items = new List<FileContainerItem>
                    {
                        new FileContainerItem() { ItemType = ContainerItemType.Folder, Path = "drop" },
                        new FileContainerItem() { ItemType = ContainerItemType.File, Path = "drop/file.txt" }
                    };
                    var downloadParameters = new ArtifactDownloadParameters() { ParallelizationLimit = 1, IncludeArtifactNameInPath = true };
                    var artifact = new BuildArtifact() { Name = "drop", Resource = new ArtifactResource() { Data = "#/1/drop" } };

                    Exception ex = await Assert.ThrowsAnyAsync<Exception>(() =>
                        provider.DownloadFileContainerAsync(items, downloadParameters, artifact, mount, context, CancellationToken.None));

                    Assert.Contains("outside the mounts of the container", ex.ToString());
                    Assert.Equal("content", File.ReadAllText(outsideFile));
                }
            }
            finally
            {
                IOUtil.DeleteDirectory(root, CancellationToken.None);
            }
        }
    }
}
