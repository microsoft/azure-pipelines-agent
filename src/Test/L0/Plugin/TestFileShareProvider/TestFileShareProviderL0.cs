// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Agent.Plugins;
using Agent.Plugins.PipelineArtifact;
using Agent.Sdk;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.Agent.Blob;
using Microsoft.VisualStudio.Services.Agent.Tests;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    public class TestFileShareProviderL0
    {
        private const string TestSourceFolder = "sourceFolder";
        private const string TestDestFolder = "destFolder";
        private const string TestDownloadSourceFolder = "sourceDownloadFolder";

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task TestPublishArtifactAsync()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Robocopy only works in Windows and since agent is using Xunit, Assert.Inconclusive doesn't exist. 
                return;
            }

            byte[] sourceContent = GenerateRandomData();
            TestFile sourceFile = new TestFile(sourceContent);

            sourceFile.PlaceItem(Path.Combine(Directory.GetCurrentDirectory(), Path.Combine(TestSourceFolder, "test1.txt")));

            using (var hostContext = new TestHostContext(this))
            {
                var context = new AgentTaskPluginExecutionContext(hostContext.GetTrace());
                context.Variables.Add("system.hosttype", "build");
                var provider = new FileShareProvider(context, null, context.CreateArtifactsTracer(), new MockDedupManifestArtifactClientFactory());

                // Get source directory path and destination directory path
                string sourcePath = Path.Combine(Directory.GetCurrentDirectory(), TestSourceFolder);
                string destPath = Path.Combine(Directory.GetCurrentDirectory(), TestDestFolder);
                await provider.PublishArtifactAsync(sourcePath, destPath, 1, CancellationToken.None);
                var sourceFiles = Directory.GetFiles(sourcePath);
                var destFiles = Directory.GetFiles(destPath);
                Assert.Equal(sourceFiles.Length, destFiles.Length);
                foreach (var file in sourceFiles)
                {
                    string destFile = destFiles.FirstOrDefault(f => Path.GetFileName(f).Equals(Path.GetFileName(file)));
                    Assert.True(StructuralComparisons.StructuralEqualityComparer.Equals(ComputeHash(file), ComputeHash(destFile)));
                }
                TestCleanup();
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task TestDownloadArtifactAsync()
        {
            byte[] sourceContent1 = GenerateRandomData();
            byte[] sourceContent2 = GenerateRandomData();
            TestFile sourceFile1 = new TestFile(sourceContent1);
            TestFile sourceFile2 = new TestFile(sourceContent2);

            sourceFile1.PlaceItem(Path.Combine(Directory.GetCurrentDirectory(), Path.Combine(TestDownloadSourceFolder, "drop/test2.txt")));
            sourceFile2.PlaceItem(Path.Combine(Directory.GetCurrentDirectory(), Path.Combine(TestDownloadSourceFolder, "drop/test3.txt")));

            using (var hostContext = new TestHostContext(this))
            {
                var context = new AgentTaskPluginExecutionContext(hostContext.GetTrace());
                var provider = new FileShareProvider(context, null, context.CreateArtifactsTracer(), new MockDedupManifestArtifactClientFactory());

                string sourcePath = Path.Combine(Directory.GetCurrentDirectory(), TestDownloadSourceFolder);
                string destPath = Path.Combine(Directory.GetCurrentDirectory(), TestDestFolder);
                ArtifactDownloadParameters downloadParameters = new ArtifactDownloadParameters();
                downloadParameters.TargetDirectory = destPath;
                downloadParameters.MinimatchFilters = new string[] { "**" };
                BuildArtifact buildArtifact = new BuildArtifact();
                buildArtifact.Name = "drop";
                buildArtifact.Resource = new ArtifactResource();
                buildArtifact.Resource.Data = sourcePath;

                await provider.DownloadMultipleArtifactsAsync(downloadParameters, new List<BuildArtifact> { buildArtifact }, CancellationToken.None, context);
                var sourceFiles = Directory.GetFiles(sourcePath);
                var destFiles = Directory.GetFiles(destPath);

                Assert.Equal(sourceFiles.Length, destFiles.Length);
                foreach (var file in sourceFiles)
                {
                    string destFile = destFiles.FirstOrDefault(f => Path.GetFileName(f).Equals(Path.GetFileName(file)));
                    Assert.True(StructuralComparisons.StructuralEqualityComparer.Equals(ComputeHash(file), ComputeHash(destFile)));
                }
                TestCleanup();
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task TestDownloadArtifactAsyncWithMinimatchPattern()
        {
            byte[] sourceContent1 = GenerateRandomData();
            byte[] sourceContent2 = GenerateRandomData();
            TestFile sourceFile1 = new TestFile(sourceContent1);
            TestFile sourceFile2 = new TestFile(sourceContent2);

            sourceFile1.PlaceItem(Path.Combine(Directory.GetCurrentDirectory(), Path.Combine(TestDownloadSourceFolder, "drop/test2.txt")));
            sourceFile2.PlaceItem(Path.Combine(Directory.GetCurrentDirectory(), Path.Combine(TestDownloadSourceFolder, "drop/test3.txt")));

            using (var hostContext = new TestHostContext(this))
            {
                var context = new AgentTaskPluginExecutionContext(hostContext.GetTrace());
                var provider = new FileShareProvider(context, null, context.CreateArtifactsTracer(), new MockDedupManifestArtifactClientFactory());

                string sourcePath = Path.Combine(Directory.GetCurrentDirectory(), TestDownloadSourceFolder);
                string destPath = Path.Combine(Directory.GetCurrentDirectory(), TestDestFolder);
                ArtifactDownloadParameters downloadParameters = new ArtifactDownloadParameters();
                downloadParameters.TargetDirectory = destPath;
                downloadParameters.MinimatchFilters = new string[] { "drop/test2.txt" };
                BuildArtifact buildArtifact = new BuildArtifact();
                buildArtifact.Name = "drop";
                buildArtifact.Resource = new ArtifactResource();
                buildArtifact.Resource.Data = sourcePath;

                await provider.DownloadMultipleArtifactsAsync(downloadParameters, new List<BuildArtifact> { buildArtifact }, CancellationToken.None, context);
                var sourceFiles = Directory.GetFiles(sourcePath);
                var destFiles = Directory.GetFiles(Path.Combine(destPath, buildArtifact.Name));
                Assert.Equal(1, destFiles.Length);
                foreach (var file in sourceFiles)
                {
                    string destFile = destFiles.FirstOrDefault(f => Path.GetFileName(f).Equals(Path.GetFileName(file)));
                    Assert.True(StructuralComparisons.StructuralEqualityComparer.Equals(ComputeHash(file), ComputeHash(destFile)));
                }
                TestCleanup();
            }
        }

        private void TestCleanup()
        {
            DirectoryInfo destDir = new DirectoryInfo(TestDestFolder);

            foreach (FileInfo file in destDir.GetFiles("*", SearchOption.AllDirectories))
            {
                file.Delete();
            }

            foreach (DirectoryInfo dir in destDir.EnumerateDirectories())
            {
                dir.Delete(true);
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA5394:Do not use insecure randomness")]
        private byte[] GenerateRandomData()
        {
            byte[] data = new byte[1024];
            Random rng = new Random();
            rng.NextBytes(data);
            return data;
        }

        private byte[] ComputeHash(string filePath)
        {
            using (SHA256 sha256Hash = SHA256.Create())
            {
                return sha256Hash.ComputeHash(File.ReadAllBytes(filePath));
            }
        }
    }

    public class TestFile
    {
        public byte[] Content { get; protected set; }
        protected internal TestFile(byte[] content)
        {
            this.Content = content;
        }

        internal void PlaceItem(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // If the file path ends in a directory (empty dir), don't attempt opening a file handle on it.
            if (!string.IsNullOrEmpty(Path.GetFileName(path)))
            {
                File.WriteAllBytes(path, this.Content);
            }
        }
    }
}