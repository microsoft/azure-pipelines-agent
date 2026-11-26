// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Runtime.CompilerServices;

namespace Microsoft.VisualStudio.Services.Agent.Util
{
    /// <summary>
    /// Checks that a path used by a plugin task on a container step, after resolving links, is
    /// within the container's mounts. Call it right before each read or write.
    /// </summary>
    public static class PluginFileAccessGuard
    {
        private const int MaxLinkDepth = 64;
        private const FileAttributes Missing = (FileAttributes)(-1);

        private static readonly ConditionalWeakTable<ContainerInfo, MountRoots> s_mountRoots = new ConditionalWeakTable<ContainerInfo, MountRoots>();

        private static readonly EnumerationOptions s_listOptions = new EnumerationOptions
        {
            AttributesToSkip = 0,
            IgnoreInaccessible = false,
            MatchType = MatchType.Win32,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false
        };

        public static string EnsureWithinMounts(ContainerInfo container, string path, bool write)
        {
            if (container == null || string.IsNullOrEmpty(path))
            {
                return path;
            }

            return ResolveAndCheck(GetMountRoots(container), path, write, out _);
        }

        public static bool IsUnderOrEqual(string root, string path)
        {
            string rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
            return path.Equals(root, IOUtil.FilePathStringComparison) ||
                path.StartsWith(rootWithSeparator, IOUtil.FilePathStringComparison);
        }

        /// <summary>
        /// Checks a folder, which may not exist yet, and everything under it. Links are checked
        /// but not followed. Use before an external process or library uses the folder.
        /// </summary>
        public static void EnsureTreeWithinMounts(ContainerInfo container, string rootPath, bool write)
        {
            if (container == null || string.IsNullOrEmpty(rootPath))
            {
                return;
            }

            MountRoot[] roots = GetMountRoots(container);
            string resolvedRoot = ResolveAndCheck(roots, rootPath, write, out bool fullyResolved);
            if (Directory.Exists(resolvedRoot))
            {
                DescendAndCheckChildren(roots, resolvedRoot, fullyResolved, write, new HashSet<string>(StringComparer.FromComparison(IOUtil.FilePathStringComparison)));
            }
        }

        /// <summary>
        /// Resolves links in each part of the path, in order, the same way the OS does.
        /// Parts that don't exist yet are kept as-is.
        /// </summary>
        public static string ResolveAllLinks(string path)
        {
            bool fullyResolved = true;
            return ResolveAllLinks(path, 0, null, ref fullyResolved);
        }

        private sealed class MountRoot
        {
            public MountVolume Mount;
            public string SourcePath;
            public string ResolvedPath;
            public string ParentPath;
            public string ResolvedParentPath;
        }

        private sealed class MountRoots
        {
            public MountVolume[] Mounts;
            public string[] SourcePaths;
            public bool[] ReadOnly;
            public MountRoot[] Roots;

            public bool Matches(List<MountVolume> mounts)
            {
                if (mounts.Count != Mounts.Length)
                {
                    return false;
                }

                for (int i = 0; i < Mounts.Length; i++)
                {
                    if (!ReferenceEquals(mounts[i], Mounts[i]) ||
                        !string.Equals(mounts[i].SourceVolumePath, SourcePaths[i], StringComparison.Ordinal) ||
                        mounts[i].ReadOnly != ReadOnly[i])
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        private static MountRoot[] GetMountRoots(ContainerInfo container)
        {
            List<MountVolume> mounts = container.MountVolumes;
            if (s_mountRoots.TryGetValue(container, out MountRoots cached) && cached.Matches(mounts))
            {
                return cached.Roots;
            }

            MountVolume[] snapshot = mounts.ToArray();
            var roots = new List<MountRoot>();
            var sourcePaths = new string[snapshot.Length];
            var readOnly = new bool[snapshot.Length];
            for (int i = 0; i < snapshot.Length; i++)
            {
                MountVolume mount = snapshot[i];
                sourcePaths[i] = mount.SourceVolumePath;
                readOnly[i] = mount.ReadOnly;
                if (string.IsNullOrEmpty(mount.SourceVolumePath) || !Path.IsPathRooted(mount.SourceVolumePath))
                {
                    continue;
                }

                string fullPath = Path.GetFullPath(mount.SourceVolumePath);
                string sourcePath = Path.TrimEndingDirectorySeparator(fullPath);
                string parentPath = Path.GetDirectoryName(sourcePath);
                roots.Add(new MountRoot
                {
                    Mount = mount,
                    SourcePath = sourcePath,
                    ResolvedPath = ResolveAllLinks(fullPath),
                    ParentPath = parentPath,
                    ResolvedParentPath = parentPath == null ? null : ResolveAllLinks(parentPath)
                });
            }

            var result = new MountRoots { Mounts = snapshot, SourcePaths = sourcePaths, ReadOnly = readOnly, Roots = roots.ToArray() };
            s_mountRoots.AddOrUpdate(container, result);
            return result.Roots;
        }

        private static string ResolveAndCheck(MountRoot[] roots, string path, bool write, out bool fullyResolved)
        {
            fullyResolved = true;
            string resolved = ResolveAllLinks(Path.GetFullPath(path), 0, roots, ref fullyResolved);
            CheckWithinMounts(roots, path, resolved, write);
            return resolved;
        }

        private static void CheckWithinMounts(MountRoot[] roots, string path, string resolved, bool write)
        {
            MountRoot mostSpecific = null;
            int mostSpecificLength = -1;
            foreach (MountRoot root in roots)
            {
                if (!IsUnderOrEqual(root.ResolvedPath, resolved))
                {
                    continue;
                }

                // The most specific mount wins; on a tie, the later one wins, like Docker.
                if (root.ResolvedPath.Length >= mostSpecificLength)
                {
                    mostSpecific = root;
                    mostSpecificLength = root.ResolvedPath.Length;
                }
            }

            if (mostSpecific == null)
            {
                throw new InvalidOperationException(
                    $"Path '{path}' resolves to '{resolved}', which is outside the mounts of the container.");
            }

            if (write && mostSpecific.Mount.ReadOnly)
            {
                throw new InvalidOperationException(
                    $"Path '{path}' resolves to '{resolved}', which is inside a read-only mount of the container.");
            }
        }

        private static MountRoot FindOutermostMountContaining(MountRoot[] roots, string path)
        {
            MountRoot outermost = null;
            foreach (MountRoot root in roots)
            {
                if ((outermost == null || root.SourcePath.Length < outermost.SourcePath.Length) && IsUnderOrEqual(root.SourcePath, path))
                {
                    outermost = root;
                }
            }

            return outermost;
        }

        private static string ResolveAllLinks(string path, int depth, MountRoot[] roots, ref bool fullyResolved)
        {
            if (!Path.IsPathRooted(path))
            {
                path = Path.Combine(Directory.GetCurrentDirectory(), path);
            }

            string current;
            MountRoot mount = roots == null ? null : FindOutermostMountContaining(roots, path);
            if (mount?.ParentPath != null)
            {
                current = string.Equals(mount.ParentPath, mount.ResolvedParentPath, StringComparison.Ordinal)
                    ? path.Substring(0, mount.ParentPath.Length)
                    : mount.ResolvedParentPath;
                path = path.Substring(mount.ParentPath.Length);
            }
            else
            {
                current = Path.GetPathRoot(path) ?? string.Empty;
                path = path.Substring(current.Length);
            }

            int missingParts = 0;
            foreach (string segment in path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            {
                if (string.IsNullOrEmpty(segment) || segment == ".") continue;

                if (segment == "..")
                {
                    string parent = Path.GetDirectoryName(current);
                    if (parent != null)
                    {
                        current = parent;
                        if (missingParts > 0)
                        {
                            missingParts--;
                        }
                    }

                    continue;
                }

                current = Path.Combine(current, segment);
                if (missingParts > 0)
                {
                    missingParts++;
                    continue;
                }

                if (!TryGetAttributes(current, out FileAttributes attributes))
                {
                    if (roots != null)
                    {
                        throw new InvalidOperationException(
                            $"Path '{current}' can't be read, so it can't be checked against the mounts of the container.");
                    }

                    continue;
                }

                if (attributes == Missing)
                {
                    missingParts = 1;
                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    current = FollowLink(current, (attributes & FileAttributes.Directory) != 0, depth, roots, ref fullyResolved);
                }
            }

            return current;
        }

        private static string FollowLink(string path, bool isDirectory, int depth, MountRoot[] roots, ref bool fullyResolved)
        {
            string linkTarget;
            try
            {
                linkTarget = GetLinkTarget(path, isDirectory);
            }
            catch (Exception ex)
            {
                if (roots != null)
                {
                    throw new InvalidOperationException(
                        $"Path '{path}' is a link whose target can't be read, so it can't be checked against the mounts of the container.", ex);
                }

                return path;
            }

            if (linkTarget == null)
            {
                return path;
            }

            if (depth < MaxLinkDepth)
            {
                return ResolveAllLinks(linkTarget, depth + 1, roots, ref fullyResolved);
            }

            fullyResolved = false;
            return Path.GetFullPath(linkTarget);
        }

        private static bool TryGetAttributes(string path, out FileAttributes attributes)
        {
            try
            {
                attributes = new FileInfo(path).Attributes;
                return true;
            }
            catch
            {
                attributes = default;
                return false;
            }
        }

        private static string GetLinkTarget(string path, bool isDirectory)
        {
            if (PlatformUtil.RunningOnWindows)
            {
                FileSystemInfo target = isDirectory
                    ? Directory.ResolveLinkTarget(path, returnFinalTarget: true)
                    : File.ResolveLinkTarget(path, returnFinalTarget: true);

                return target?.FullName;
            }

            string linkTarget = new FileInfo(path).LinkTarget;
            if (linkTarget == null || Path.IsPathRooted(linkTarget))
            {
                return linkTarget;
            }

            return Path.Combine(Path.GetDirectoryName(path), linkTarget);
        }

        private static void DescendAndCheckChildren(MountRoot[] roots, string path, bool pathFullyResolved, bool write, HashSet<string> visited)
        {
            if (!visited.Add(path))
            {
                return;
            }

            var entries = new FileSystemEnumerable<(string Path, FileAttributes Attributes)>(
                path,
                (ref FileSystemEntry entry) => (entry.ToSpecifiedFullPath(), entry.Attributes),
                s_listOptions);

            // One level at a time, so each entry is checked before it is entered.
            foreach ((string entry, FileAttributes attributes) in entries)
            {
                if (!pathFullyResolved || (attributes & FileAttributes.ReparsePoint) != 0 || !string.Equals(Path.GetFullPath(entry), entry, StringComparison.Ordinal))
                {
                    ResolveAndDescend(roots, entry, write, visited);
                    continue;
                }

                CheckWithinMounts(roots, entry, entry, write);
                if ((attributes & FileAttributes.Directory) == 0)
                {
                    continue;
                }

                if (!TryGetAttributes(entry, out FileAttributes current))
                {
                    throw new InvalidOperationException(
                        $"Path '{entry}' can't be read, so it can't be checked against the mounts of the container.");
                }

                if (current == Missing)
                {
                    continue;
                }

                if ((current & FileAttributes.ReparsePoint) != 0 || (current & FileAttributes.Directory) == 0)
                {
                    ResolveAndDescend(roots, entry, write, visited);
                    continue;
                }

                DescendAndCheckChildren(roots, entry, true, write, visited);
            }
        }

        private static void ResolveAndDescend(MountRoot[] roots, string entry, bool write, HashSet<string> visited)
        {
            string resolvedEntry = ResolveAndCheck(roots, entry, write, out bool fullyResolved);
            if (Directory.Exists(resolvedEntry))
            {
                DescendAndCheckChildren(roots, resolvedEntry, fullyResolved, write, visited);
            }
        }
    }
}