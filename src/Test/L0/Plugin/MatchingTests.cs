// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Agent.Plugins.PipelineCache;
using Agent.Sdk;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.PipelineCache
{
    public class MatchingTests
    {
        private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        private static readonly string DefaultWorkingDirectory =
            IsWindows
                ? "C:\\working"
                : "/working";

        private static string MakeOSPath(string path)
        {
            if (IsWindows)
            {
                return path;
            }

            path = path.Replace('\\', '/');

            if (path.Length >= 2 && path[1] == ':')
            {
                return path.Substring(2);
            }

            return path;
        }

        private void RunTests(
            string[] includePatterns,
            string[] excludePatterns,
            (string path, bool match)[] testCases,
            [CallerMemberName] string testName = null)
        {
            using (var hostContext = new TestHostContext(this, testName))
            {
                var context = new AgentTaskPluginExecutionContext(hostContext.GetTrace());

                includePatterns = includePatterns
                    .Select(p => MakeOSPath(p))
                    .Select(p => FingerprintCreator.MakePathCanonical(
                                    DefaultWorkingDirectory,
                                    p))
                    .ToArray();
                excludePatterns = excludePatterns
                    .Select(p => MakeOSPath(p))
                    .Select(p => FingerprintCreator.MakePathCanonical(
                                    DefaultWorkingDirectory,
                                    p))
                    .ToArray();
                Func<string, bool> filter = FingerprintCreator.CreateFilter(
                    context,
                    includePatterns,
                    excludePatterns
                );

                Action<string, bool> assertPath = (path, isMatch) =>
                    Assert.True(isMatch == filter(path), $"filter({path}) should have returned {isMatch}.");

                foreach ((string path, bool match) in testCases)
                {
                    assertPath(MakeOSPath(path), match);
                }
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void MultipleIncludes()
        {
            RunTests(
                includePatterns: new[] { "good1.tmp", "good2.tmp" },
                excludePatterns: new string[] { },
                testCases: new[]{
                    ("C:\\working\\good1.tmp",true),
                    ("C:\\working\\good2.tmp",true),
                    ("C:\\working\\something.else",false),
                }
            );
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void AbsoluteAndRelative()
        {
            RunTests(
                includePatterns: new[] { "C:\\working\\good1.tmp", "good2.tmp" },
                excludePatterns: new string[] { },
                testCases: new[]{
                    ("C:\\working\\good1.tmp",true),
                    ("C:\\working\\good2.tmp",true),
                    ("C:\\working\\something.else",false),
                    ("D:\\junk",false),
                }
            );
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void ExcludeSingleFile()
        {
            RunTests(
                includePatterns: new[] { "*.tmp" },
                excludePatterns: new[] { "bad.tmp" },
                testCases: new[]{
                    ("C:\\working\\good.tmp",true),
                    ("C:\\working\\bad.tmp",false),
                    ("C:\\working\\something.else",false),
                }
            );
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void ExcludeSingleFileWithDot()
        {
            RunTests(
                includePatterns: new[] { "./*.tmp" },
                excludePatterns: new[] { "./bad.tmp" },
                testCases: new[]{
                    ("C:\\working\\good.tmp",true),
                    ("C:\\working\\bad.tmp",false),
                    ("C:\\working\\something.else",false),
                }
            );
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void DoubleAsteriskAsPartOfPathSegment()
        {
            RunTests(
                includePatterns: new[] { "./**blah/.tmp" },
                excludePatterns: new[] { "./bad.tmp" },
                testCases: new[]{
                    ("C:\\working\\good.tmp",false),
                    ("C:\\working\\bad.tmp",false),
                    ("C:\\working\\something.else",false),
                }
            );
        }

        private void AssertFileEnumeration(
            string includeGlobPath,
            string expectedEnumerateRootPath,
            string expectedEnumeratePattern,
            SearchOption expectedEnumerateDepth)
        {
            FingerprintCreator.Enumeration e = FingerprintCreator.DetermineFileEnumerationFromGlob(MakeOSPath(includeGlobPath));
            Assert.Equal(MakeOSPath(expectedEnumerateRootPath), e.RootPath);
            Assert.Equal(MakeOSPath(expectedEnumeratePattern), e.Pattern);
            Assert.Equal(expectedEnumerateDepth, e.Depth);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void DetermineFileEnumerationExact()
        {
            AssertFileEnumeration(
                includeGlobPath: @"C:\dir\file.txt",
                expectedEnumerateRootPath: @"C:\dir",
                expectedEnumeratePattern: @"file.txt",
                expectedEnumerateDepth: SearchOption.TopDirectoryOnly);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void DetermineFileEnumerationTopLevel()
        {
            AssertFileEnumeration(
                includeGlobPath: @"C:\dir\*.txt",
                expectedEnumerateRootPath: @"C:\dir",
                expectedEnumeratePattern: @"*",
                expectedEnumerateDepth: SearchOption.TopDirectoryOnly);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void DetermineFileEnumerationRecursive()
        {
            AssertFileEnumeration(
                includeGlobPath: @"C:\dir\**\*.txt",
                expectedEnumerateRootPath: @"C:\dir",
                expectedEnumeratePattern: @"*",
                expectedEnumerateDepth: SearchOption.AllDirectories);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void DetermineFileEnumerationExactFileNameRecursive()
        {
            AssertFileEnumeration(
                includeGlobPath: @"C:\dir\node_modules\**\package-lock.json",
                expectedEnumerateRootPath: @"C:\dir\node_modules",
                expectedEnumeratePattern: @"*",
                expectedEnumerateDepth: SearchOption.AllDirectories);
        }
    }
}