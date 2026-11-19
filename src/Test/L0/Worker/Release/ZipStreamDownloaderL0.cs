// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.Release;

using Moq;

using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker.Release
{
    public sealed class ZipStreamDownloaderL0
    {
        private const string _downloadFolderName = "drop";

        private Mock<IExecutionContext> _ec;
        private Variables _variables;
        private ZipStreamDownloader _zipStreamDownloader;

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task DownloadFromStreamShouldWriteNormalEntryToLocalFolder()
        {
            using (TestHostContext hc = Setup())
            {
                await AssertEntryIsWrittenAsync(hc, "ok.txt", "ok.txt");
            }
        }

        [Theory]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        [InlineData("nested/dir/file.txt", "nested/dir/file.txt")]
        [InlineData("with space.txt", "with space.txt")]
        [InlineData("caf\u00E9.txt", "caf\u00E9.txt")]
        [InlineData("\u65E5\u672C\u8A9E.txt", "\u65E5\u672C\u8A9E.txt")]
        [InlineData("./nested/../ok.txt", "ok.txt")]
        [InlineData("../" + _downloadFolderName + "/ok.txt", "ok.txt")]
        public async Task DownloadFromStreamShouldWriteEntriesThatResolveInsideLocalFolder(string entryName, string expectedRelativePath)
        {
            using (TestHostContext hc = Setup())
            {
                await AssertEntryIsWrittenAsync(hc, entryName, expectedRelativePath);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task DownloadFromStreamShouldRejectEntryTraversingOutsideLocalFolder()
        {
            using (TestHostContext hc = Setup())
            {
                string workDirectory = hc.GetDirectory(WellKnownDirectory.Work);
                try
                {
                    string localFolderPath = Path.Combine(workDirectory, _downloadFolderName);

                    await Assert.ThrowsAsync<InvalidOperationException>(
                        () => DownloadSingleEntryAsync(hc, "../other/marker.txt", "marker", localFolderPath));

                    Assert.False(File.Exists(Path.Combine(workDirectory, "other", "marker.txt")));
                }
                finally
                {
                    IOUtil.DeleteDirectory(workDirectory, CancellationToken.None);
                }
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task DownloadFromStreamShouldRejectLookalikeFolderWithSoftHyphen()
        {
            using (TestHostContext hc = Setup())
            {
                await AssertLookalikeFolderIsRejectedAsync(hc, "\u00AD");
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task DownloadFromStreamShouldRejectLookalikeFolderWithZeroWidthSpace()
        {
            using (TestHostContext hc = Setup())
            {
                await AssertLookalikeFolderIsRejectedAsync(hc, "\u200B");
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task DownloadFromStreamShouldRejectLookalikeFolderWithZeroWidthNoBreakSpace()
        {
            using (TestHostContext hc = Setup())
            {
                await AssertLookalikeFolderIsRejectedAsync(hc, "\uFEFF");
            }
        }

        private async Task AssertEntryIsWrittenAsync(TestHostContext hc, string entryName, string expectedRelativePath)
        {
            string workDirectory = hc.GetDirectory(WellKnownDirectory.Work);
            try
            {
                string localFolderPath = Path.Combine(workDirectory, _downloadFolderName);

                int streamsDownloaded = await DownloadSingleEntryAsync(hc, entryName, "expected content", localFolderPath);

                string downloadedFile = Path.Combine(localFolderPath, expectedRelativePath);
                Assert.Equal(1, streamsDownloaded);
                Assert.True(File.Exists(downloadedFile), $"Expected '{downloadedFile}' to be written.");
                Assert.Equal("expected content", File.ReadAllText(downloadedFile));
            }
            finally
            {
                IOUtil.DeleteDirectory(workDirectory, CancellationToken.None);
            }
        }

        private async Task AssertLookalikeFolderIsRejectedAsync(TestHostContext hc, string invisibleCharacter)
        {
            string workDirectory = hc.GetDirectory(WellKnownDirectory.Work);
            try
            {
                string localFolderPath = Path.Combine(workDirectory, _downloadFolderName);

                // Culture-sensitive comparison ignores these characters, which makes this sibling folder look like the download folder.
                string lookalikeFolderName = _downloadFolderName + invisibleCharacter;

                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => DownloadSingleEntryAsync(hc, $"../{lookalikeFolderName}/marker.txt", "marker", localFolderPath));

                Assert.False(File.Exists(Path.Combine(workDirectory, lookalikeFolderName, "marker.txt")));
            }
            finally
            {
                IOUtil.DeleteDirectory(workDirectory, CancellationToken.None);
            }
        }

        private async Task<int> DownloadSingleEntryAsync(TestHostContext hc, string entryName, string content, string localFolderPath)
        {
            // ZipStreamDownloader creates a new file system manager for every download.
            hc.EnqueueInstance<IReleaseFileSystemManager>(new ReleaseFileSystemManager());

            using (MemoryStream zipStream = CreateZipStream(entryName, content))
            {
                return await _zipStreamDownloader.DownloadFromStream(_ec.Object, zipStream, folderWithinStream: string.Empty, relativePathWithinStream: string.Empty, localFolderPath);
            }
        }

        private static MemoryStream CreateZipStream(string entryName, string content)
        {
            var zipStream = new MemoryStream();
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                ZipArchiveEntry entry = archive.CreateEntry(entryName);
                using (Stream entryStream = entry.Open())
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(content);
                    entryStream.Write(bytes, 0, bytes.Length);
                }
            }

            zipStream.Position = 0;
            return zipStream;
        }

        private TestHostContext Setup([CallerMemberName] string name = "")
        {
            TestHostContext hc = new TestHostContext(this, name);

            List<string> warnings;
            _variables = new Variables(hc, new Dictionary<string, VariableValue>(), out warnings);

            _ec = new Mock<IExecutionContext>();
            _ec.Setup(x => x.Variables).Returns(_variables);

            _zipStreamDownloader = new ZipStreamDownloader();
            _zipStreamDownloader.Initialize(hc);

            return hc;
        }
    }
}
