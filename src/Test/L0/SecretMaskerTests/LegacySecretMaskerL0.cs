// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.DistributedTask.Logging;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    public sealed class LegacySecretMaskerL0
    {
        private ISecretMasker initSecretMasker()
        {
            var testSecretMasker = new SecretMasker();
            testSecretMasker.AddRegex(AdditionalMaskingRegexes.UrlSecretPattern);

            return testSecretMasker;
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void IsSimpleUrlNotMasked()
        {
            var testSecretMasker = initSecretMasker();

            Assert.Equal(
               "https://simpledomain@example.com",
               testSecretMasker.MaskSecrets("https://simpledomain@example.com"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void IsComplexUrlNotMasked()
        {
            var testSecretMasker = initSecretMasker();

            Assert.Equal(
                "https://url.com:443/~user/foo=bar+42-18?what=this.is.an.example....~~many@&param=value",
                testSecretMasker.MaskSecrets("https://url.com:443/~user/foo=bar+42-18?what=this.is.an.example....~~many@&param=value"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void IsUserInfoMaskedCorrectly()
        {
            var testSecretMasker = initSecretMasker();

            Assert.Equal(
               "https://user:***@example.com",
               testSecretMasker.MaskSecrets("https://user:pass@example.com"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void IsUserInfoWithSpecialCharactersMaskedCorrectly()
        {
            var testSecretMasker = initSecretMasker();

            Assert.Equal(
               "https://user:***@example.com",
               testSecretMasker.MaskSecrets(@"https://user:pass4';.!&*()=,$-+~@example.com"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void IsUserInfoWithDigitsInNameMaskedCorrectly()
        {
            var testSecretMasker = initSecretMasker();

            Assert.Equal(
               "https://username123:***@example.com",
               testSecretMasker.MaskSecrets(@"https://username123:password@example.com"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void IsUserInfoWithLongPasswordAndNameMaskedCorrectly()
        {
            var testSecretMasker = initSecretMasker();

            Assert.Equal(
               "https://username_loooooooooooooooooooooooooooooooooooooooooong:***@example.com",
               testSecretMasker.MaskSecrets(@"https://username_loooooooooooooooooooooooooooooooooooooooooong:password_looooooooooooooooooooooooooooooooooooooooooooooooong@example.com"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void IsUserInfoWithEncodedCharactersdInNameMaskedCorrectly()
        {
            var testSecretMasker = initSecretMasker();

            Assert.Equal(
               "https://username%10%A3%F6:***@example.com",
               testSecretMasker.MaskSecrets(@"https://username%10%A3%F6:password123@example.com"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void IsUserInfoWithEncodedAndEscapedCharactersdInNameMaskedCorrectly()
        {
            var testSecretMasker = initSecretMasker();

            Assert.Equal(
               "https://username%AZP2510%AZP25A3%AZP25F6:***@example.com",
               testSecretMasker.MaskSecrets(@"https://username%AZP2510%AZP25A3%AZP25F6:password123@example.com"));
        }
    }
}
