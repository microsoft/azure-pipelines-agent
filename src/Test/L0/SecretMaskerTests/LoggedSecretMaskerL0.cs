using Agent.Sdk.Util;
using Microsoft.TeamFoundation.DistributedTask.Logging;
using System;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    public class LoggedSecretMaskerL0
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void IsShortSecretRemovedFromDictionary()
        {
            var testSecretMasker = new LoggedSecretMasker(new SecretMasker());
            testSecretMasker.AddValue("1");

            var input = "123";

            Assert.Equal(
               "***23",
               testSecretMasker.MaskSecrets(input));

            testSecretMasker.MinSecretLength = 4;

            testSecretMasker.RemoveShortSecretsFromDictionary();

            Assert.Equal(
               input,
               testSecretMasker.MaskSecrets(input));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void IsShortSecretRemovedFromDictionaryOnBoundaryValue()
        {
            var testSecretMasker = new LoggedSecretMasker(new SecretMasker());
            testSecretMasker.AddValue("123");
            testSecretMasker.AddValue("456");

            var input = "123456";

            Assert.Equal(
               "***",
               testSecretMasker.MaskSecrets(input));

            testSecretMasker.MinSecretLength = 3;

            testSecretMasker.RemoveShortSecretsFromDictionary();

            Assert.Equal(
               "***",
               testSecretMasker.MaskSecrets(input));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void IsShortSecretSkipped()
        {
            var testSecretMasker = new LoggedSecretMasker(new SecretMasker(3));
            testSecretMasker.AddValue("1");

            Assert.Equal(
               "123",
               testSecretMasker.MaskSecrets(@"123"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void IsBigMinSecretLengthValueRestricted()
        {
            var testSecretMasker = new LoggedSecretMasker(new SecretMasker());

            Assert.Throws<ArgumentException>(() => testSecretMasker.MinSecretLength = 5);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void IsBigMinSecretLengthValueSetToDefault()
        {
            var testSecretMasker = new LoggedSecretMasker(new SecretMasker());

            try { testSecretMasker.MinSecretLength = 5; }
            catch (ArgumentException) { }

            testSecretMasker.AddValue("1");
            testSecretMasker.AddValue("2345");

            Assert.Equal(
               "1***",
               testSecretMasker.MaskSecrets(@"12345"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void IsNegativeValuePassed()
        {
            var testSecretMasker = new LoggedSecretMasker(new SecretMasker());

            testSecretMasker.MinSecretLength = -2;

            testSecretMasker.AddValue("1");
            testSecretMasker.AddValue("2345");

            Assert.Equal(
               "***",
               testSecretMasker.MaskSecrets(@"12345"));
        }
    }
}
