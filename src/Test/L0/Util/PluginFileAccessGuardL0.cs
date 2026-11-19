// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using Xunit;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Util
{
    public sealed class PluginFileAccessGuardL0 : IDisposable
    {
        private readonly string _root = Path.Combine(PluginFileAccessGuard.ResolveAllLinks(Path.GetTempPath()), Guid.NewGuid().ToString("N"));
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

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Util")]
        public void EnsureWithinMounts_UsesMountsChangedAfterEarlierCheck()
        {
            string outsidePath = Path.Combine(_outside, "a");
            string mountPath = Path.Combine(_mount, "a");
            Assert.Throws<InvalidOperationException>(() =>
                PluginFileAccessGuard.EnsureWithinMounts(_container, outsidePath, write: true));
            PluginFileAccessGuard.EnsureWithinMounts(_container, mountPath, write: true);

            _container.MountVolumes.Add(new MountVolume(_outside, "/outside"));
            _container.MountVolumes[0].ReadOnly = true;

            Assert.Equal(outsidePath, PluginFileAccessGuard.EnsureWithinMounts(_container, outsidePath, write: true));
            Assert.Throws<InvalidOperationException>(() =>
                PluginFileAccessGuard.EnsureWithinMounts(_container, mountPath, write: true));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Util")]
        public void EnsureWithinMounts_ChecksSharedFolderAgainAfterItIsReplacedByLink()
        {
            string writable = Directory.CreateDirectory(Path.Combine(_root, "writable")).FullName;
            ContainerInfo container = new ContainerInfo(new Pipelines.ContainerResource() { Alias = "container", Image = "foo" }, isJobContainer: false);
            container.MountVolumes.Add(new MountVolume(writable, "/writable"));
            container.MountVolumes.Add(new MountVolume(_readOnlyMount, "/readonlymount", readOnly: true));
            string path = Path.Combine(writable, "file.txt");
            PluginFileAccessGuard.EnsureWithinMounts(container, path, write: true);

            Directory.Delete(writable);
            CreateDirectoryLink(writable, _readOnlyMount);

            Assert.Throws<InvalidOperationException>(() =>
                PluginFileAccessGuard.EnsureWithinMounts(container, path, write: true));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Util")]
        public void EnsureWithinMounts_ChecksPartsBelowFolderThatCannotBeRead()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            string parent = Directory.CreateDirectory(Path.Combine(_mount, "parent")).FullName;
            string folder = Directory.CreateDirectory(Path.Combine(parent, "folder")).FullName;
            CreateDirectoryLink(Path.Combine(folder, "staging"), _outside);
            SetDenyRule(folder, FileSystemRights.ReadAttributes, deny: true);
            SetDenyRule(parent, FileSystemRights.ListDirectory, deny: true);
            try
            {
                Assert.Throws<InvalidOperationException>(() =>
                    PluginFileAccessGuard.EnsureWithinMounts(_container, Path.Combine(folder, "staging", "file.txt"), write: true));
            }
            finally
            {
                SetDenyRule(parent, FileSystemRights.ListDirectory, deny: false);
                SetDenyRule(folder, FileSystemRights.ReadAttributes, deny: false);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Util")]
        public void EnsureTreeWithinMounts_ChecksEntriesBelowLinkChainTooLongToFollow()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            File.WriteAllText(Path.Combine(_outside, "file.txt"), "content");
            string chain = Directory.CreateDirectory(Path.Combine(_mount, "chain")).FullName;
            string previous = _outside;
            for (int i = 0; i < 70; i++)
            {
                string link = Path.Combine(chain, "link" + i);
                Directory.CreateSymbolicLink(link, previous);
                previous = link;
            }

            string root = Directory.CreateDirectory(Path.Combine(_mount, "staging")).FullName;
            Directory.CreateSymbolicLink(Path.Combine(root, "top"), previous);

            Assert.Throws<InvalidOperationException>(() =>
                PluginFileAccessGuard.EnsureTreeWithinMounts(_container, root, write: false));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Util")]
        public void EnsureWithinMounts_BlocksLinkWhoseTargetCannotBeRead()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            string folder = Directory.CreateDirectory(Path.Combine(_mount, "folder")).FullName;
            CreateDirectoryLink(Path.Combine(folder, "staging"), _outside);
            SetDenyRule(folder, FileSystemRights.ListDirectory, deny: true);
            try
            {
                Assert.Throws<InvalidOperationException>(() =>
                    PluginFileAccessGuard.EnsureWithinMounts(_container, Path.Combine(folder, "staging", "file.txt"), write: true));
            }
            finally
            {
                SetDenyRule(folder, FileSystemRights.ListDirectory, deny: false);
            }
        }

        private static void SetDenyRule(string directory, FileSystemRights rights, bool deny)
        {
            var info = new DirectoryInfo(directory);
            DirectorySecurity security = info.GetAccessControl();
            var rule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User, rights, AccessControlType.Deny);
            if (deny)
            {
                security.AddAccessRule(rule);
            }
            else
            {
                security.RemoveAccessRule(rule);
            }

            info.SetAccessControl(security);
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
