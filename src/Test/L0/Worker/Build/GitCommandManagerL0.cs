using Agent.Sdk;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.Build;
using Moq;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker.Build;

public class TestGitCommandManagerL0
{
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
