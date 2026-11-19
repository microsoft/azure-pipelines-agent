// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Agent.Plugins.BuildArtifacts;
using Agent.Sdk;
using Agent.Sdk.Knob;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Minimatch;
using Moq;
using Xunit;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;

namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    public sealed class BuildArtifactPluginV1L0
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void CreateMinimatchOptionsWithFlagOffDoesNotDetectFileSystemCaseSensitivity()
        {
            Mock<IKnobValueContext> knobContext = CreateKnobContext(isEnabled: false);
            int detectionCallCount = 0;

            Options options = DownloadBuildArtifactTaskV1_0_0.CreateMinimatchOptions(
                knobContext.Object,
                "unused",
                _ =>
                {
                    detectionCallCount++;
                    return false;
                },
                runningOnWindows: true,
                runningOnMacOS: false);

            Assert.False(options.NoCase);
            Assert.Equal(0, detectionCallCount);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void CreateMinimatchOptionsUsesNoCaseOnWindowsWithoutDetection()
        {
            Mock<IKnobValueContext> knobContext = CreateKnobContext(isEnabled: true);
            int detectionCallCount = 0;

            Options options = DownloadBuildArtifactTaskV1_0_0.CreateMinimatchOptions(
                knobContext.Object,
                "unused",
                _ =>
                {
                    detectionCallCount++;
                    return null;
                },
                runningOnWindows: true,
                runningOnMacOS: false);

            Assert.True(options.NoCase);
            Assert.Equal(0, detectionCallCount);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void CreateMinimatchOptionsUsesNoCaseWhenMacFileSystemIsCaseInsensitive()
        {
            Mock<IKnobValueContext> knobContext = CreateKnobContext(isEnabled: true);

            Options options = DownloadBuildArtifactTaskV1_0_0.CreateMinimatchOptions(
                knobContext.Object,
                "unused",
                _ => false,
                runningOnWindows: false,
                runningOnMacOS: true);

            Assert.True(options.NoCase);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void CreateMinimatchOptionsUsesCaseSensitiveMatchingWhenMacFileSystemIsCaseSensitive()
        {
            Mock<IKnobValueContext> knobContext = CreateKnobContext(isEnabled: true);

            Options options = DownloadBuildArtifactTaskV1_0_0.CreateMinimatchOptions(
                knobContext.Object,
                "unused",
                _ => true,
                runningOnWindows: false,
                runningOnMacOS: true);

            Assert.False(options.NoCase);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void CreateMinimatchOptionsUsesCaseSensitiveMatchingWhenDetectionIsIndeterminate()
        {
            Mock<IKnobValueContext> knobContext = CreateKnobContext(isEnabled: true);

            Options options = DownloadBuildArtifactTaskV1_0_0.CreateMinimatchOptions(
                knobContext.Object,
                "unused",
                _ => null,
                runningOnWindows: false,
                runningOnMacOS: true);

            Assert.False(options.NoCase);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void CreateMinimatchOptionsPreservesCaseSensitiveMatchingOnUnsupportedPlatforms()
        {
            Mock<IKnobValueContext> knobContext = CreateKnobContext(isEnabled: true);
            int detectionCallCount = 0;

            Options options = DownloadBuildArtifactTaskV1_0_0.CreateMinimatchOptions(
                knobContext.Object,
                "unused",
                _ =>
                {
                    detectionCallCount++;
                    return false;
                },
                runningOnWindows: false,
                runningOnMacOS: false);

            Assert.False(options.NoCase);
            Assert.Equal(0, detectionCallCount);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task CleanDestinationFolderDoesNotDeleteThroughLinkOutOfContainerMounts()
        {
            string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            try
            {
                string mount = Directory.CreateDirectory(Path.Combine(root, "mount")).FullName;
                string outside = Directory.CreateDirectory(Path.Combine(root, "outside")).FullName;
                string outsideFile = Path.Combine(outside, "file.txt");
                File.WriteAllText(outsideFile, "content");
                string downloadPath = Path.Combine(mount, "download");
                Util.PluginFileAccessGuardL0.CreateDirectoryLink(downloadPath, outside);

                using (var hostContext = new TestHostContext(this))
                {
                    var context = new AgentTaskPluginExecutionContext(hostContext.GetTrace());
                    context.Variables["AGENT_ENFORCE_CONTAINER_BOUNDARY"] = new VariableValue("true");
                    context.Variables["system.defaultworkingdirectory"] = new VariableValue(mount);
                    context.Variables["Agent.TempDirectory"] = new VariableValue(mount);
                    context.Variables["system.teamProjectId"] = new VariableValue(Guid.NewGuid().ToString());
                    context.Variables["build.buildId"] = new VariableValue("1");
                    context.Inputs["buildType"] = "current";
                    context.Inputs["downloadType"] = "single";
                    context.Inputs["artifactName"] = "drop";
                    context.Inputs["downloadPath"] = downloadPath;
                    context.Inputs["cleanDestinationFolder"] = "true";
                    context.Inputs["tags"] = string.Empty;
                    context.Container = new ContainerInfo(new Pipelines.ContainerResource() { Alias = "container", Image = "foo" }, isJobContainer: false);
                    context.Container.MountVolumes.Add(new MountVolume(mount, "/mount"));

                    await Assert.ThrowsAsync<InvalidOperationException>(() =>
                        new DownloadBuildArtifactTaskV1_0_0().RunAsync(context, CancellationToken.None));

                    Assert.True(File.Exists(outsideFile));
                }
            }
            finally
            {
                IOUtil.DeleteDirectory(root, CancellationToken.None);
            }
        }

        private static Mock<IKnobValueContext> CreateKnobContext(bool isEnabled)
        {
            Mock<IKnobValueContext> knobContext = new Mock<IKnobValueContext>();
            knobContext
                .Setup(x => x.GetVariableValueOrDefault(
                    "DistributedTask.Agent.CaseInsensitiveArtifactMatchingFixEnabled"))
                .Returns(isEnabled ? "true" : null);
            knobContext
                .Setup(x => x.GetScopedEnvironment())
                .Returns(new LocalEnvironment());

            return knobContext;
        }
    }
}
