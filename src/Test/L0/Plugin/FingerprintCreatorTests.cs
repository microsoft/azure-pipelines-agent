﻿using System;
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

        static FingerprintCreatorTests()
        {
            var r = new Random(0);
            content1 = new byte[100 + r.Next(100)]; r.NextBytes(content1);
            content2 = new byte[100 + r.Next(100)]; r.NextBytes(content2);

            path1 = Path.GetTempFileName();
            path2 = Path.GetTempFileName();

            directory = Path.GetDirectoryName(path1);
            Assert.Equal(directory, Path.GetDirectoryName(path2));

            File.WriteAllBytes(path1, content1);
            File.WriteAllBytes(path2, content2);

            var hasher = new SHA256Managed();
            hash1 = hasher.ComputeHash(content1);
            hash2 = hasher.ComputeHash(content2);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void Fingerprint_StartsWithExcludeFails()
        {
            using(var hostContext = new TestHostContext(this))
            {
                var context = new AgentTaskPluginExecutionContext(hostContext.GetTrace());
                var segments = new[]
                {
                    $"!{path1},{path2}"
                };

                Assert.Throws<ArgumentException>(
                    () => FingerprintCreator.EvaluateKeyToFingerprint(context, directory, segments, addWildcard: false)
                );
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void Fingerprint_MultipleIncludeFails()
        {
            using(var hostContext = new TestHostContext(this))
            {
                var context = new AgentTaskPluginExecutionContext(hostContext.GetTrace());
                var segments = new[]
                {
                    $"{path1},{path2}"
                };
                
                Assert.Throws<ArgumentException>(
                    () => FingerprintCreator.EvaluateKeyToFingerprint(context, directory, segments, addWildcard: false)
                );
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void Fingerprint_ReservedFails()
        {
            using(var hostContext = new TestHostContext(this))
            {
                var context = new AgentTaskPluginExecutionContext(hostContext.GetTrace());
                Assert.Throws<ArgumentException>(
                    () => FingerprintCreator.EvaluateKeyToFingerprint(context, directory, new [] {"*"}, addWildcard: false)
                );
                Assert.Throws<ArgumentException>(
                    () => FingerprintCreator.EvaluateKeyToFingerprint(context, directory, new [] {"**"}, addWildcard: false)
                );
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void Fingerprint_ExcludeExactMatches()
        {
            using(var hostContext = new TestHostContext(this))
            {
                var context = new AgentTaskPluginExecutionContext(hostContext.GetTrace());
                var segments = new[]
                {
                    $"{Path.GetDirectoryName(path1)},!{path1}",
                };
                Assert.Throws<FileNotFoundException>(
                    () => FingerprintCreator.EvaluateKeyToFingerprint(context, directory, segments, addWildcard: false)
                );
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void Fingerprint_ExcludeExactMisses()
        {
            using(var hostContext = new TestHostContext(this))
            {
                var context = new AgentTaskPluginExecutionContext(hostContext.GetTrace());
                var segments = new[]
                {
                    $"{path1},!{path2}",
                };
                Fingerprint f = FingerprintCreator.EvaluateKeyToFingerprint(context, directory, segments, addWildcard: false);
                
                Assert.Equal(1, f.Segments.Length);
                Assert.Equal(FingerprintCreator.SummarizeString($"\nSHA256({Path.GetFileName(path1)})=[{content1.Length}]{hash1.ToHex()}"), f.Segments[0]);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void Fingerprint_FileAbsolute()
        {
            using(var hostContext = new TestHostContext(this))
            {
                var context = new AgentTaskPluginExecutionContext(hostContext.GetTrace());
                var segments = new[]
                {
                    $"{path1}",
                    $"{path2}",
                };
                Fingerprint f = FingerprintCreator.EvaluateKeyToFingerprint(context, directory, segments, addWildcard: false);
                
                Assert.Equal(2, f.Segments.Length);
                Assert.Equal(FingerprintCreator.SummarizeString($"\nSHA256({Path.GetFileName(path1)})=[{content1.Length}]{hash1.ToHex()}"), f.Segments[0]);
                Assert.Equal(FingerprintCreator.SummarizeString($"\nSHA256({Path.GetFileName(path2)})=[{content2.Length}]{hash2.ToHex()}"), f.Segments[1]);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void Fingerprint_FileRelative()
        {
            string workingDir = Path.GetDirectoryName(path1);
            string relPath1 = Path.GetFileName(path1);
            string relPath2 = Path.GetFileName(path2);

            using(var hostContext = new TestHostContext(this))
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

                Fingerprint f = FingerprintCreator.EvaluateKeyToFingerprint(context, directory, segments, addWildcard: false);
                
                Assert.Equal(2, f.Segments.Length);
                Assert.Equal(FingerprintCreator.SummarizeString($"\nSHA256({relPath1})=[{content1.Length}]{hash1.ToHex()}"), f.Segments[0]);
                Assert.Equal(FingerprintCreator.SummarizeString($"\nSHA256({relPath2})=[{content2.Length}]{hash2.ToHex()}"), f.Segments[1]);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void Fingerprint_Str()
        {
            using(var hostContext = new TestHostContext(this))
            {
                var context = new AgentTaskPluginExecutionContext(hostContext.GetTrace());
                var segments = new[]
                {
                    $"hello",
                };

                Fingerprint f = FingerprintCreator.EvaluateKeyToFingerprint(context, directory, segments, addWildcard: false);
                
                Assert.Equal(1, f.Segments.Length);
                Assert.Equal($"hello", f.Segments[0]);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void Fingerprint_Wildcard()
        {
            using(var hostContext = new TestHostContext(this))
            {
                var context = new AgentTaskPluginExecutionContext(hostContext.GetTrace());
                var segments = new[]
                {
                    $"hello",
                };

                Fingerprint f = FingerprintCreator.EvaluateKeyToFingerprint(context, directory, segments, addWildcard: true);
                
                Assert.Equal(2, f.Segments.Length);
                Assert.Equal($"hello", f.Segments[0]);
                Assert.Equal(Fingerprint.Wildcard, f.Segments[1]);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void ParseMultilineKeyAsOld()
        {
            (bool isOldFormat, string[] keySegments, IEnumerable<string[]> restoreKeys) = PipelineCacheTaskPluginBase.ParseIntoSegments(
                string.Empty,
                "gems\n$(Agent.OS)\n$(Build.SourcesDirectory)/my.gemspec",
                string.Empty);
            Assert.True(isOldFormat);
            Assert.Equal(new [] {"gems", "$(Agent.OS)", "$(Build.SourcesDirectory)/my.gemspec"}, keySegments);
            Assert.Equal(0, restoreKeys.Count());
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void ParseSingleLineAsNew()
        {
            (bool isOldFormat, string[] keySegments, IEnumerable<string[]> restoreKeys) = PipelineCacheTaskPluginBase.ParseIntoSegments(
                string.Empty,
                "$(Agent.OS)",
                string.Empty);
            Assert.False(isOldFormat);
            Assert.Equal(new [] {"$(Agent.OS)"}, keySegments);
            Assert.Equal(0, restoreKeys.Count());
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void ParseMultilineWithRestoreKeys()
        {
            (bool isOldFormat, string[] keySegments, IEnumerable<string[]> restoreKeys) = PipelineCacheTaskPluginBase.ParseIntoSegments(
                string.Empty,
                "$(Agent.OS) | Gemfile.lock | **/*.gemspec,!./junk/**",
                "$(Agent.OS) | Gemfile.lock\n$(Agent.OS)");
            Assert.False(isOldFormat);
            Assert.Equal(new [] {"$(Agent.OS)","Gemfile.lock","**/*.gemspec,!./junk/**"}, keySegments);
            Assert.Equal(new [] {new []{ "$(Agent.OS)","Gemfile.lock"}, new[] {"$(Agent.OS)"}}, restoreKeys);
        }
    }
}