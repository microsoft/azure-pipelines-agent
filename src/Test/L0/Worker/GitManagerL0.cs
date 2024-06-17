// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Threading;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Moq;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker
{
    public sealed class GitManagerL0
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public async void DownloadAsync()
        {
            using var tokenSource = new CancellationTokenSource();
            using var hostContext = new TestHostContext(this);
            GitManager gitManager = new();
            gitManager.Initialize(hostContext);
            var executionContext = new Mock<IExecutionContext>();
            executionContext.Setup(x => x.CancellationToken).Returns(tokenSource.Token);
            await gitManager.DownloadAsync(executionContext.Object);

            var externalsPath = hostContext.GetDirectory(WellKnownDirectory.Externals);

            Assert.True(Directory.Exists(Path.Combine(externalsPath, "git-2.39.1")));
            Assert.True(File.Exists(Path.Combine(externalsPath, "git-2.39.1", "cmd", "git.exe")));
        }
    }
}
