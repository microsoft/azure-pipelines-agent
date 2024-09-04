// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Moq;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker
{
    public sealed class TfManagerL0
    {
        private const string VstsomLegacy = "vstsom-legacy";
        private const string TfLegacy = "tf-legacy";

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task DownloadTfLegacyToolsAsync()
        {
            // Arrange
            using var tokenSource = new CancellationTokenSource();
            using var hostContext = new TestHostContext(this);
            var executionContext = new Mock<IExecutionContext>();

            executionContext.Setup(x => x.CancellationToken).Returns(tokenSource.Token);
            executionContext.Setup(x => x.GetVariableValueOrDefault(It.Is<string>(s => s == "Agent.HomeDirectory")))
                .Returns(hostContext.GetDirectory(WellKnownDirectory.Root));

            string externalsPath = hostContext.GetDirectory(WellKnownDirectory.Externals);
            string tfPath = Path.Combine(externalsPath, TfLegacy);
            string vstsomPath = Path.Combine(externalsPath, VstsomLegacy);

            // Act
            await TfManager.DownloadLegacyTfToolsAsync(executionContext.Object);

            // Assert
            Assert.True(Directory.Exists(tfPath));
            Assert.True(File.Exists(Path.Combine(tfPath, "TF.exe")));
            Assert.False(Directory.Exists(Path.Combine(externalsPath, "tf_download_temp")));

            Assert.True(Directory.Exists(vstsomPath));
            Assert.True(File.Exists(Path.Combine(vstsomPath, "TF.exe")));
            Assert.False(Directory.Exists(Path.Combine(externalsPath, "vstsom_download_temp")));

            // Cleanup
            IOUtil.DeleteDirectory(tfPath, CancellationToken.None);
            IOUtil.DeleteDirectory(vstsomPath, CancellationToken.None);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task DownloadAsync_Retries()
        {
            // Arrange
            using var tokenSource = new CancellationTokenSource();
            using var hostContext = new TestHostContext(this);
            var executionContext = new Mock<IExecutionContext>();

            executionContext.Setup(x => x.CancellationToken).Returns(tokenSource.Token);
            executionContext.Setup(x => x.GetVariableValueOrDefault(It.Is<string>(s => s == "Agent.HomeDirectory")))
                .Returns(hostContext.GetDirectory(WellKnownDirectory.Root));

            var retryOptions = new Mock<IRetryOptions>();
            retryOptions.SetupProperty(opt => opt.CurrentCount);
            retryOptions.Setup(opt => opt.ToString()).Throws<Exception>();
            retryOptions.Setup(opt => opt.Limit).Returns(3);

            const string downloadUrl = "https://vstsagenttools.blob.core.windows.net/tools/vstsom/m122_887c6659/vstsom.zip";
            string tempDirectory = Path.Combine(hostContext.GetDirectory(WellKnownDirectory.Externals), "temp-test");
            string extractDirectory = Path.Combine(hostContext.GetDirectory(WellKnownDirectory.Externals), "test");

            // Act
            await TfManager.DownloadAsync(executionContext.Object, downloadUrl, tempDirectory, extractDirectory, retryOptions.Object);

            // Assert
            Assert.False(Directory.Exists(tempDirectory));
            Assert.False(Directory.Exists(extractDirectory));
            retryOptions.VerifySet(opt => opt.CurrentCount = It.IsAny<int>(), Times.Exactly(3));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async Task DownloadAsync_Cancellation()
        {
            // Arrange
            using var tokenSource = new CancellationTokenSource();
            using var hostContext = new TestHostContext(this);
            var executionContext = new Mock<IExecutionContext>();

            executionContext.Setup(x => x.CancellationToken).Returns(tokenSource.Token);
            executionContext.Setup(x => x.GetVariableValueOrDefault(It.Is<string>(s => s == "Agent.HomeDirectory")))
                .Returns(hostContext.GetDirectory(WellKnownDirectory.Root));

            var retryOptions = new Mock<IRetryOptions>();
            retryOptions.SetupProperty(opt => opt.CurrentCount);
            retryOptions.Setup(opt => opt.ToString()).Callback(() => tokenSource.Cancel());
            retryOptions.Setup(opt => opt.Limit).Returns(3);

            const string downloadUrl = "https://vstsagenttools.blob.core.windows.net/tools/vstsom/m122_887c6659/vstsom.zip";
            string tempDirectory = Path.Combine(hostContext.GetDirectory(WellKnownDirectory.Externals), "temp-test");
            string extractDirectory = Path.Combine(hostContext.GetDirectory(WellKnownDirectory.Externals), "test");

            // Act
            await TfManager.DownloadAsync(executionContext.Object, downloadUrl, tempDirectory, extractDirectory, retryOptions.Object);

            // Assert
            Assert.False(Directory.Exists(tempDirectory));
            Assert.False(Directory.Exists(extractDirectory));
            retryOptions.VerifySet(opt => opt.CurrentCount = It.IsAny<int>(), Times.Never());
        }
    }
}
