using System;
using System.IO;
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
        
        private static readonly string path1;
        private static readonly string path2;

        static FingerprintCreatorTests()
        {
            var r = new Random(0);
            content1 = new byte[100 + r.Next(100)]; r.NextBytes(content1);
            content2 = new byte[100 + r.Next(100)]; r.NextBytes(content2);

            path1 = Path.GetTempFileName();
            path2 = Path.GetTempFileName();

            File.WriteAllBytes(path1, content1);
            File.WriteAllBytes(path2, content2);

            var hasher = new SHA256Managed();
            hash1 = hasher.ComputeHash(content1);
            hash2 = hasher.ComputeHash(content2);
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
                Fingerprint f = FingerprintCreator.ParseFromYAML(context, segments, addWildcard: false);
                
                Assert.Equal(2, f.Segments.Length);
                Assert.Equal(FingerprintCreator.SummarizeString($"\nSHA256({path1})=[{content1.Length}]{hash1.ToHex()}"), f.Segments[0]);
                Assert.Equal(FingerprintCreator.SummarizeString($"\nSHA256({path2})=[{content2.Length}]{hash2.ToHex()}"), f.Segments[1]);
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

                Fingerprint f = FingerprintCreator.ParseFromYAML(context, segments, addWildcard: false);
                
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

                Fingerprint f = FingerprintCreator.ParseFromYAML(context, segments, addWildcard: false);
                
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

                Fingerprint f = FingerprintCreator.ParseFromYAML(context, segments, addWildcard: true);
                
                Assert.Equal(2, f.Segments.Length);
                Assert.Equal($"hello", f.Segments[0]);
                Assert.Equal(FingerprintCreator.Wildcard, f.Segments[1]);
            }
        }
    }
}