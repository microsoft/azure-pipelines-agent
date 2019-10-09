using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.Services.Agent.Worker.Container;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker.Container
{
    public sealed class ContainerInfoL0
    {
        private class MountVolumeTest
        {
            private string Input;
            private MountVolume Expected;
            private string Title;

            public MountVolumeTest(string input, MountVolume expected, string title="")
            {
                this.Input = input;
                this.Expected = expected;
                this.Title = title;
            }

            public void run()
            {
                MountVolume got = new MountVolume(Input);
                Assert.True(Expected.SourceVolumePath == got.SourceVolumePath, $"{Title} - testing property SourceVolumePath. Expected: '{Expected.SourceVolumePath}' Got: '{got.SourceVolumePath}' ");
                Assert.True(Expected.TargetVolumePath == got.TargetVolumePath, $"{Title} - testing property TargetVolumePath. Expected: '{Expected.TargetVolumePath}' Got: '{got.TargetVolumePath}'");
                Assert.True(Expected.ReadOnly == got.ReadOnly, $"{Title} - testing property ReadOnly. Expected: '{Expected.ReadOnly}' Got: '{got.ReadOnly}'");
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void MountVolumeConstructorParsesStringInput()
        {
            List<MountVolumeTest> tests = new List<MountVolumeTest>
            {
                // Unix style paths
                new MountVolumeTest("/dst/dir", new MountVolume(null, "/dst/dir", false), "Maps anonymous Docker volume into target dir"), 
                new MountVolumeTest("/src/dir:/dst/dir", new MountVolume("/src/dir", "/dst/dir", false), "Maps source to target dir"), 
                new MountVolumeTest("/dst/dir:ro", new MountVolume(null, "/dst/dir", true), "Maps anonymous Docker volume read-only into target dir"), 
                new MountVolumeTest("/src/dir:/dst/dir:ro", new MountVolume("/src/dir", "/dst/dir", true), "Maps source to read-only target dir"), 
                new MountVolumeTest(":/dst/dir", new MountVolume(null, "/dst/dir", false), "Maps anonymous Docker volume into target dir with leading colon"), 
                new MountVolumeTest("/c/src/dir:/c/dst/dir", new MountVolume("/c/src/dir", "/c/dst/dir", false), "Maps source to target dir prefixed with /c/"), 
                new MountVolumeTest("/src/dir\\:with\\:escaped\\:colons:/dst/dir\\:with\\:escaped\\:colons", new MountVolume("/src/dir:with:escaped:colons", "/dst/dir:with:escaped:colons", false), "Maps source to target dir prefixed with escaped colons"), 

                // Windows style paths
                new MountVolumeTest("c:\\dst\\dir", new MountVolume(null, "c:\\dst\\dir", false), "Maps anonymous Docker volume into target dir using Windows-style paths"), 
                new MountVolumeTest("c:\\src\\dir:c:\\dst\\dir", new MountVolume("c:\\src\\dir", "c:\\dst\\dir", false), "Maps source to target dir using Windows-style paths"), 
                new MountVolumeTest("c:\\dst\\dir:ro", new MountVolume(null, "c:\\dst\\dir", true), "Maps anonymous Docker volume read-only into target dir using Windows-style paths"), 
                new MountVolumeTest("c:\\src\\dir:c:\\dst\\dir:ro", new MountVolume("c:\\src\\dir", "c:\\dst\\dir", true), "Maps source to read-only target dir using Windows-style paths"), 
                new MountVolumeTest("c\\:\\src\\dir:c\\:\\dst\\dir:ro", new MountVolume("c:\\src\\dir", "c:\\dst\\dir", true), "Maps source to read-only target dir using Windows-style paths and explicit escape"), 
            };

            foreach (var test in tests)
            {
                test.run();
            }
        
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void DefaultContainerInfoMappings()
        {
            var dockerContainer = new Pipelines.ContainerResource()
                {
                    Alias = "vsts_container_preview",
                    Image = "foo"
                };
            using (TestHostContext hc = CreateTestContext())
            {
                ContainerInfo info = new ContainerInfo(hc, dockerContainer);
                Assert.True(info.TranslateToContainerPath(hc.GetDirectory(WellKnownDirectory.Tools)).EndsWith($"{Path.DirectorySeparatorChar}__t"), "Tools directory maps");
                Assert.True(info.TranslateToContainerPath(hc.GetDirectory(WellKnownDirectory.Work)).EndsWith($"{Path.DirectorySeparatorChar}__w"), "Work directory maps");
                Assert.True(info.TranslateToContainerPath(hc.GetDirectory(WellKnownDirectory.Root)).EndsWith($"{Path.DirectorySeparatorChar}__a"), "Root directory maps");
            }
        }

        private TestHostContext CreateTestContext([CallerMemberName] string testName = "")
        {
            TestHostContext hc = new TestHostContext(this, testName);
            return hc;
        }
    }
}
