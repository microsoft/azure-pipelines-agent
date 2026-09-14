// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.Telemetry;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker.Telemetry
{
    public sealed class VsoPathTranslationTelemetryAccumulatorL0
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Record_PreservesDistinctSourcesForTheSamePair()
        {
            var accumulator = new VsoPathTranslationTelemetryAccumulator();
            accumulator.Record("/__w/file", "/work/file", "ContainerInfo", true, VsoPathTranslationSource.TaskUploadFile);
            accumulator.Record("/__w/file", "/work/file", "ContainerInfo", true, VsoPathTranslationSource.TaskLogIssueSourcePath);
            accumulator.Record("/__w/file", "/work/file", "ContainerInfo", true, VsoPathTranslationSource.TaskUploadFile);

            var properties = JObject.FromObject(accumulator.ToTelemetryProperties("definition", "build"));
            var sample = Assert.Single(properties["PathSamples"]);

            Assert.Equal(3, properties.Value<int>("TotalCalls"));
            Assert.Equal(3, properties.Value<int>("TranslatedCount"));
            Assert.Equal("/__w/file", sample.Value<string>("Before"));
            Assert.Equal("/work/file", sample.Value<string>("After"));
            Assert.Equal(2, sample["Sources"].Count());
            Assert.Contains(nameof(VsoPathTranslationSource.TaskUploadFile), sample["Sources"].Values<string>());
            Assert.Contains(nameof(VsoPathTranslationSource.TaskLogIssueSourcePath), sample["Sources"].Values<string>());
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Record_UpdatesExistingSourcesWithoutExceedingThePairLimit()
        {
            var accumulator = new VsoPathTranslationTelemetryAccumulator();
            for (int index = 0; index < 20; index++)
            {
                accumulator.Record($"before-{index}", $"after-{index}", "ContainerInfo", false, VsoPathTranslationSource.TaskLogIssueSourcePath);
            }
            accumulator.Record("before-0", "after-0", "ContainerInfo", false, VsoPathTranslationSource.TaskUploadFile);
            accumulator.Record("overflow", "overflow", "ContainerInfo", false, VsoPathTranslationSource.ArtifactUpload);

            var properties = JObject.FromObject(accumulator.ToTelemetryProperties("definition", "build"));
            var samples = properties["PathSamples"].ToList();
            var first = samples.Single(sample => sample.Value<string>("Before") == "before-0");

            Assert.Equal(22, properties.Value<int>("TotalCalls"));
            Assert.Equal(20, samples.Count);
            Assert.DoesNotContain(samples, sample => sample.Value<string>("Before") == "overflow");
            Assert.Contains(nameof(VsoPathTranslationSource.TaskUploadFile), first["Sources"].Values<string>());
            Assert.Contains(nameof(VsoPathTranslationSource.TaskLogIssueSourcePath), first["Sources"].Values<string>());
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Record_PreservesLegacyNormalizationCountersAndFlagState()
        {
            var accumulator = new VsoPathTranslationTelemetryAccumulator();
            accumulator.Record(null, null, "ContainerInfo", false, VsoPathTranslationSource.TaskLogIssueSourcePath);
            accumulator.Record("", "", "ContainerInfo", true, VsoPathTranslationSource.TaskUploadFile);
            accumulator.Record("A", "a", "HostInfo", true, VsoPathTranslationSource.TaskUploadFile);
            accumulator.Record("a", "a", "HostInfo", false, VsoPathTranslationSource.TaskLogIssueSourcePath);

            var properties = JObject.FromObject(accumulator.ToTelemetryProperties("definition", "build"));
            var samples = properties["PathSamples"].ToList();
            var empty = samples.Single(sample => sample.Value<string>("Before") == "");

            Assert.Equal(4, properties.Value<int>("TotalCalls"));
            Assert.Equal(0, properties.Value<int>("TranslatedCount"));
            Assert.False(properties.Value<bool>("ValidationEnabled"));
            Assert.Equal(3, samples.Count);
            Assert.Equal(2, empty["Sources"].Count());
            Assert.Equal("", empty.Value<string>("After"));
            Assert.Equal("definition", properties.Value<string>("DefinitionId"));
            Assert.Equal("build", properties.Value<string>("BuildId"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void ToTelemetryProperties_ReturnsAnIndependentSourceSnapshot()
        {
            var accumulator = new VsoPathTranslationTelemetryAccumulator();
            accumulator.Record("file", "file", "ContainerInfo", false, VsoPathTranslationSource.TaskLogIssueSourcePath);
            var snapshot = accumulator.ToTelemetryProperties("definition", "build");

            accumulator.Record("file", "file", "ContainerInfo", true, VsoPathTranslationSource.TaskUploadFile);

            var original = JObject.FromObject(snapshot);
            var current = JObject.FromObject(accumulator.ToTelemetryProperties("definition", "build"));
            Assert.Single(original["PathSamples"].Single()["Sources"]);
            Assert.Equal(2, current["PathSamples"].Single()["Sources"].Count());
            Assert.Equal(1, original.Value<int>("TotalCalls"));
            Assert.False(original.Value<bool>("ValidationEnabled"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Record_ConcurrentCallsPreserveCountsSourcesAndBounds()
        {
            var accumulator = new VsoPathTranslationTelemetryAccumulator();
            Parallel.For(0, 1000, index =>
            {
                var source = index % 2 == 0
                    ? VsoPathTranslationSource.TaskUploadFile
                    : VsoPathTranslationSource.TaskLogIssueSourcePath;
                accumulator.Record($"before-{index % 25}", $"after-{index % 25}", "ContainerInfo", false, source);
            });

            var properties = JObject.FromObject(accumulator.ToTelemetryProperties("definition", "build"));
            Assert.Equal(1000, properties.Value<int>("TotalCalls"));
            Assert.Equal(1000, properties.Value<int>("TranslatedCount"));
            Assert.Equal(20, properties["PathSamples"].Count());
            Assert.All(properties["PathSamples"], sample => Assert.Equal(2, sample["Sources"].Count()));
        }
    }
}
