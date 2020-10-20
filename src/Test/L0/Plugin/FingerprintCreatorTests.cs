// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Agent.Plugins.PipelineCache;
using Agent.Sdk;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using Microsoft.VisualStudio.Services.PipelineCache.WebApi;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.PipelineCache
{
    public class FingerprintCreatorTests
    {
        private static readonly byte[] content1;
        private static readonly byte[] content2;

        private static readonly byte[] hash1;
        private static readonly byte[] hash2;
        private static readonly string directory;
        private static readonly string path1;
        private static readonly string path2;

        private static readonly string workspaceRoot;
        private static readonly string directory1;
        private static readonly string directory2;

        private static readonly string directory1Name;
        private static readonly string directory2Name;

        static FingerprintCreatorTests()
        {
            var r = new Random(0);
            content1 = new byte[100 + r.Next(100)]; r.NextBytes(content1);
            content2 = new byte[100 + r.Next(100)]; r.NextBytes(content2);

            path1 = Path.GetTempFileName();
            path2 = Path.GetTempFileName();

            directory = Path.GetDirectoryName(path1);
            Assert.Equal(directory, Path.GetDirectoryName(path2));

            var workspace = Guid.NewGuid().ToString();

            var path3 = Path.Combine(directory, workspace, Guid.NewGuid().ToString(), Guid.NewGuid().ToString());
            var path4 = Path.Combine(directory, workspace, Guid.NewGuid().ToString(), Guid.NewGuid().ToString());

            directory1 = Path.GetDirectoryName(path3);
            directory2 = Path.GetDirectoryName(path4);

            workspaceRoot = Path.GetDirectoryName(directory1);

            directory1Name = Path.GetFileName(directory1);
            directory2Name = Path.GetFileName(directory2);

            File.WriteAllBytes(path1, content1);
            File.WriteAllBytes(path2, content2);

            Directory.CreateDirectory(directory1);
            Directory.CreateDirectory(directory2);

            File.WriteAllBytes(path3, content1);
            File.WriteAllBytes(path4, content2);

            using (var hasher = new SHA256Managed())
            {
                hash1 = hasher.ComputeHash(content1);
                hash2 = hasher.ComputeHash(content2);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void Fingerprint_ReservedFails()
        {
            using (var hostContext = new TestHostContext(this))
            {
                var context = new AgentTaskPluginExecutionContext(hostContext.GetTrace());
                Assert.Throws<ArgumentException>(
                    () => FingerprintCreator.EvaluateToFingerprint(context, directory, new[] { "*" }, FingerprintType.Key)
                );
                Assert.Throws<ArgumentException>(
                    () => FingerprintCreator.EvaluateToFingerprint(context, directory, new[] { "**" }, FingerprintType.Key)
                );
                Assert.Throws<ArgumentException>(
                    () => FingerprintCreator.EvaluateToFingerprint(context, directory, new[] { "*" }, FingerprintType.Path)
                );
                Assert.Throws<ArgumentException>(
                    () => FingerprintCreator.EvaluateToFingerprint(context, directory, new[] { "**" }, FingerprintType.Path)
                );
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void Fingerprint_Key_ExcludeExactMatches()
        {
            using (var hostContext = new TestHostContext(this))
            {
                var context = new AgentTaskPluginExecutionContext(hostContext.GetTrace());
                var segments = new[]
                {
                    $"{Path.GetDirectoryName(path1)},!{path1}",
                };
                Assert.Throws<AggregateException>(
                    () => FingerprintCreator.EvaluateToFingerprint(context, directory, segments, FingerprintType.Key)
                );
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void Fingerprint_Path_IncludeFullPathMatches()
        {
            using (var hostContext = new TestHostContext(this))
            {
                var context = new AgentTaskPluginExecutionContext(hostContext.GetTrace());
                var segments = new[]
                {
                    directory1,
                    directory2
                };

                Fingerprint f = FingerprintCreator.EvaluateToFingerprint(context, workspaceRoot, segments, FingerprintType.Path);
                Assert.Equal(
                    new[] { directory1Name, directory2Name }.OrderBy(x => x),
                    f.Segments.OrderBy(x => x)
                );
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void Fingerprint_Path_IncludeRelativePathMatches()
        {
            using (var hostContext = new TestHostContext(this))
            {
                var context = new AgentTaskPluginExecutionContext(hostContext.GetTrace());
                var segments = new[]
                {
                    directory1Name,
                    directory2Name
                };

                Fingerprint f = FingerprintCreator.EvaluateToFingerprint(context, workspaceRoot, segments, FingerprintType.Path);
                Assert.Equal(
                    new[] { directory1Name, directory2Name }.OrderBy(x => x),
                    f.Segments.OrderBy(x => x)
                );
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void Fingerprint_Path_IncludeGlobMatches()
        {
            using (var hostContext = new TestHostContext(this))
            {
                var context = new AgentTaskPluginExecutionContext(hostContext.GetTrace());
                var segments = new[]
                {
                    $"**/{directory1Name},**/{directory2Name}",
                };

                Fingerprint f = FingerprintCreator.EvaluateToFingerprint(context, workspaceRoot, segments, FingerprintType.Path);
                Assert.Equal(
                    new[] { directory1Name, directory2Name }.OrderBy(x => x),
                    f.Segments.OrderBy(x => x)
                );
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void Fingerprint_Path_ExcludeGlobMatches()
        {
            using (var hostContext = new TestHostContext(this))
            {
                var context = new AgentTaskPluginExecutionContext(hostContext.GetTrace());
                var segments = new[]
                {
                    $"**/{directory1Name},!**/{directory2Name}",
                };

                Fingerprint f = FingerprintCreator.EvaluateToFingerprint(context, workspaceRoot, segments, FingerprintType.Path);
                Assert.Equal(
                    new[] { directory1Name },
                    f.Segments
                );
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void Fingerprint_Path_NoIncludePatterns()
        {
            using (var hostContext = new TestHostContext(this))
            {
                var context = new AgentTaskPluginExecutionContext(hostContext.GetTrace());
                var segments = new[]
                {
                    $"!**/{directory1Name},!**/{directory2Name}",
                };

                Assert.Throws<ArgumentException>(
                    () => FingerprintCreator.EvaluateToFingerprint(context, workspaceRoot, segments, FingerprintType.Path)
                );
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void Fingerprint_Path_NoMatches()
        {
            using (var hostContext = new TestHostContext(this))
            {
                var context = new AgentTaskPluginExecutionContext(hostContext.GetTrace());
                var segments = new[]
                {
                    $"**/{Guid.NewGuid().ToString()},!**/{directory2Name}"
                };

                // TODO: Should this really be throwing an exception?
                Assert.Throws<AggregateException>(
                    () => FingerprintCreator.EvaluateToFingerprint(context, workspaceRoot, segments, FingerprintType.Path)
                );
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void Fingerprint_Path_SinglePathOutsidePipelineWorkspace()
        {
            using (var hostContext = new TestHostContext(this))
            {
                var context = new AgentTaskPluginExecutionContext(hostContext.GetTrace());
                var directoryInfo = new DirectoryInfo(workspaceRoot);
                var segments = new[]
                {
                    directoryInfo.Parent.FullName,
                };

                Fingerprint f = FingerprintCreator.EvaluateToFingerprint(context, workspaceRoot, segments, FingerprintType.Path);

                Assert.Equal(1, f.Segments.Count());
                Assert.Equal(
                    new[] { Path.GetRelativePath(workspaceRoot, directoryInfo.Parent.FullName) },
                    f.Segments
                );
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void Fingerprint_Path_MultiplePathOutsidePipelineWorkspace()
        {
            using (var hostContext = new TestHostContext(this))
            {
                var context = new AgentTaskPluginExecutionContext(hostContext.GetTrace());
                var directoryInfo = new DirectoryInfo(workspaceRoot);
                var segments = new[]
                {
                    directoryInfo.Parent.FullName,
                    directory1Name,
                };

                Assert.Throws<AggregateException>(
                    () => FingerprintCreator.EvaluateToFingerprint(context, workspaceRoot, segments, FingerprintType.Path)
                );
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void Fingerprint_Path_BacktracedGlobPattern()
        {
            using (var hostContext = new TestHostContext(this))
            {
                var context = new AgentTaskPluginExecutionContext(hostContext.GetTrace());
                var directoryInfo = new DirectoryInfo(workspaceRoot);
                var segments = new[]
                {
                    $"{directoryInfo.Parent.FullName}/*",
                };

                Assert.Throws<AggregateException>(
                    () => FingerprintCreator.EvaluateToFingerprint(context, workspaceRoot, segments, FingerprintType.Path)
                );
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void Fingerprint_Key_ExcludeExactMisses()
        {
            using (var hostContext = new TestHostContext(this))
            {
                var context = new AgentTaskPluginExecutionContext(hostContext.GetTrace());
                var segments = new[]
                {
                    $"{path1},!{path2}",
                };
                Fingerprint f = FingerprintCreator.EvaluateToFingerprint(context, directory, segments, FingerprintType.Key);

                Assert.Equal(1, f.Segments.Length);

                var matchedFile = new FingerprintCreator.MatchedFile(Path.GetFileName(path1), content1.Length, hash1.ToHex());
                Assert.Equal(matchedFile.GetHash(), f.Segments[0]);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void Fingerprint_Key_FileAbsolute()
        {
            using (var hostContext = new TestHostContext(this))
            {
                var context = new AgentTaskPluginExecutionContext(hostContext.GetTrace());
                var segments = new[]
                {
                    $"{path1}",
                    $"{path2}",
                };
                Fingerprint f = FingerprintCreator.EvaluateToFingerprint(context, directory, segments, FingerprintType.Key);

                var file1 = new FingerprintCreator.MatchedFile(Path.GetFileName(path1), content1.Length, hash1.ToHex());
                var file2 = new FingerprintCreator.MatchedFile(Path.GetFileName(path2), content2.Length, hash2.ToHex());

                Assert.Equal(2, f.Segments.Length);
                Assert.Equal(file1.GetHash(), f.Segments[0]);
                Assert.Equal(file2.GetHash(), f.Segments[1]);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void Fingerprint_Key_FileRelative()
        {
            string workingDir = Path.GetDirectoryName(path1);
            string relPath1 = Path.GetFileName(path1);
            string relPath2 = Path.GetFileName(path2);

            using (var hostContext = new TestHostContext(this))
            {
                var context = new AgentTaskPluginExecutionContext(hostContext.GetTrace());
                context.SetVariable(
                    "system.defaultworkingdirectory", // Constants.Variables.System.DefaultWorkingDirectory
                    workingDir,
                    isSecret: false);

                var segments = new[]
                {
                    $"{relPath1}",
                    $"{relPath2}",
                };

                Fingerprint f = FingerprintCreator.EvaluateToFingerprint(context, directory, segments, FingerprintType.Key);

                var file1 = new FingerprintCreator.MatchedFile(relPath1, content1.Length, hash1.ToHex());
                var file2 = new FingerprintCreator.MatchedFile(relPath2, content2.Length, hash2.ToHex());

                Assert.Equal(2, f.Segments.Length);
                Assert.Equal(file1.GetHash(), f.Segments[0]);
                Assert.Equal(file2.GetHash(), f.Segments[1]);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void Fingerprint_Key_Str()
        {
            using (var hostContext = new TestHostContext(this))
            {
                var context = new AgentTaskPluginExecutionContext(hostContext.GetTrace());
                var segments = new[]
                {
                    $"hello",
                };

                Fingerprint f = FingerprintCreator.EvaluateToFingerprint(context, directory, segments, FingerprintType.Key);

                Assert.Equal(1, f.Segments.Length);
                Assert.Equal($"hello", f.Segments[0]);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void ParseMultilineKeyAsOld()
        {
            (bool isOldFormat, string[] keySegments, IEnumerable<string[]> restoreKeys, string[] pathSegments) = PipelineCacheTaskPluginBase.ParseIntoSegments(
                string.Empty,
                "gems\n$(Agent.OS)\n$(Build.SourcesDirectory)/my.gemspec",
                string.Empty,
                string.Empty);
            Assert.True(isOldFormat);
            Assert.Equal(new[] { "gems", "$(Agent.OS)", "$(Build.SourcesDirectory)/my.gemspec" }, keySegments);
            Assert.Equal(0, restoreKeys.Count());
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void ParseSingleLineAsNew()
        {
            (bool isOldFormat, string[] keySegments, IEnumerable<string[]> restoreKeys, string[] pathSegments) = PipelineCacheTaskPluginBase.ParseIntoSegments(
                string.Empty,
                "$(Agent.OS)",
                string.Empty,
                string.Empty);
            Assert.False(isOldFormat);
            Assert.Equal(new[] { "$(Agent.OS)" }, keySegments);
            Assert.Equal(0, restoreKeys.Count());
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void ParseMultilineWithRestoreKeys()
        {
            (bool isOldFormat, string[] keySegments, IEnumerable<string[]> restoreKeys, string[] pathSegments) = PipelineCacheTaskPluginBase.ParseIntoSegments(
                string.Empty,
                "$(Agent.OS) | Gemfile.lock | **/*.gemspec,!./junk/**",
                "$(Agent.OS) | Gemfile.lock\n$(Agent.OS)",
                string.Empty);
            Assert.False(isOldFormat);
            Assert.Equal(new[] { "$(Agent.OS)", "Gemfile.lock", "**/*.gemspec,!./junk/**" }, keySegments);
            Assert.Equal(new[] { new[] { "$(Agent.OS)", "Gemfile.lock" }, new[] { "$(Agent.OS)" } }, restoreKeys);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void ParsePathSegments()
        {
            (bool isOldFormat, string[] keySegments, IEnumerable<string[]> restoreKeys, string[] pathSegments) = PipelineCacheTaskPluginBase.ParseIntoSegments(
                string.Empty,
                string.Empty,
                string.Empty,
                "node_modules | dist | **/globby.*,!**.exclude");
            Assert.False(isOldFormat);
            Assert.Equal(new[] { "node_modules", "dist", "**/globby.*,!**.exclude" }, pathSegments);
        }
    }
}