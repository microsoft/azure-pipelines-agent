using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Agent.Plugins.PipelineCache;
using Agent.Sdk;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.PipelineCache
{
    public class MatchingTests
    {
        private const string WorkingDirectory = "C:\\working";

        private void RunTests(
            string includePattern,
            string[] excludePatterns,
            (string path, bool match)[] testCases,
            [CallerMemberName] string testName = null)
        {
            using(var hostContext = new TestHostContext(this, testName))
            {
                var context = new AgentTaskPluginExecutionContext(hostContext.GetTrace());

                string workingDir = null;
                if(!FingerprintCreator.IsAbsolutePath(includePattern))
                {
                    workingDir = WorkingDirectory;
                }

                includePattern = FingerprintCreator.MakePathAbsolute(workingDir,includePattern);
                excludePatterns = excludePatterns.Select(p => FingerprintCreator.MakePathAbsolute(workingDir,p)).ToArray();
                Func<string,bool> filter = FingerprintCreator.CreateFilter(
                    context,
                    workingDir,
                    includePattern,
                    excludePatterns
                );

                Action<string,bool> assertPath = (path, isMatch) =>
                    Assert.True(isMatch == filter(path), $"filter({path}) should have returned {isMatch}.");

                foreach((string path, bool match) in testCases)
                {
                    assertPath(path, match);
                }
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void ExcludeSingleFile()
        {
            RunTests(
                includePattern: "*.tmp",
                excludePatterns: new [] {"bad.tmp"},
                testCases:new []{
                    ("C:\\working\\good.tmp",true),
                    ("C:\\working\\bad.tmp",false),
                    ("C:\\working\\something.else",false),
                }
            );
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void ExcludeSingleFileWithDot()
        {
            RunTests(
                includePattern: "./*.tmp",
                excludePatterns: new [] {"./bad.tmp"},
                testCases:new []{
                    ("C:\\working\\good.tmp",true),
                    ("C:\\working\\bad.tmp",false),
                    ("C:\\working\\something.else",false),
                }
            );
        }
    }
}