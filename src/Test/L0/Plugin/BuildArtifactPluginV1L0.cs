// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Plugins.BuildArtifacts;
using Agent.Sdk;
using Agent.Sdk.Knob;
using Minimatch;
using Moq;
using Xunit;

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
