// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Xunit;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Util
{
    public sealed class PluginFileAccessGuardL0 : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        private readonly string _mount;
        private readonly string _outside;
        private readonly string _readOnlyMount;
        private readonly string _nestedReadOnlyMount;
        private readonly ContainerInfo _container;

        public PluginFileAccessGuardL0()
        {
            _mount = Directory.CreateDirectory(Path.Combine(_root, "mount")).FullName;
            _outside = Directory.CreateDirectory(Path.Combine(_root, "outside")).FullName;
            _readOnlyMount = Directory.CreateDirectory(Path.Combine(_root, "readonlymount")).FullName;
            // Like "_work/_tasks": a read-only mount inside a writable one.
            _nestedReadOnlyMount = Directory.CreateDirectory(Path.Combine(_mount, "nested_readonly")).FullName;
            _container = new ContainerInfo(new Pipelines.ContainerResource() { Alias = "container", Image = "foo" }, isJobContainer: false);
            _container.MountVolumes.Add(new MountVolume(_mount, "/mount"));
            // Like "mountReadOnly: { tools: true }" in YAML.
            _container.MountVolumes.Add(new MountVolume(_readOnlyMount, "/readonlymount", readOnly: true));
            _container.MountVolumes.Add(new MountVolume(_nestedReadOnlyMount, "/mount/nested_readonly", readOnly: true));
        }

        public void Dispose()
        {
            IOUtil.DeleteDirectory(_root, CancellationToken.None);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Util")]
        public void EnsureWithinMounts_AllowsPathInsideMount()
        {
            string path = Path.Combine(_mount, "a", "b");
            Assert.Equal(path, PluginFileAccessGuard.EnsureWithinMounts(_container, path, write: true));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Util")]
        public void EnsureWithinMounts_BlocksPathOutsideMounts()
        {
            Assert.Throws<InvalidOperationException>(() =>
                PluginFileAccessGuard.EnsureWithinMounts(_container, Path.Combine(_outside, "a"), write: false));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Util")]
        public void EnsureTreeWithinMounts_BlocksWhenRootIsLinkOutsideMounts()
        {
            string root = Path.Combine(_mount, "staging");
            CreateDirectoryLink(root, _outside);

            Assert.Throws<InvalidOperationException>(() =>
                PluginFileAccessGuard.EnsureTreeWithinMounts(_container, root, write: true));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Util")]
        public void EnsureTreeWithinMounts_BlocksWhenParentIsLinkOutsideMounts()
        {
            // The folder itself is ordinary, but its parent is a link.
            string staging = Path.Combine(_mount, "staging");
            CreateDirectoryLink(staging, _outside);
            string artifactUnderLink = Path.Combine(staging, "artifact");
            Directory.CreateDirectory(artifactUnderLink);

            Assert.Throws<InvalidOperationException>(() =>
                PluginFileAccessGuard.EnsureTreeWithinMounts(_container, artifactUnderLink, write: true));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Util")]
        public void EnsureTreeWithinMounts_BlocksWhenNestedEntryIsLinkOutsideMounts()
        {
            string root = Directory.CreateDirectory(Path.Combine(_mount, "staging")).FullName;
            string nestedLink = Path.Combine(root, "subdir");
            CreateDirectoryLink(nestedLink, _outside);

            Assert.Throws<InvalidOperationException>(() =>
                PluginFileAccessGuard.EnsureTreeWithinMounts(_container, root, write: true));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Util")]
        public void EnsureTreeWithinMounts_AllowsLinkThatResolvesInsideMounts()
        {
            string root = Directory.CreateDirectory(Path.Combine(_mount, "staging")).FullName;
            string realTarget = Directory.CreateDirectory(Path.Combine(_mount, "realtarget")).FullName;
            string nestedLink = Path.Combine(root, "subdir");
            CreateDirectoryLink(nestedLink, realTarget);

            // Allowed: the link points inside the mounts.
            PluginFileAccessGuard.EnsureTreeWithinMounts(_container, root, write: true);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Util")]
        public void EnsureWithinMounts_BlocksWriteToReadOnlyMount_DirectPath()
        {
            string path = Path.Combine(_readOnlyMount, "a");
            Assert.Throws<InvalidOperationException>(() =>
                PluginFileAccessGuard.EnsureWithinMounts(_container, path, write: true));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Util")]
        public void EnsureWithinMounts_AllowsReadFromReadOnlyMount_DirectPath()
        {
            string path = Path.Combine(_readOnlyMount, "a");
            Assert.Equal(path, PluginFileAccessGuard.EnsureWithinMounts(_container, path, write: false));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Util")]
        public void EnsureTreeWithinMounts_BlocksWriteThroughLinkToReadOnlyMount()
        {
            string root = Directory.CreateDirectory(Path.Combine(_mount, "staging")).FullName;
            string nestedLink = Path.Combine(root, "subdir");
            CreateDirectoryLink(nestedLink, _readOnlyMount);

            Assert.Throws<InvalidOperationException>(() =>
                PluginFileAccessGuard.EnsureTreeWithinMounts(_container, root, write: true));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Util")]
        public void EnsureWithinMounts_BlocksWriteToReadOnlyMountNestedUnderWritableMount_DirectPath()
        {
            // The more specific read-only mount wins over the writable mount around it.
            string path = Path.Combine(_nestedReadOnlyMount, "file.txt");
            Assert.Throws<InvalidOperationException>(() =>
                PluginFileAccessGuard.EnsureWithinMounts(_container, path, write: true));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Util")]
        public void EnsureWithinMounts_AllowsWriteToBroaderMountOutsideNestedReadOnlySubfolder()
        {
            string path = Path.Combine(_mount, "ordinary.txt");
            Assert.Equal(path, PluginFileAccessGuard.EnsureWithinMounts(_container, path, write: true));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Util")]
        public void EnsureTreeWithinMounts_BlocksNotYetCreatedFolderUnderLinkOutsideMounts()
        {
            string staging = Path.Combine(_mount, "staging");
            CreateDirectoryLink(staging, _outside);

            Assert.Throws<InvalidOperationException>(() =>
                PluginFileAccessGuard.EnsureTreeWithinMounts(_container, Path.Combine(staging, "new"), write: true));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Util")]
        public void EnsureTreeWithinMounts_AllowsNotYetCreatedFolderInsideMount()
        {
            PluginFileAccessGuard.EnsureTreeWithinMounts(_container, Path.Combine(_mount, "new", "nested"), write: true);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Util")]
        public void EnsureWithinMounts_BlocksLinkWhoseTargetGoesThroughAnotherLink()
        {
            string x = Path.Combine(_mount, "x");
            CreateDirectoryLink(x, _outside);
            string staging = Path.Combine(_mount, "staging");
            CreateDirectoryLink(staging, Path.Combine(x, "sub"));

            Assert.Throws<InvalidOperationException>(() =>
                PluginFileAccessGuard.EnsureWithinMounts(_container, Path.Combine(staging, "file.txt"), write: true));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Util")]
        public void EnsureWithinMounts_BlocksRelativeLinkWithParentSegmentAfterAnotherLink()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            Directory.CreateDirectory(Path.Combine(_outside, "deep"));
            Directory.CreateSymbolicLink(Path.Combine(_mount, "x"), Path.Combine(_outside, "deep"));
            string staging = Path.Combine(_mount, "staging");
            Directory.CreateSymbolicLink(staging, "x/../other");

            Assert.Throws<InvalidOperationException>(() =>
                PluginFileAccessGuard.EnsureWithinMounts(_container, Path.Combine(staging, "file.txt"), write: true));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Util")]
        public void EnsureWithinMounts_BlocksLinkReachedAfterParentSegment()
        {
            string one = Directory.CreateDirectory(Path.Combine(_mount, "one")).FullName;
            Directory.CreateDirectory(Path.Combine(_mount, "two", "deep"));
            CreateDirectoryLink(Path.Combine(one, "a"), Path.Combine(_mount, "two", "deep"));
            CreateDirectoryLink(Path.Combine(one, "b"), _outside);

            Assert.Throws<InvalidOperationException>(() =>
                PluginFileAccessGuard.EnsureWithinMounts(_container, Path.Combine(one, "a", "..", "b", "file.txt"), write: true));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Util")]
        public void EnsureTreeWithinMounts_StopsAtLinkBackToParentFolder()
        {
            string root = Directory.CreateDirectory(Path.Combine(_mount, "staging")).FullName;
            CreateDirectoryLink(Path.Combine(root, "loop"), root);

            PluginFileAccessGuard.EnsureTreeWithinMounts(_container, root, write: true);
        }

        internal static void CreateDirectoryLink(string link, string target)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Directory.CreateSymbolicLink(link, target);
                return;
            }

            var startInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec"), $"/c mklink /J \"{link}\" \"{target}\"")
            {
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            using (Process process = Process.Start(startInfo))
            {
                process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                Assert.Equal(0, process.ExitCode);
            }
        }
    }
}
