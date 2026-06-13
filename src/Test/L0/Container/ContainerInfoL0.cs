// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;
using Xunit;
using Agent.Sdk;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker.Container
{
    public sealed class ContainerInfoL0
    {
        private class MountVolumeTest
        {
            private string Input;
            private MountVolume Expected;
            private string Title;

            public MountVolumeTest(string input, MountVolume expected, string title = "")
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
                new MountVolumeTest("/dst/dir:RO", new MountVolume(null, "/dst/dir", true), "Maps anonymous Docker volume read-only (RO) into target dir"),
                new MountVolumeTest("/dst/dir:rw", new MountVolume(null, "/dst/dir", false), "Maps anonymous Docker volume read-write into target dir"),
                new MountVolumeTest("/dst/dir:RW", new MountVolume(null, "/dst/dir", false), "Maps anonymous Docker volume read-write (RW) into target dir"),
                new MountVolumeTest("/src/dir:/dst/dir:ro", new MountVolume("/src/dir", "/dst/dir", true), "Maps source to read-only target dir"),
                new MountVolumeTest(":/dst/dir", new MountVolume(null, "/dst/dir", false), "Maps anonymous Docker volume into target dir with leading colon"),
                new MountVolumeTest("/c/src/dir:/c/dst/dir", new MountVolume("/c/src/dir", "/c/dst/dir", false), "Maps source to target dir prefixed with /c/"),
                new MountVolumeTest("/src/dir\\:with\\:escaped\\:colons:/dst/dir\\:with\\:escaped\\:colons", new MountVolume("/src/dir:with:escaped:colons", "/dst/dir:with:escaped:colons", false), "Maps source to target dir prefixed with escaped colons"),
                new MountVolumeTest("/src/dir:/dst/dir:rw", new MountVolume("/src/dir", "/dst/dir", false), "Maps source to explicit read-write target dir"),
                new MountVolumeTest("/src/dir:/dst/dir:garbage", new MountVolume("/src/dir", "/dst/dir", false), "Maps source to target dir with bad options"),


                // Windows style paths
                new MountVolumeTest("c:\\dst\\dir", new MountVolume(null, "c:\\dst\\dir", false), "Maps anonymous Docker volume into target dir using Windows-style paths"),
                new MountVolumeTest("c:\\src\\dir:c:\\dst\\dir", new MountVolume("c:\\src\\dir", "c:\\dst\\dir", false), "Maps source to target dir using Windows-style paths"),
                new MountVolumeTest("c:\\dst\\dir:ro", new MountVolume(null, "c:\\dst\\dir", true), "Maps anonymous Docker volume read-only into target dir using Windows-style paths"),
                new MountVolumeTest("c:\\dst\\dir:rw", new MountVolume(null, "c:\\dst\\dir", false), "Maps anonymous Docker volume read-write into target dir using Windows-style paths"),
                new MountVolumeTest("c:\\src\\dir:c:\\dst\\dir:ro", new MountVolume("c:\\src\\dir", "c:\\dst\\dir", true), "Maps source to read-only target dir using Windows-style paths"),
                new MountVolumeTest("c\\:\\src\\dir:c\\:\\dst\\dir:ro", new MountVolume("c:\\src\\dir", "c:\\dst\\dir", true), "Maps source to read-only target dir using Windows-style paths and explicit escape"),
                new MountVolumeTest("c:\\src\\dir:c:\\dst\\dir:rw", new MountVolume("c:\\src\\dir", "c:\\dst\\dir", false), "Maps source to explicit read-write target dir using Windows-style paths"),
                new MountVolumeTest("c:\\src\\dir:c:\\dst\\dir:garbage", new MountVolume("c:\\src\\dir", "c:\\dst\\dir", false), "Maps source to target dir using Windows-style paths with bad options"),

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
                ContainerInfo info = hc.CreateContainerInfo(dockerContainer, isJobContainer: false);
                Assert.True(info.TranslateToContainerPath(hc.GetDirectory(WellKnownDirectory.Tools)).EndsWith($"{Path.DirectorySeparatorChar}__t"), "Tools directory maps");
                Assert.True(info.TranslateToContainerPath(hc.GetDirectory(WellKnownDirectory.Work)).EndsWith($"{Path.DirectorySeparatorChar}__w"), "Work directory maps");
                Assert.True(info.TranslateToContainerPath(hc.GetDirectory(WellKnownDirectory.Root)).EndsWith($"{Path.DirectorySeparatorChar}__a"), "Root directory maps");
            }
        }

        [Theory]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        [MemberData(nameof(TranslateToHostPathTestData))]
        public void TranslateToHostPathTests(string mappingHost, PlatformUtil.OS imageOS, string mappingContainer, string containerPath, string expected)
        {
            var dockerContainer = new Pipelines.ContainerResource()
                {
                    Alias = "vsts_container_preview",
                    Image = "foo",
                };
            using (TestHostContext hc = CreateTestContext())
            {
                ContainerInfo info = hc.CreateContainerInfo(dockerContainer);
                info.ImageOS = imageOS;
                var mappings = new Dictionary<string, string>
                {
                    { mappingHost, mappingContainer }
                };
                info.AddPathMappings(mappings);

                var got = info.TranslateToHostPath(containerPath);

                Assert.Equal(expected, got);
            }
        }
        public static IEnumerable<object[]> TranslateToHostPathTestData()
        {
            string Normalize(string path) => PlatformUtil.RunningOnWindows ? $@"C:\{path.Replace('/', '\\')}" : $"/{path}";

            yield return new object[] { Normalize("agent/w"), PlatformUtil.OS.Windows, @"C:\w", @"C:\w\test", Normalize("agent/w/test") };
            yield return new object[] { Normalize("agent/w"), PlatformUtil.OS.Windows, @"C:\w", @"C:/w/test", Normalize("agent/w/test") };
            yield return new object[] { Normalize("agent/w"), PlatformUtil.OS.Windows, @"C:\w", @"C:\w/test", Normalize("agent/w/test") };
            yield return new object[] { Normalize("agent/w"), PlatformUtil.OS.Linux, @"/w", @"/w/test", Normalize("agent/w/test") };
        }

        [Theory]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        [MemberData(nameof(TranslateToContainerPathTestData))]
        public void TranslateToContainerPathTests(string mappingHost, PlatformUtil.OS imageOS, string mappingContainer, string hostPath, string expected)
        {
            var dockerContainer = new Pipelines.ContainerResource()
                {
                    Alias = "vsts_container_preview",
                    Image = "foo"
                };
            using (TestHostContext hc = CreateTestContext())
            {
                ContainerInfo info = hc.CreateContainerInfo(dockerContainer);
                info.ImageOS = imageOS;
                var mappings = new Dictionary<string, string>
                {
                    { mappingHost, mappingContainer }
                };
                info.AddPathMappings(mappings);

                var got = info.TranslateToContainerPath(hostPath);

                Assert.Equal(expected, got);
            }
        }
        public static IEnumerable<object[]> TranslateToContainerPathTestData()
        {
            string Normalize(string path) => PlatformUtil.RunningOnWindows ? $@"C:\{path.Replace('/', '\\')}" : $"/{path}";

            yield return new object[] { Normalize("agent/w"), PlatformUtil.OS.Windows, @"C:\w", Normalize(@"agent/w/test"), @"C:\w\test" };
            yield return new object[] { Normalize("agent/w"), PlatformUtil.OS.Linux, @"/w", Normalize(@"agent/w/test"), @"/w/test" };
            if (PlatformUtil.RunningOnWindows)
            {
                yield return new object[] { @"C:\agent\w", PlatformUtil.OS.Windows, @"C:\w", @"C:/agent/w/test", @"C:\w\test" };
                yield return new object[] { @"C:\agent\w", PlatformUtil.OS.Windows, @"C:\w", @"C:/agent\w\test", @"C:\w\test" };
            }
        }


        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void TranslateContainerPathForImageOSTestsWindowsToLinux()
        {
            var dockerContainer = new Pipelines.ContainerResource()
            {
                Alias = "vsts_container_preview",
                Image = "foo"
            };
            using (TestHostContext hc = CreateTestContext())
            {
                ContainerInfo info = hc.CreateContainerInfo(dockerContainer, isJobContainer: false);
                info.ImageOS = PlatformUtil.OS.Linux;

                foreach (var test in new string[][] {
                    new string [] { "C:\\path\\for\\linux", "/path/for/linux" },
                    new string [] { "c:\\path\\for\\linux", "/path/for/linux" },
                    new string [] { "D:\\path\\for\\linux", "/path/for/linux" },
                    new string [] { "C:\\", "/" },
                    new string [] { "/path/for/linux", "/path/for/linux" },
                    new string [] { "", "" },
                    new string [] { null, null },
                })
                {
                    var winPath = test[0];
                    var linPath = test[1];
                    var got = info.TranslateContainerPathForImageOS(PlatformUtil.OS.Windows, winPath);
                    Assert.True(string.Equals(got, linPath), $"Converted {winPath} expected {linPath}, got {got}");
                }
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void TranslateContainerPathForImageOSTestsUnixLikeToLinux()
        {
            var dockerContainer = new Pipelines.ContainerResource()
            {
                Alias = "vsts_container_preview",
                Image = "foo"
            };
            using (TestHostContext hc = CreateTestContext())
            {
                ContainerInfo info = hc.CreateContainerInfo(dockerContainer, isJobContainer: false);
                info.ImageOS = PlatformUtil.OS.Linux;

                foreach (var os in new PlatformUtil.OS[] { PlatformUtil.OS.Linux, PlatformUtil.OS.OSX })
                {
                    foreach (var test in new string[][] {
                        new string [] { "/path/for/linux", "/path/for/linux" },
                        new string [] { "/", "/" },
                        new string [] { "", "" },
                        new string [] { null, null },
                    })
                    {
                        var origPath = test[0];
                        var linPath = test[1];
                        var got = info.TranslateContainerPathForImageOS(os, origPath);
                        Assert.True(string.Equals(got, linPath), $"Converted {origPath} expected {linPath}, got {got}");
                    }
                }
            }
        }
        private TestHostContext CreateTestContext([CallerMemberName] string testName = "")
        {
            TestHostContext hc = new TestHostContext(this, testName);
            return hc;
        }
    }
}
