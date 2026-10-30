using Agent.Sdk;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.Build;
using Microsoft.VisualStudio.Services.Agent.Util;
using Moq;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker.Build;

public class TestGitCommandManagerL0
{
    [Theory]
    [InlineData("https://example.com/project/_git/repo", 0, true)]
    [InlineData("https://user@dev.azure.com/org/Fen%C3%AAtre%20-%20Project/_git/repo", 0, true)]
    [InlineData("https://dev.azure.com/org/Fen%C3%AAtre%20-%20Project/_git/repo", 0, true)]
    [InlineData("https://EXAMPLE.com:443/repo", 0, true)]
    [InlineData("https://example.com/a%20b", 0, true)]
    [InlineData("https://example.com/a%ZZ", 0, false)]
    [InlineData("https://example.com/a b", 0, false)]
    [InlineData("https://example.com/a\\b", 0, false)]
    [InlineData("/absolute/path", 0, false)]
    [InlineData("relative/path", 0, false)]
    [InlineData("https://[invalid", 0, false)]
    [InlineData("", 0, false)]
    [InlineData("https://example.com/one\nhttps://example.com/two", 0, false)]
    [InlineData("https://example.com/project/_git/repo", 1, false)]
    [Trait("Level", "L0")]
    [Trait("Category", "Worker")]
    public async Task GitGetFetchUrlParsesAbsoluteOrigins(string output, int exitCode, bool expectedValid)
    {
        using var hostContext = new TestHostContext(this);
        var context = new Mock<IExecutionContext>();
        context.Setup(x => x.Variables)
            .Returns(new Variables(hostContext, new Dictionary<string, VariableValue>(), out _));
        var processInvoker = new Mock<IProcessInvoker>();
        processInvoker.Setup(x => x.ExecuteAsync(
            It.IsAny<string>(), It.IsAny<string>(), "config --get remote.origin.url",
            It.IsAny<IDictionary<string, string>>(), false, It.IsAny<Encoding>(), It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                foreach (string line in output.Split('\n'))
                {
                    processInvoker.Raise(x => x.OutputDataReceived += null, processInvoker.Object, new ProcessDataReceivedEventArgs(line));
                }
            })
            .ReturnsAsync(exitCode);
        hostContext.EnqueueInstance(processInvoker.Object);
        var manager = new GitCommandManager();
        manager.Initialize(hostContext);

        var actual = await manager.GitGetFetchUrl(context.Object, "repositoryPath");

        if (expectedValid)
        {
            Assert.Equal(new Uri(output, UriKind.Absolute), actual);
        }
        else
        {
            Assert.Null(actual);
        }
    }

    [Fact]
    [Trait("Level", "L0")]
    [Trait("Category", "Worker")]
    [Trait("SkipOn", "darwin")]
    [Trait("SkipOn", "linux")]
    public void TestGetInternalGitPaths()
    {
        using var tc = new TestHostContext(this);
        var trace = tc.GetTrace();
        var executionContext = new Mock<IExecutionContext>();

        GitCommandManager gitCliManager = new();
        gitCliManager.Initialize(tc);
        var (resolvedGitPath, resolvedGitLfsPath) = gitCliManager.GetInternalGitPaths();

        string gitPath = Path.Combine(tc.GetDirectory(WellKnownDirectory.Externals), "git", "cmd", "git.exe");

        var binPath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
        var rootPath = new DirectoryInfo(binPath).Parent.FullName;
        var externalsDirectoryPath = Path.Combine(rootPath, Constants.Path.ExternalsDirectory);
        string gitLfsPath;

        if (PlatformUtil.BuiltOnX86)
        {
            gitLfsPath = Path.Combine(externalsDirectoryPath, "git", "mingw32", "bin", $"git-lfs.exe");
        }
        else
        {
            gitLfsPath = Path.Combine(externalsDirectoryPath, "git", "mingw64", "bin", $"git-lfs.exe");
        }

        Assert.Equal(resolvedGitPath, gitPath);
        Assert.Equal(resolvedGitLfsPath, gitLfsPath);
    }
}
