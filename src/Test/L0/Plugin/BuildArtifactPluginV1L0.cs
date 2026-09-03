// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
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
                    return FileSystemCaseSensitivity.CaseInsensitive;
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
                    return FileSystemCaseSensitivity.Indeterminate;
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
                _ => FileSystemCaseSensitivity.CaseInsensitive,
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
                _ => FileSystemCaseSensitivity.CaseSensitive,
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
                _ => FileSystemCaseSensitivity.Indeterminate,
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
                    return FileSystemCaseSensitivity.CaseInsensitive;
                },
                runningOnWindows: false,
                runningOnMacOS: false);

            Assert.False(options.NoCase);
            Assert.Equal(0, detectionCallCount);
        }

        [Theory]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        [InlineData(true, true)]
        [InlineData(false, false)]
        public void DetectProbesInsideNearestExistingDestinationAncestor(
            bool alternateCaseExists,
            bool expectedCaseInsensitive)
        {
            string rootPath = Path.GetPathRoot(Path.GetFullPath(Environment.CurrentDirectory));
            string volumesPath = Path.Combine(rootPath, "Volumes");
            string mountedVolumePath = Path.Combine(volumesPath, "ArtifactCaseSensitive");
            string targetPath = Path.Combine(mountedVolumePath, "download");
            string childPath = Path.Combine(mountedVolumePath, "ProbeFile.txt");
            string alternateCaseChildPath = Path.Combine(mountedVolumePath, "probeFile.txt");

            bool DirectoryExists(string path)
            {
                return string.Equals(path, rootPath, StringComparison.Ordinal)
                    || string.Equals(path, volumesPath, StringComparison.Ordinal)
                    || string.Equals(path, mountedVolumePath, StringComparison.Ordinal);
            }

            bool FileSystemEntryExists(string path)
            {
                Assert.Equal(alternateCaseChildPath, path);
                return alternateCaseExists;
            }

            IEnumerable<string> EnumerateFileSystemEntries(string path)
            {
                Assert.Equal(mountedVolumePath, path);
                return new[] { childPath };
            }

            FileSystemCaseSensitivity caseSensitivity = FileSystemCaseSensitivityDetector.Detect(
                targetPath,
                DirectoryExists,
                FileSystemEntryExists,
                EnumerateFileSystemEntries);

            FileSystemCaseSensitivity expectedCaseSensitivity = expectedCaseInsensitive
                ? FileSystemCaseSensitivity.CaseInsensitive
                : FileSystemCaseSensitivity.CaseSensitive;
            Assert.Equal(expectedCaseSensitivity, caseSensitivity);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void DetectSupportsRootDestination()
        {
            string rootPath = Path.GetPathRoot(Path.GetFullPath(Environment.CurrentDirectory));
            string childPath = Path.Combine(rootPath, "RootProbe");
            string alternateCaseChildPath = Path.Combine(rootPath, "rootProbe");

            bool DirectoryExists(string path)
            {
                return string.Equals(path, rootPath, StringComparison.Ordinal);
            }

            bool FileSystemEntryExists(string path)
            {
                Assert.Equal(alternateCaseChildPath, path);
                return true;
            }

            IEnumerable<string> EnumerateFileSystemEntries(string path)
            {
                Assert.Equal(rootPath, path);
                return new[] { childPath };
            }

            FileSystemCaseSensitivity caseSensitivity = FileSystemCaseSensitivityDetector.Detect(
                rootPath,
                DirectoryExists,
                FileSystemEntryExists,
                EnumerateFileSystemEntries);

            Assert.Equal(FileSystemCaseSensitivity.CaseInsensitive, caseSensitivity);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void DetectReturnsIndeterminateWhenNearestExistingDirectoryIsEmpty()
        {
            string rootPath = Path.GetPathRoot(Path.GetFullPath(Environment.CurrentDirectory));
            string existingPath = Path.Combine(rootPath, "ExistingSegment");
            string targetPath = Path.Combine(existingPath, "destination");

            bool DirectoryExists(string path)
            {
                return string.Equals(path, existingPath, StringComparison.Ordinal);
            }

            bool FileSystemEntryExists(string path)
            {
                throw new InvalidOperationException($"Unexpected existence probe for '{path}'.");
            }

            IEnumerable<string> EnumerateFileSystemEntries(string path)
            {
                Assert.Equal(existingPath, path);
                return Array.Empty<string>();
            }

            FileSystemCaseSensitivity caseSensitivity = FileSystemCaseSensitivityDetector.Detect(
                targetPath,
                DirectoryExists,
                FileSystemEntryExists,
                EnumerateFileSystemEntries);

            Assert.Equal(FileSystemCaseSensitivity.Indeterminate, caseSensitivity);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void DetectReturnsIndeterminateWhenNoEntryHasAsciiLetter()
        {
            string rootPath = Path.GetPathRoot(Path.GetFullPath(Environment.CurrentDirectory));
            string existingPath = Path.Combine(rootPath, "ExistingSegment");
            string targetPath = Path.Combine(existingPath, "destination");

            bool DirectoryExists(string path)
            {
                return string.Equals(path, existingPath, StringComparison.Ordinal);
            }

            bool FileSystemEntryExists(string path)
            {
                throw new InvalidOperationException($"Unexpected existence probe for '{path}'.");
            }

            IEnumerable<string> EnumerateFileSystemEntries(string path)
            {
                Assert.Equal(existingPath, path);
                return new[] { Path.Combine(existingPath, "12345") };
            }

            FileSystemCaseSensitivity caseSensitivity = FileSystemCaseSensitivityDetector.Detect(
                targetPath,
                DirectoryExists,
                FileSystemEntryExists,
                EnumerateFileSystemEntries);

            Assert.Equal(FileSystemCaseSensitivity.Indeterminate, caseSensitivity);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void DetectReturnsIndeterminateWhenCaseEquivalentEntriesAreAmbiguous()
        {
            string rootPath = Path.GetPathRoot(Path.GetFullPath(Environment.CurrentDirectory));
            string existingPath = Path.Combine(rootPath, "ExistingSegment");
            string targetPath = Path.Combine(existingPath, "destination");

            bool DirectoryExists(string path)
            {
                return string.Equals(path, existingPath, StringComparison.Ordinal);
            }

            bool FileSystemEntryExists(string path)
            {
                throw new InvalidOperationException($"Unexpected existence probe for '{path}'.");
            }

            IEnumerable<string> EnumerateFileSystemEntries(string path)
            {
                Assert.Equal(existingPath, path);
                return new[]
                {
                    Path.Combine(existingPath, "ProbeFile.txt"),
                    Path.Combine(existingPath, "probeFile.txt")
                };
            }

            FileSystemCaseSensitivity caseSensitivity = FileSystemCaseSensitivityDetector.Detect(
                targetPath,
                DirectoryExists,
                FileSystemEntryExists,
                EnumerateFileSystemEntries);

            Assert.Equal(FileSystemCaseSensitivity.Indeterminate, caseSensitivity);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void DetectReturnsIndeterminateWhenReadOnlyProbeFails()
        {
            string rootPath = Path.GetPathRoot(Path.GetFullPath(Environment.CurrentDirectory));
            string existingPath = Path.Combine(rootPath, "ExistingSegment");
            string targetPath = Path.Combine(existingPath, "destination");

            bool DirectoryExists(string path)
            {
                return string.Equals(path, existingPath, StringComparison.Ordinal);
            }

            bool FileSystemEntryExists(string path)
            {
                throw new InvalidOperationException($"Unexpected existence probe for '{path}'.");
            }

            IEnumerable<string> EnumerateFileSystemEntries(string _)
            {
                throw new UnauthorizedAccessException();
            }

            FileSystemCaseSensitivity caseSensitivity = FileSystemCaseSensitivityDetector.Detect(
                targetPath,
                DirectoryExists,
                FileSystemEntryExists,
                EnumerateFileSystemEntries);

            Assert.Equal(FileSystemCaseSensitivity.Indeterminate, caseSensitivity);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void DetectReturnsIndeterminateWhenAlternateEntryResolutionFails()
        {
            string rootPath = Path.GetPathRoot(Path.GetFullPath(Environment.CurrentDirectory));
            string existingPath = Path.Combine(rootPath, "ExistingSegment");
            string targetPath = Path.Combine(existingPath, "destination");
            string childPath = Path.Combine(existingPath, "ProbeFile.txt");

            bool DirectoryExists(string path)
            {
                return string.Equals(path, existingPath, StringComparison.Ordinal);
            }

            bool FileSystemEntryExists(string _)
            {
                throw new UnauthorizedAccessException();
            }

            IEnumerable<string> EnumerateFileSystemEntries(string path)
            {
                Assert.Equal(existingPath, path);
                return new[] { childPath };
            }

            FileSystemCaseSensitivity caseSensitivity = FileSystemCaseSensitivityDetector.Detect(
                targetPath,
                DirectoryExists,
                FileSystemEntryExists,
                EnumerateFileSystemEntries);

            Assert.Equal(FileSystemCaseSensitivity.Indeterminate, caseSensitivity);
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
