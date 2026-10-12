// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    /// <summary>
    /// Shared path-resolution/containment helpers used by container path validation.
    /// </summary>
    internal static class ContainerPathUtil
    {
        /// <summary>
        /// Resolves symlinks/junctions segment-by-segment, then applies "..", matching OS
        /// filesystem semantics. Non-existent segments are appended verbatim.
        /// </summary>
        public static string ResolveAllLinks(string path, Action<string> trace = null)
        {
            void Log(string message) => trace?.Invoke(message);

            // Combine (not GetFullPath) so ".." isn't resolved lexically before symlinks.
            if (!Path.IsPathRooted(path))
            {
                string abs = Path.Combine(Directory.GetCurrentDirectory(), path);
                Log($"ResolveAllLinks: relative path '{path}' made absolute '{abs}'");
                path = abs;
            }

            string root = Path.GetPathRoot(path) ?? string.Empty;
            string relative = path.Substring(root.Length);
            string[] segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // Root as-is — Path.Combine handles trailing separators on all platforms.
            string current = root;

            foreach (string segment in segments)
            {
                if (string.IsNullOrEmpty(segment) || segment == ".") continue;

                if (segment == "..")
                {
                    // Move up after resolution, not lexically before.
                    string parent = Path.GetDirectoryName(current);
                    if (parent != null)
                    {
                        Log($"ResolveAllLinks: '..' at '{current}' -> '{parent}'");
                        current = parent;
                    }
                    continue;
                }

                current = Path.Combine(current, segment);

                if (!Directory.Exists(current) && !File.Exists(current))
                {
                    Log($"ResolveAllLinks: '{current}' does not exist, appending verbatim.");
                    continue;
                }

                try
                {
                    FileSystemInfo linkTarget = Directory.Exists(current)
                        ? Directory.ResolveLinkTarget(current, returnFinalTarget: true)
                        : File.ResolveLinkTarget(current, returnFinalTarget: true);

                    if (linkTarget != null)
                    {
                        Log($"ResolveAllLinks: symlink/junction '{current}' -> '{linkTarget.FullName}'");

                        // Recurse so the target is canonicalized the same way as a direct path
                        // (e.g. macOS's /var -> /private/var), not just lexically GetFullPath'd.
                        current = ResolveAllLinks(Path.GetFullPath(linkTarget.FullName), trace);
                    }
                }
                catch (Exception ex)
                {
                    Log($"ResolveAllLinks: ResolveLinkTarget failed for '{current}': {ex.Message}");
                }
            }

            Log($"ResolveAllLinks: result '{current}'");
            return current;
        }

        /// <summary>
        /// True if <paramref name="candidate"/> equals or is nested under <paramref name="root"/>.
        /// Normalizes a trailing separator on root first so paths like "F:\" or "/" still match.
        /// </summary>
        public static bool IsUnderOrEqual(string candidate, string root, StringComparison comparison)
        {
            if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(root))
            {
                return false;
            }

            if (string.Equals(candidate, root, comparison))
            {
                return true;
            }

            string normalizedRoot = (root.EndsWith(Path.DirectorySeparatorChar.ToString()) || root.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
                ? root
                : root + Path.DirectorySeparatorChar;

            return candidate.StartsWith(normalizedRoot, comparison);
        }
    }
}
