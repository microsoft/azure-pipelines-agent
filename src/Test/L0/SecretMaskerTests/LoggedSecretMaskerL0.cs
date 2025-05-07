// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk.SecretMasking;
using Microsoft.Security.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using ISecretMasker = Microsoft.TeamFoundation.DistributedTask.Logging.ISecretMasker;
using VsoSecretMasker = Microsoft.TeamFoundation.DistributedTask.Logging.SecretMasker;

namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    public class OssLoggedSecretMaskerL0 : LoggedSecretMaskerL0
    {
        private readonly ITestOutputHelper _output;

        public OssLoggedSecretMaskerL0(ITestOutputHelper output)
        {
            _output = output;
        }

        protected override ISecretMasker CreateSecretMasker()
        {
            return new OssSecretMasker();
        }

        [Theory]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(OssSecretMasker.MaxTelemetryDetections - 1)]
        [InlineData(OssSecretMasker.MaxTelemetryDetections)]
        [InlineData(OssSecretMasker.MaxTelemetryDetections + 1)]
        [InlineData(2 * OssSecretMasker.MaxTelemetryDetections - 1)]
        [InlineData(2 * OssSecretMasker.MaxTelemetryDetections)]
        [InlineData(2 * OssSecretMasker.MaxTelemetryDetections + 1)]
        public void OssLoggedSecretMasker_TelemetryEnabled_SendsTelemetry(int uniqueCorrelatingIds)
        {
            var pattern = new RegexPattern(id: "TEST001/001",
                                           name: "TestPattern",
                                           label: "a test",
                                           DetectionMetadata.HighEntropy,
                                           pattern: "TEST[0-9]+");

            using var ossMasker = new OssSecretMasker(new[] { pattern });
            using var lsm = new LoggedSecretMasker(ossMasker);
            lsm.EnableTelemetry();

            int charsScanned = 0;
            int stringsScanned = 0;
            int totalDetections = 0;
            var correlatingIds = new string[uniqueCorrelatingIds];

            for (int i = 0; i < uniqueCorrelatingIds; i++)
            {
                string inputWithSecret = $"Hello TEST{i} World!";
                lsm.MaskSecrets(inputWithSecret);
                lsm.MaskSecrets(inputWithSecret + "x");

                string inputWithoutSecret = "Nothing to see here";
                lsm.MaskSecrets(inputWithoutSecret);

                correlatingIds[i] = RegexPattern.GenerateCrossCompanyCorrelatingId($"TEST{i}");
                stringsScanned += 3;
                charsScanned += 2 * inputWithSecret.Length + 1 + inputWithoutSecret.Length;
                totalDetections += 2;
            }

            var correlatingIdsToObserve = new HashSet<string>(correlatingIds);

            var telemetry = new List<(string Feature, Dictionary<string, string> Data)>();
            lsm.PublishTelemetry((feature, data) =>
            {
                _output.WriteLine($"Telemetry Event Received: {feature}");
                _output.WriteLine($"Properties: ({data.Count}):");

                foreach (var (key, value) in data)
                {
                    _output.WriteLine($"    {key}: {value}");
                }

                _output.WriteLine("");

                telemetry.Add((feature, data));
            });

            int remainder = uniqueCorrelatingIds % OssSecretMasker.MaxDetectionsPerTelemetryEvent;
            int expectedDetectionEvents = (uniqueCorrelatingIds / OssSecretMasker.MaxDetectionsPerTelemetryEvent) + (remainder == 0 ? 0 : 1);

            bool maxEventsExceeded = expectedDetectionEvents > OssSecretMasker.MaxTelemetryDetectionEvents;
            if (maxEventsExceeded)
            {
                expectedDetectionEvents = OssSecretMasker.MaxTelemetryDetectionEvents;
            }

            int expectedEvents = expectedDetectionEvents + 1;

            Assert.Equal(expectedEvents, telemetry.Count);

            Dictionary<string, string> mergedDetectionData = new Dictionary<string, string>();

            for (int i = 0; i < expectedDetectionEvents; i++)
            {
                var detectionTelemetry = telemetry[i];
                var detectionData = detectionTelemetry.Data;

                Assert.Equal(detectionTelemetry.Feature, "SecretMaskerDetections");

                if (maxEventsExceeded || remainder == 0 || i < expectedDetectionEvents - 1)
                {
                    Assert.Equal(OssSecretMasker.MaxDetectionsPerTelemetryEvent, detectionData.Count);
                }
                else
                {
                    Assert.Equal(remainder, detectionData.Count);
                }

                foreach (var (key, value) in detectionData)
                {
                    Assert.True(correlatingIdsToObserve.Remove(key));
                    Assert.Equal("TEST001/001.TestPattern", value);
                }
            }

            if (maxEventsExceeded)
            {
                Assert.Equal(uniqueCorrelatingIds - OssSecretMasker.MaxTelemetryDetections, correlatingIdsToObserve.Count);
            }
            else
            {
                Assert.Equal(0, correlatingIdsToObserve.Count);
            }

            var overallTelemetry = telemetry[telemetry.Count - 1];
            var overallData = overallTelemetry.Data;
            Assert.Equal(overallTelemetry.Feature, "SecretMasker");
            Assert.Equal(SecretMasker.Version.ToString(), overallData["Version"]);
            Assert.Equal(charsScanned.ToString(CultureInfo.InvariantCulture), overallData["CharsScanned"]);
            Assert.Equal(stringsScanned.ToString(CultureInfo.InvariantCulture), overallData["StringsScanned"]);
            Assert.Equal(uniqueCorrelatingIds.ToString(CultureInfo.InvariantCulture), overallData["UniqueCorrelatingIds"]);
            Assert.True(0.0 <= double.Parse(overallData["ElapsedMaskingTimeInMilliseconds"], CultureInfo.InvariantCulture));

            if (maxEventsExceeded)
            {
                Assert.Equal("true", overallData["DetectionDataIsIncomplete"]);
            }
        }
    }

    public class VsoLoggedSecretMaskerL0 : LoggedSecretMaskerL0
    {
        protected override ISecretMasker CreateSecretMasker()
        {
            return new VsoSecretMasker();
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void VsoLoggedSecretMasker_TelemetryEnabled_Ignored()
        {
            using var lsm = new LoggedSecretMasker(CreateSecretMasker());
            lsm.EnableTelemetry(); // no-op
            lsm.PublishTelemetry((_, _) => Assert.True(false, "This should not be called."));
        }
    }

    public abstract class LoggedSecretMaskerL0
    {
        protected abstract ISecretMasker CreateSecretMasker();

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void LoggedSecretMasker_TelemetryDisabled_DoesNotPublish()
        {
            using var lsm = new LoggedSecretMasker(CreateSecretMasker());
            lsm.PublishTelemetry((_, _) => Assert.True(false, "This should not be called."));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void LoggedSecretMasker_MaskingSecrets()
        {
            using var lsm = new LoggedSecretMasker(CreateSecretMasker())
            {
                MinSecretLength = 0
            };
            var inputMessage = "123";

            lsm.AddValue("1");
            var resultMessage = lsm.MaskSecrets(inputMessage);

            Assert.Equal("***23", resultMessage);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void LoggedSecretMasker_ShortSecret_Removes_From_Dictionary()
        {
            using var lsm = new LoggedSecretMasker(CreateSecretMasker())
            {
                MinSecretLength = 0
            };
            var inputMessage = "123";

            lsm.AddValue("1");
            lsm.MinSecretLength = 4;
            lsm.RemoveShortSecretsFromDictionary();
            var resultMessage = lsm.MaskSecrets(inputMessage);

            Assert.Equal(inputMessage, resultMessage);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void LoggedSecretMasker_ShortSecret_Removes_From_Dictionary_BoundaryValue()
        {
            using var lsm = new LoggedSecretMasker(CreateSecretMasker())
            {
                MinSecretLength = LoggedSecretMasker.MinSecretLengthLimit
            };
            var inputMessage = "1234567";

            lsm.AddValue("12345");
            var resultMessage = lsm.MaskSecrets(inputMessage);

            Assert.Equal("1234567", resultMessage);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void LoggedSecretMasker_ShortSecret_Removes_From_Dictionary_BoundaryValue2()
        {
            using var lsm = new LoggedSecretMasker(CreateSecretMasker())
            {
                MinSecretLength = LoggedSecretMasker.MinSecretLengthLimit
            };
            var inputMessage = "1234567";

            lsm.AddValue("123456");
            var resultMessage = lsm.MaskSecrets(inputMessage);

            Assert.Equal("***7", resultMessage);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void LoggedSecretMasker_Skipping_ShortSecrets()
        {
            using var lsm = new LoggedSecretMasker(CreateSecretMasker())
            {
                MinSecretLength = 3
            };

            lsm.AddValue("1");
            var resultMessage = lsm.MaskSecrets(@"123");

            Assert.Equal("123", resultMessage);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void LoggedSecretMasker_Sets_MinSecretLength_To_MaxValue()
        {
            using var lsm = new LoggedSecretMasker(CreateSecretMasker());
            var expectedMinSecretsLengthValue = LoggedSecretMasker.MinSecretLengthLimit;

            lsm.MinSecretLength = LoggedSecretMasker.MinSecretLengthLimit + 1;

            Assert.Equal(expectedMinSecretsLengthValue, lsm.MinSecretLength);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void LoggedSecretMasker_NegativeValue_Passed()
        {
            using var lsm = new LoggedSecretMasker(CreateSecretMasker())
            {
                MinSecretLength = -2
            };
            var inputMessage = "12345";

            lsm.AddValue("1");
            var resultMessage = lsm.MaskSecrets(inputMessage);

            Assert.Equal("***2345", resultMessage);
        }
    }
}
