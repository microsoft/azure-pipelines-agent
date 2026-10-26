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
        public void Record_SamplesDistinctPathAndSourceCombinations()
        {
            var accumulator = new VsoPathTranslationTelemetryAccumulator();
            accumulator.Record("/__w/file", "/work/file", "ContainerInfo", true, VsoPathTranslationSource.TaskUploadFile);
            accumulator.Record("/__w/file", "/work/file", "ContainerInfo", true, VsoPathTranslationSource.TaskLogIssueSourcePath);
            accumulator.Record("/__w/file", "/work/file", "ContainerInfo", true, VsoPathTranslationSource.TaskUploadFile);

            var properties = JObject.FromObject(accumulator.ToTelemetryProperties("definition", "build"));
            var samples = properties["PathSamples"].ToList();

            Assert.Equal(3, properties.Value<int>("TotalCalls"));
            Assert.Equal(3, properties.Value<int>("TranslatedCount"));
            Assert.Equal(2, samples.Count);
            Assert.All(samples, sample =>
            {
                Assert.Equal("/__w/file", sample.Value<string>("Before"));
                Assert.Equal("/work/file", sample.Value<string>("After"));
                Assert.Equal(3, sample.Count());
                Assert.Equal(JTokenType.String, sample["TranslationSource"].Type);
                Assert.Null(sample["Sources"]);
            });
            Assert.Contains(nameof(VsoPathTranslationSource.TaskUploadFile), samples.Select(sample => sample.Value<string>("TranslationSource")));
            Assert.Contains(nameof(VsoPathTranslationSource.TaskLogIssueSourcePath), samples.Select(sample => sample.Value<string>("TranslationSource")));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Record_LimitsPathAndSourceCombinationsToTwentySamples()
        {
            var accumulator = new VsoPathTranslationTelemetryAccumulator();
            for (int index = 0; index < 10; index++)
            {
                accumulator.Record($"before-{index}", $"after-{index}", "ContainerInfo", false, VsoPathTranslationSource.TaskLogIssueSourcePath);
                accumulator.Record($"before-{index}", $"after-{index}", "ContainerInfo", false, VsoPathTranslationSource.TaskUploadFile);
            }
            accumulator.Record("before-0", "after-0", "ContainerInfo", false, VsoPathTranslationSource.ArtifactUpload);
            accumulator.Record("overflow", "overflow", "ContainerInfo", false, VsoPathTranslationSource.ArtifactUpload);

            var properties = JObject.FromObject(accumulator.ToTelemetryProperties("definition", "build"));
            var samples = properties["PathSamples"].ToList();
            Assert.Equal(22, properties.Value<int>("TotalCalls"));
            Assert.Equal(21, properties.Value<int>("TranslatedCount"));
            Assert.Equal(20, samples.Count);
            Assert.Equal(10, samples.Select(sample => sample.Value<string>("Before")).Distinct().Count());
            Assert.DoesNotContain(samples, sample => sample.Value<string>("Before") == "overflow");
            Assert.DoesNotContain(samples, sample => sample.Value<string>("TranslationSource") == nameof(VsoPathTranslationSource.ArtifactUpload));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Record_PreservesLegacyNormalizationCountersAndFlagState()
        {
            var accumulator = new VsoPathTranslationTelemetryAccumulator();
            accumulator.Record(null, null, "ContainerInfo", false, VsoPathTranslationSource.TaskLogIssueSourcePath);
            accumulator.Record("", "", "ContainerInfo", true, VsoPathTranslationSource.TaskLogIssueSourcePath);
            accumulator.Record("", "", "ContainerInfo", true, VsoPathTranslationSource.TaskUploadFile);
            accumulator.Record("A", "a", "HostInfo", true, VsoPathTranslationSource.TaskUploadFile);
            accumulator.Record("a", "a", "HostInfo", false, VsoPathTranslationSource.TaskUploadFile);

            var properties = JObject.FromObject(accumulator.ToTelemetryProperties("definition", "build"));
            var samples = properties["PathSamples"].ToList();
            var empty = samples.Where(sample => sample.Value<string>("Before") == "").ToList();

            Assert.Equal(5, properties.Value<int>("TotalCalls"));
            Assert.Equal(0, properties.Value<int>("TranslatedCount"));
            Assert.False(properties.Value<bool>("ValidationEnabled"));
            Assert.Equal(4, samples.Count);
            Assert.Equal(2, empty.Count);
            Assert.All(empty, sample => Assert.Equal("", sample.Value<string>("After")));
            Assert.Contains(nameof(VsoPathTranslationSource.TaskLogIssueSourcePath), empty.Select(sample => sample.Value<string>("TranslationSource")));
            Assert.Contains(nameof(VsoPathTranslationSource.TaskUploadFile), empty.Select(sample => sample.Value<string>("TranslationSource")));
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
            Assert.Equal(nameof(VsoPathTranslationSource.TaskLogIssueSourcePath),
                Assert.Single(original["PathSamples"]).Value<string>("TranslationSource"));
            Assert.Equal(2, current["PathSamples"].Count());
            Assert.Contains(nameof(VsoPathTranslationSource.TaskUploadFile),
                current["PathSamples"].Select(sample => sample.Value<string>("TranslationSource")));
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
            Assert.Equal(20, properties["PathSamples"].Select(sample => (
                sample.Value<string>("Before"),
                sample.Value<string>("After"),
                sample.Value<string>("TranslationSource"))).Distinct().Count());
            Assert.All(properties["PathSamples"], sample => Assert.Contains(sample.Value<string>("TranslationSource"),
                new[] { nameof(VsoPathTranslationSource.TaskUploadFile), nameof(VsoPathTranslationSource.TaskLogIssueSourcePath) }));
        }
    }
}
