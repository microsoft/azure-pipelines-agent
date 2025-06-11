// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk.SecretMasking;
using Microsoft.TeamFoundation.DistributedTask.Logging;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    public class OssLoggedSecretMaskerL0 : LoggedSecretMaskerL0
    {
        protected override ILoggedSecretMasker CreateSecretMasker()
        {
#pragma warning disable CA2000 // Dispose objects before losing scope. LoggedSecretMasker takes ownership.
            return LoggedSecretMasker.Create(new OssSecretMasker());
#pragma warning restore CA2000
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
            var secretMasker = (ISecretMasker)lsm;
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

            using var secretMasker2 = (ILoggedSecretMasker)(((ISecretMasker)secretMasker1).Clone());
            secretMasker2.AddValue("value2", origin: "Test 2");

            secretMasker1.AddValue("value3", origin: "Test 3");

            Assert.Equal("***", secretMasker1.MaskSecrets("value1"));
            Assert.Equal("value2", secretMasker1.MaskSecrets("value2"));
            Assert.Equal("***", secretMasker1.MaskSecrets("value3"));

            Assert.Equal("***", secretMasker2.MaskSecrets("value1"));
            Assert.Equal("***", secretMasker2.MaskSecrets("value2"));
            Assert.Equal("value3", secretMasker2.MaskSecrets("value3"));
        }
    }

    public abstract class LoggedSecretMaskerL0
    {
        protected abstract ILoggedSecretMasker CreateSecretMasker();

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
