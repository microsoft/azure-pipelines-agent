// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Agent.Plugins.PipelineCache;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.PipelineCache
{
    public class IsPathyTests
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void Fingerprint_IsPath()
        {
            Action<string, bool> assertPath = (path, isPath) =>
                Assert.True(isPath == FingerprintCreator.IsPathyKeySegment(path), $"IsPathy({path}) should have returned {isPath}.");
            assertPath(@"''", false);
            assertPath(@"Windows_NT", false);
            assertPath(@"README.md", true);
            assertPath(@"This is a sentence.", false);
            assertPath(@"http://xkcd.com.", false);
            assertPath(@"""D:\README.md""", false);
            assertPath(@"D:\README.md", true);
            assertPath(@"D:\src\vsts-agent\_layout\_work\2\s/README.md", true);
            assertPath(@"D:\src\vsts-agent\_layout\_work\2\s/**/README.md", true);
            assertPath(@"/**/README.md,!./junk/**;./azure-pipelines.yml", true);
            assertPath(@"./**,!./.git/**", true);
            assertPath(@"/src/foo", true);
            assertPath(@"src/foo", true);

            // ones we don't feel great about
            assertPath(@"We should go to the store/mall", true);
            assertPath(@"KEY_SALT=5-macos-10.13-stable-x86_64-apple-darwin", true);
            assertPath(@"ruby:2.6.2", true);
        }
    }
}