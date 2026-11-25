// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.Services.Agent.Worker.Build;
using Xunit;

namespace Test.L0.Worker.Build
{
    public sealed class ContainedTreeWalkerL0
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void EnumerateFiles_WithoutLinks_ListsAllFilesAndNeverCallsValidator()
        {
            string root = CreateTempRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "sub", "deep"));
                File.WriteAllText(Path.Combine(root, "a.txt"), "a");
                File.WriteAllText(Path.Combine(root, "sub", "b.txt"), "b");
                File.WriteAllText(Path.Combine(root, "sub", "deep", "c.txt"), "c");
                var validated = new List<string>();

                var files = ContainedTreeWalker.EnumerateFiles(root, path => { validated.Add(path); return path; });

                Assert.Equal(new[] { "a.txt", "b.txt", "c.txt" }, files.Select(Path.GetFileName).OrderBy(x => x).ToArray());
                Assert.Empty(validated);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void EnumerateFiles_EmptyDirectory_ReturnsEmpty()
        {
            string root = CreateTempRoot();
            try
            {
                Assert.Empty(ContainedTreeWalker.EnumerateFiles(root, path => path));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void EnumerateFiles_FileSymlink_IsValidatedAndListed()
        {
            string root = CreateTempRoot();
            try
            {
                string target = Path.Combine(root, "real.txt");
                string link = Path.Combine(root, "link.txt");
                File.WriteAllText(target, "x");
                if (!TryCreateSymbolicLink(link, target, isDirectory: false))
                {
                    return;
                }

                var validated = new List<string>();
                var files = ContainedTreeWalker.EnumerateFiles(root, path => { validated.Add(path); return path; });

                Assert.Equal(new[] { "link.txt", "real.txt" }, files.Select(Path.GetFileName).OrderBy(x => x).ToArray());
                Assert.Equal(new[] { link }, validated);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void EnumerateFiles_FileSymlinkRejectedByValidator_Throws()
        {
            string root = CreateTempRoot();
            try
            {
                string target = Path.Combine(root, "real.txt");
                File.WriteAllText(target, "x");
                if (!TryCreateSymbolicLink(Path.Combine(root, "link.txt"), target, isDirectory: false))
                {
                    return;
                }

                Assert.Throws<InvalidOperationException>(() => ContainedTreeWalker.EnumerateFiles(root, path => throw new InvalidOperationException()));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void EnumerateFiles_DirectorySymlinkRejectedByValidator_IsNotDescendedInto()
        {
            string root = CreateTempRoot();
            string outside = CreateTempRoot();
            try
            {
                File.WriteAllText(Path.Combine(outside, "secret.txt"), "secret");
                string link = Path.Combine(root, "linkdir");
                if (!TryCreateSymbolicLink(link, outside, isDirectory: true))
                {
                    return;
                }

                var validated = new List<string>();
                Assert.Throws<InvalidOperationException>(() => ContainedTreeWalker.EnumerateFiles(root, path =>
                {
                    validated.Add(path);
                    throw new InvalidOperationException();
                }));

                // Only the link was validated; nothing beneath it was listed first.
                Assert.Equal(new[] { link }, validated);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
                Directory.Delete(outside, recursive: true);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void EnumerateFiles_DirectorySymlinkAcceptedByValidator_ListsItsFiles()
        {
            string root = CreateTempRoot();
            try
            {
                string real = Path.Combine(root, "real");
                Directory.CreateDirectory(real);
                File.WriteAllText(Path.Combine(real, "b.txt"), "b");
                string link = Path.Combine(root, "linkdir");
                if (!TryCreateSymbolicLink(link, real, isDirectory: true))
                {
                    return;
                }

                var files = ContainedTreeWalker.EnumerateFiles(root, path => path);

                Assert.Contains(Path.Combine(link, "b.txt"), files);
                Assert.Contains(Path.Combine(real, "b.txt"), files);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        private static string CreateTempRoot()
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("D"));
            Directory.CreateDirectory(path);
            return path;
        }

        // Creating symlinks can require elevation on Windows; callers skip the scenario when it is unavailable.
        private static bool TryCreateSymbolicLink(string linkPath, string target, bool isDirectory)
        {
            try
            {
                if (isDirectory)
                {
                    Directory.CreateSymbolicLink(linkPath, target);
                }
                else
                {
                    File.CreateSymbolicLink(linkPath, target);
                }

                return true;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException || ex is PlatformNotSupportedException)
            {
                return false;
            }
        }
    }
}
