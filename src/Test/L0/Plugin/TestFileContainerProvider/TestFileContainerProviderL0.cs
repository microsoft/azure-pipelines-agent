// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Agent.Plugins;
using Agent.Sdk;
using Microsoft.VisualStudio.Services.FileContainer;
using Minimatch;
using Xunit;

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
    }
}
