using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agent.Plugins.PipelineArtifact;
using Agent.Sdk;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.Agent.Tests;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;
using Xunit;

namespace Test.L0.Plugin.TestFileShareProviderL0
{
    public class TestFileShareProviderL0
    {
        private const string TestSourceFolder = "testFolder1";
        private const string TestDestFolder = "testFolder2";
        private const string TestMultidownloadSourceFolder = "testDownload";

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task TestPublishArtifactAsync()
        {
            using(var hostContext = new TestHostContext(this))
            {
                var context = new AgentTaskPluginExecutionContext(hostContext.GetTrace());
                context.Variables.Add("system.hosttype", "build");
                var provider = new FileShareProvider(context, new CallbackAppTraceSource(str => context.Output(str), System.Diagnostics.SourceLevels.Information));
                // Get source directory path and destination directory path
                string sourcePath = Path.Combine(Directory.GetCurrentDirectory(), TestSourceFolder);
                string destPath = Path.Combine(Directory.GetCurrentDirectory(), TestDestFolder);
                await provider.PublishArtifactAsync(sourcePath, destPath, 1, CancellationToken.None);
                var sourceFiles = Directory.GetFiles(sourcePath);
                var destFiles = Directory.GetFiles(destPath);
                Assert.Equal(sourceFiles.Length, destFiles.Length);
                foreach(var file in sourceFiles)
                {
                    Assert.True(destFiles.Any(f => Path.GetFileName(f).Equals(Path.GetFileName(file))));
                }
                TestCleanup();
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public async Task TestDownloadArtifactAsync()
        {
            using(var hostContext = new TestHostContext(this))
            {
                var context = new AgentTaskPluginExecutionContext(hostContext.GetTrace());
                var provider = new FileShareProvider(context, new CallbackAppTraceSource(str => context.Output(str), System.Diagnostics.SourceLevels.Information));
                
                string sourcePath = Path.Combine(Directory.GetCurrentDirectory(), TestMultidownloadSourceFolder);
                string destPath = Path.Combine(Directory.GetCurrentDirectory(), TestDestFolder);
                PipelineArtifactDownloadParameters downloadParameters = new PipelineArtifactDownloadParameters();
                downloadParameters.TargetDirectory = destPath;
                downloadParameters.MinimatchFilters = new string[] {"**"};
                BuildArtifact buildArtifact = new BuildArtifact();
                buildArtifact.Name = "drop";
                buildArtifact.Resource = new ArtifactResource();
                buildArtifact.Resource.Data = sourcePath;
                
                await provider.DownloadSingleArtifactAsync(downloadParameters, buildArtifact, CancellationToken.None);
                var sourceFiles = Directory.GetFiles(sourcePath);
                var destFiles = Directory.GetFiles(destPath);

                Assert.Equal(sourceFiles.Length, destFiles.Length);
                foreach(var file in sourceFiles)
                {
                    Assert.True(destFiles.Any(f => Path.GetFileName(f).Equals(Path.GetFileName(file))));
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
    }
}