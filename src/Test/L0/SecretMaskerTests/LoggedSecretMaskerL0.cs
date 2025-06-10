// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Globalization;

using Agent.Sdk.SecretMasking;
using Microsoft.Security.Utilities;
using Microsoft.TeamFoundation.DistributedTask.Logging;
using Xunit;
using Xunit.Abstractions;


namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    public class OssLoggedSecretMaskerL0 : LoggedSecretMaskerL0
    {
        private readonly ITestOutputHelper _output;

        public OssLoggedSecretMaskerL0(ITestOutputHelper output)
        {
            _output = output;
        }

        protected override ILoggedSecretMasker CreateSecretMasker()
        {
#pragma warning disable CA2000 // Dispose objects before losing scope. LoggedSecretMasker takes ownership.
            return LoggedSecretMasker.Create(new OssSecretMasker());
#pragma warning restore CA2000
        }

        [Theory]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        [InlineData(0, 1, 1)]
        [InlineData(1, 0, 1)]
        [InlineData(1, 1, 1)]
        [InlineData(10, 5, 3)]
        [InlineData(10, 20, 5)]
        public void OssLoggedSecretMasker_TelemetryEnabled_SendsTelemetry(int uniqueCorrelatingIds, int maxUniqueCorrelatingIds, int maxCorrelatingIdsPerEvent)
        {
            var pattern = new RegexPattern(id: "TEST001/001",
                                           name: "TestPattern",
                                           label: "a test",
                                           DetectionMetadata.HighEntropy,
                                           pattern: "TEST[0-9]+");

            using var ossMasker = new OssSecretMasker(new[] { pattern });
            using var lsm = LoggedSecretMasker.Create(ossMasker);
            lsm.StartTelemetry(maxUniqueCorrelatingIds);

            int charsScanned = 0;
            int stringsScanned = 0;
            int totalDetections = 0;
            var correlatingIds = new string[uniqueCorrelatingIds];

            // Although this secret matches a rule, it is added and masked as a
            // value so we should not report its C3ID nor return more than one
            // detection.
            string knownSecret = "TEST99999";
            string inputWithKnownSecret = $"Known secret added as a value that also matches a rule: {knownSecret}";
            ossMasker.AddValue(knownSecret);
            lsm.MaskSecrets(inputWithKnownSecret);
            stringsScanned++;
            charsScanned += inputWithKnownSecret.Length;
            totalDetections++;

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
            lsm.StopAndPublishTelemetry(
                maxCorrelatingIdsPerEvent,
                (feature, data) =>
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

            int expectedCorrelationEvents = (int)Math.Ceiling((double)uniqueCorrelatingIds / maxCorrelatingIdsPerEvent);
            int maxCorrelationEvents = (int)Math.Ceiling((double)maxUniqueCorrelatingIds / maxCorrelatingIdsPerEvent);

            bool maxEventsExceeded = expectedCorrelationEvents > maxCorrelationEvents;
            if (maxEventsExceeded)
            {
                expectedCorrelationEvents = maxCorrelationEvents;
            }

            int expectedEvents = expectedCorrelationEvents + 1; // +1 for the overall telemetry event.
            Assert.Equal(expectedEvents, telemetry.Count);

            int correlationIdsReceived = 0;
            for (int i = 0; i < expectedCorrelationEvents; i++)
            {
                var correlationTelemetry = telemetry[i];
                var correlationData = correlationTelemetry.Data;

                Assert.Equal(correlationTelemetry.Feature, "SecretMaskerCorrelation");

                if (i < expectedCorrelationEvents - 1)
                {
                    Assert.Equal(maxCorrelatingIdsPerEvent, correlationData.Count);
                }
                else
                {
                    Assert.True(correlationData.Count <= maxCorrelatingIdsPerEvent);
                }

                correlationIdsReceived += correlationData.Count;
                foreach (var (key, value) in correlationData)
                {
                    Assert.True(correlatingIdsToObserve.Remove(key));
                    Assert.Equal("TEST001/001.TestPattern", value);
                }
            }

            Assert.Equal(Math.Min(maxUniqueCorrelatingIds, uniqueCorrelatingIds), correlationIdsReceived);
            if (maxEventsExceeded)
            {
                Assert.Equal(uniqueCorrelatingIds - maxUniqueCorrelatingIds, correlatingIdsToObserve.Count);
            }
            else
            {
                Assert.Equal(0, correlatingIdsToObserve.Count);
            }

            var overallTelemetry = telemetry[telemetry.Count - 1];
            var overallData = overallTelemetry.Data;
            Assert.Equal(overallTelemetry.Feature, "SecretMasker");
            Assert.Equal(Microsoft.Security.Utilities.SecretMasker.Version.ToString(), overallData["Version"]);
            Assert.Equal(charsScanned.ToString(CultureInfo.InvariantCulture), overallData["CharsScanned"]);
            Assert.Equal(stringsScanned.ToString(CultureInfo.InvariantCulture), overallData["StringsScanned"]);
            Assert.Equal(totalDetections.ToString(CultureInfo.InvariantCulture), overallData["TotalDetections"]);
            Assert.Equal(correlationIdsReceived.ToString(CultureInfo.InvariantCulture), overallData["UniqueCorrelatingIds"]);
            Assert.True(0.0 <= double.Parse(overallData["ElapsedMaskingTimeInMilliseconds"], CultureInfo.InvariantCulture));
            Assert.Equal(maxEventsExceeded.ToString(CultureInfo.InvariantCulture), overallData["CorrelationDataIsIncomplete"]);
        }
    }

    public class LegacyLoggedSecretMaskerL0 : LoggedSecretMaskerL0
    {
        protected override ILoggedSecretMasker CreateSecretMasker()
        {
#pragma warning disable CA2000 // Dispose objects before losing scope. LoggedSecretMasker takes ownership.
            return LoggedSecretMasker.Create(new LegacySecretMasker());
#pragma warning restore CA2000
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void LegacyLoggedSecretMasker_CanUseServerInterface()
        {
            using var lsm = CreateSecretMasker();
            var secretMasker = (Microsoft.TeamFoundation.DistributedTask.Logging.ISecretMasker)lsm;
            secretMasker.AddValue("value");
            secretMasker.AddRegex("regex[0-9]");
            secretMasker.AddValueEncoder(v => v + "-encoded");

            Assert.Equal("test *** test", secretMasker.MaskSecrets("test value test"));
            Assert.Equal("test *** test", secretMasker.MaskSecrets("test regex4 test"));
            Assert.Equal("test *** test", secretMasker.MaskSecrets("test value-encoded test"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void LegacyLoggedSecretMasker_Clone()
        {
            using var secretMasker1 = CreateSecretMasker();
            secretMasker1.AddValue("value1", origin: "Test 1");

            using var secretMasker2 = (ILoggedSecretMasker)(((Microsoft.TeamFoundation.DistributedTask.Logging.ISecretMasker)secretMasker1).Clone());
            secretMasker2.AddValue("value2", origin: "Test 2");

            secretMasker1.AddValue("value3", origin: "Test 3");

            Assert.Equal("***", secretMasker1.MaskSecrets("value1"));
            Assert.Equal("value2", secretMasker1.MaskSecrets("value2"));
            Assert.Equal("***", secretMasker1.MaskSecrets("value3"));

            Assert.Equal("***", secretMasker2.MaskSecrets("value1"));
            Assert.Equal("***", secretMasker2.MaskSecrets("value2"));
            Assert.Equal("value3", secretMasker2.MaskSecrets("value3"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void LegacyLoggedSecretMasker_TelemetryEnabled_Ignored()
        {
            using var lsm = CreateSecretMasker();
            lsm.StartTelemetry(maxUniqueCorrelatingIds: 1); // no-op: legacy VSO masker does not support telemetry. 
            lsm.StopAndPublishTelemetry(maxCorrelatingIdsPerEvent: 1, (_, _) => Assert.True(false, "This should not be called."));
        }
    }

    public abstract class LoggedSecretMaskerL0
    {
        protected abstract ILoggedSecretMasker CreateSecretMasker();

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void LoggedSecretMasker_TelemetryDisabled_DoesNotPublish()
        {
            using var lsm = CreateSecretMasker();
            lsm.StopAndPublishTelemetry(maxCorrelatingIdsPerEvent: 1, (_, _) => Assert.True(false, "This should not be called."));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void LoggedSecretMasker_MaskingSecrets()
        {
            using var lsm = CreateSecretMasker();
            lsm.MinSecretLength = 0;

            var inputMessage = "123";

            lsm.AddValue("1", origin: "Test");
            var resultMessage = lsm.MaskSecrets(inputMessage);

            Assert.Equal("***23", resultMessage);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void LoggedSecretMasker_ShortSecret_Removes_From_Dictionary()
        {
            using var lsm = CreateSecretMasker();
            lsm.MinSecretLength = 0;

            var inputMessage = "123";

            lsm.AddValue("1", origin: "Test");
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
            using var lsm = CreateSecretMasker();
            lsm.MinSecretLength = LoggedSecretMasker.MinSecretLengthLimit;

            var inputMessage = "1234567";

            lsm.AddValue("12345", origin: "Test");
            var resultMessage = lsm.MaskSecrets(inputMessage);

            Assert.Equal("1234567", resultMessage);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void LoggedSecretMasker_ShortSecret_Removes_From_Dictionary_BoundaryValue2()
        {
            using var lsm = CreateSecretMasker();
            lsm.MinSecretLength = LoggedSecretMasker.MinSecretLengthLimit;

            var inputMessage = "1234567";

            lsm.AddValue("123456", origin: "Test");
            var resultMessage = lsm.MaskSecrets(inputMessage);

            Assert.Equal("***7", resultMessage);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void LoggedSecretMasker_Skipping_ShortSecrets()
        {
            using var lsm = CreateSecretMasker();
            lsm.MinSecretLength = 3;

            lsm.AddValue("1", origin: "Test");
            var resultMessage = lsm.MaskSecrets(@"123");

            Assert.Equal("123", resultMessage);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void LoggedSecretMasker_Sets_MinSecretLength_To_MaxValue()
        {
            using var lsm = CreateSecretMasker();
            var expectedMinSecretsLengthValue = LoggedSecretMasker.MinSecretLengthLimit;

            lsm.MinSecretLength = LoggedSecretMasker.MinSecretLengthLimit + 1;

            Assert.Equal(expectedMinSecretsLengthValue, lsm.MinSecretLength);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void LoggedSecretMasker_NegativeValue_Passed()
        {
            using var lsm = CreateSecretMasker();
            lsm.MinSecretLength = -2;

            var inputMessage = "12345";

            lsm.AddValue("1", origin: "Test");
            var resultMessage = lsm.MaskSecrets(inputMessage);

            Assert.Equal("***2345", resultMessage);
        }
    }
}
