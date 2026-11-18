// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Microsoft.VisualStudio.Services.Agent.Util
{
    /// <summary>
    /// Checks that a path used by a plugin task on a container step, after resolving links, is
    /// within the container's mounts. Call it right before each read or write.
    /// </summary>
    public static class PluginFileAccessGuard
    {
        private const int MaxLinkDepth = 64;

        public static string EnsureWithinMounts(ContainerInfo container, string path, bool write)
        {
            if (container == null || string.IsNullOrEmpty(path))
            {
                return path;
            }

            string resolved = Canonicalize(path);

            MountVolume mostSpecific = FindMostSpecificMount(container, resolved);

            if (mostSpecific == null)
            {
                throw new InvalidOperationException(
                    $"Path '{path}' resolves to '{resolved}', which is outside the mounts of the container.");
            }

            if (write && mostSpecific.ReadOnly)
            {
                throw new InvalidOperationException(
                    $"Path '{path}' resolves to '{resolved}', which is inside a read-only mount of the container.");
            }

            return resolved;
        }

        private static MountVolume FindMostSpecificMount(ContainerInfo container, string resolvedPath)
        {
            MountVolume best = null;
            int bestLength = -1;

            foreach (MountVolume mount in container.MountVolumes)
            {
                if (string.IsNullOrEmpty(mount.SourceVolumePath) || !Path.IsPathRooted(mount.SourceVolumePath))
                {
                    continue;
                }

                string canonicalRoot = Canonicalize(mount.SourceVolumePath);
                if (!IsUnderOrEqual(canonicalRoot, resolvedPath))
                {
                    continue;
                }

                // The most specific mount wins; on a tie, the later one wins, like Docker.
                if (canonicalRoot.Length >= bestLength)
                {
                    best = mount;
                    bestLength = canonicalRoot.Length;
                }
            }

            return best;
        }

        public static bool IsUnderOrEqual(string root, string path)
        {
            string rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
            return path.Equals(root, IOUtil.FilePathStringComparison) ||
                path.StartsWith(rootWithSeparator, IOUtil.FilePathStringComparison);
        }

        private static string Canonicalize(string path)
        {
            return ResolveAllLinks(Path.GetFullPath(path));
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

            string resolvedRoot = EnsureWithinMounts(container, rootPath, write);
            DescendAndCheckChildren(container, resolvedRoot, write, new HashSet<string>(StringComparer.FromComparison(IOUtil.FilePathStringComparison)));
        }

        private static void DescendAndCheckChildren(ContainerInfo container, string path, bool write, HashSet<string> visited)
        {
            if (!Directory.Exists(path) || !visited.Add(path))
            {
                return;
            }

            // One level at a time, so each entry is checked before it is entered.
            foreach (string entry in Directory.EnumerateFileSystemEntries(path, "*", SearchOption.TopDirectoryOnly))
            {
                string resolvedEntry = EnsureWithinMounts(container, entry, write);
                DescendAndCheckChildren(container, resolvedEntry, write, visited);
            }
        }

        /// <summary>
        /// Resolves links in each part of the path, in order, the same way the OS does.
        /// Parts that don't exist yet are kept as-is.
        /// </summary>
        public static string ResolveAllLinks(string path)
        {
            return ResolveAllLinks(path, 0);
        }

        private static string ResolveAllLinks(string path, int depth)
        {
            if (!Path.IsPathRooted(path))
            {
                path = Path.Combine(Directory.GetCurrentDirectory(), path);
            }

            string root = Path.GetPathRoot(path) ?? string.Empty;
            string relative = path.Substring(root.Length);
            string[] segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            string current = root;

            foreach (string segment in segments)
            {
                if (string.IsNullOrEmpty(segment) || segment == ".") continue;

                if (segment == "..")
                {
                    string parent = Path.GetDirectoryName(current);
                    if (parent != null)
                    {
                        current = parent;
                    }
                    continue;
                }

                current = Path.Combine(current, segment);

                if (!Directory.Exists(current) && !File.Exists(current))
                {
                    continue;
                }

                try
                {
                    string linkTarget = GetLinkTarget(current);

                    if (linkTarget != null)
                    {
                        current = depth < MaxLinkDepth ? ResolveAllLinks(linkTarget, depth + 1) : Path.GetFullPath(linkTarget);
                    }
                }
                catch
                {
                    // Keep the path as-is if the link can't be resolved; the mount check still applies.
                }
            }

            return current;
        }

        private static string GetLinkTarget(string path)
        {
            if (PlatformUtil.RunningOnWindows)
            {
                FileSystemInfo target = Directory.Exists(path)
                    ? Directory.ResolveLinkTarget(path, returnFinalTarget: true)
                    : File.ResolveLinkTarget(path, returnFinalTarget: true);

                return target?.FullName;
            }

            FileSystemInfo link = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
            string linkTarget = link.LinkTarget;
            if (linkTarget == null || Path.IsPathRooted(linkTarget))
            {
                return linkTarget;
            }

            return Path.Combine(Path.GetDirectoryName(path), linkTarget);
        }
    }
}
