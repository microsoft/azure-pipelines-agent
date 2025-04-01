// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Agent.Sdk.SecretMasking;
using Microsoft.TeamFoundation.DistributedTask.Logging;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    public sealed class LegacySecretMaskerL0 : SecretMaskerL0<SecretMasker>
    {
        protected override SecretMasker CreateSecretMasker()
        {
            var testSecretMasker = new SecretMasker();
            testSecretMasker.AddRegex(AdditionalMaskingRegexes.UrlSecretPattern);
            return testSecretMasker;
        }
    }

    public sealed class OssSecretMaskerL0 : SecretMaskerL0<OssSecretMasker>
    {
        protected override OssSecretMasker CreateSecretMasker()
        {
            var testSecretMasker = new OssSecretMasker();
            testSecretMasker.AddRegex(AdditionalMaskingRegexes.UrlSecretPattern);
            return testSecretMasker;
        }
    }

    public abstract class SecretMaskerL0<TSecretMasker> where TSecretMasker : IDisposable, ISecretMasker
    {
        protected abstract TSecretMasker CreateSecretMasker();

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void IsSimpleUrlNotMasked()
        {
            using var testSecretMasker = CreateSecretMasker();

            Assert.Equal(
               "https://simpledomain@example.com",
               testSecretMasker.MaskSecrets("https://simpledomain@example.com"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void IsComplexUrlNotMasked()
        {
            using var testSecretMasker = CreateSecretMasker();

            Assert.Equal(
                "https://url.com:443/~user/foo=bar+42-18?what=this.is.an.example....~~many@&param=value",
                testSecretMasker.MaskSecrets("https://url.com:443/~user/foo=bar+42-18?what=this.is.an.example....~~many@&param=value"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void IsUserInfoMaskedCorrectly()
        {
            using var testSecretMasker = CreateSecretMasker();

            Assert.Equal(
               "https://user:***@example.com",
               testSecretMasker.MaskSecrets("https://user:pass@example.com"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void IsUserInfoWithSpecialCharactersMaskedCorrectly()
        {
            using var testSecretMasker = CreateSecretMasker();

            Assert.Equal(
               "https://user:***@example.com",
               testSecretMasker.MaskSecrets(@"https://user:pass4';.!&*()=,$-+~@example.com"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void IsUserInfoWithDigitsInNameMaskedCorrectly()
        {
            using var testSecretMasker = CreateSecretMasker();

            Assert.Equal(
               "https://username123:***@example.com",
               testSecretMasker.MaskSecrets(@"https://username123:password@example.com"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void IsUserInfoWithLongPasswordAndNameMaskedCorrectly()
        {
            using var testSecretMasker = CreateSecretMasker();

            Assert.Equal(
               "https://username_loooooooooooooooooooooooooooooooooooooooooong:***@example.com",
               testSecretMasker.MaskSecrets(@"https://username_loooooooooooooooooooooooooooooooooooooooooong:password_looooooooooooooooooooooooooooooooooooooooooooooooong@example.com"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void IsUserInfoWithEncodedCharactersdInNameMaskedCorrectly()
        {
            using var testSecretMasker = CreateSecretMasker();

            Assert.Equal(
               "https://username%10%A3%F6:***@example.com",
               testSecretMasker.MaskSecrets(@"https://username%10%A3%F6:password123@example.com"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void IsUserInfoWithEncodedAndEscapedCharactersdInNameMaskedCorrectly()
        {
            using var testSecretMasker = CreateSecretMasker();

            Assert.Equal(
               "https://username%AZP2510%AZP25A3%AZP25F6:***@example.com",
               testSecretMasker.MaskSecrets(@"https://username%AZP2510%AZP25A3%AZP25F6:password123@example.com"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void SecretMaskerTests_CopyConstructor()
        {
            // Setup masker 1
            using var secretMasker1 = CreateSecretMasker();
            secretMasker1.AddRegex("masker-1-regex-1_*");
            secretMasker1.AddRegex("masker-1-regex-2_*");
            secretMasker1.AddValue("masker-1-value-1_");
            secretMasker1.AddValue("masker-1-value-2_");
            secretMasker1.AddValueEncoder(x => x.Replace("_", "_masker-1-encoder-1"));
            secretMasker1.AddValueEncoder(x => x.Replace("_", "_masker-1-encoder-2"));

            // Copy and add to masker 2.
            using var secretMasker2 = (TSecretMasker)secretMasker1.Clone();
            secretMasker2.AddRegex("masker-2-regex-1_*");
            secretMasker2.AddValue("masker-2-value-1_");
            secretMasker2.AddValueEncoder(x => x.Replace("_", "_masker-2-encoder-1"));

            // Add to masker 1.
            secretMasker1.AddRegex("masker-1-regex-3_*");
            secretMasker1.AddValue("masker-1-value-3_");
            secretMasker1.AddValueEncoder(x => x.Replace("_", "_masker-1-encoder-3"));

            // Assert masker 1 values.
            Assert.Equal("***", secretMasker1.MaskSecrets("masker-1-regex-1___")); // original regex
            Assert.Equal("***", secretMasker1.MaskSecrets("masker-1-regex-2___")); // original regex
            Assert.Equal("***", secretMasker1.MaskSecrets("masker-1-regex-3___")); // new regex
            Assert.Equal("***", secretMasker1.MaskSecrets("masker-1-value-1_")); // original value
            Assert.Equal("***", secretMasker1.MaskSecrets("masker-1-value-2_")); // original value
            Assert.Equal("***", secretMasker1.MaskSecrets("masker-1-value-3_")); // new value
            Assert.Equal("***", secretMasker1.MaskSecrets("masker-1-value-1_masker-1-encoder-1")); // original value, original encoder
            Assert.Equal("***", secretMasker1.MaskSecrets("masker-1-value-1_masker-1-encoder-2")); // original value, original encoder
            Assert.Equal("***", secretMasker1.MaskSecrets("masker-1-value-1_masker-1-encoder-3")); // original value, new encoder
            Assert.Equal("***", secretMasker1.MaskSecrets("masker-1-value-3_masker-1-encoder-1")); // new value, original encoder
            Assert.Equal("***", secretMasker1.MaskSecrets("masker-1-value-3_masker-1-encoder-2")); // new value, original encoder
            Assert.Equal("***", secretMasker1.MaskSecrets("masker-1-value-3_masker-1-encoder-3")); // new value, new encoder
            Assert.Equal("masker-2-regex-1___", secretMasker1.MaskSecrets("masker-2-regex-1___")); // separate regex storage from copy
            Assert.Equal("masker-2-value-1_", secretMasker1.MaskSecrets("masker-2-value-1_")); // separate value storage from copy
            Assert.Equal("***masker-2-encoder-1", secretMasker1.MaskSecrets("masker-1-value-1_masker-2-encoder-1")); // separate encoder storage from copy

            // Assert masker 2 values.
            Assert.Equal("***", secretMasker2.MaskSecrets("masker-1-regex-1___")); // copied regex
            Assert.Equal("***", secretMasker2.MaskSecrets("masker-1-regex-2___")); // copied regex
            Assert.Equal("***", secretMasker2.MaskSecrets("masker-2-regex-1___")); // new regex
            Assert.Equal("***", secretMasker2.MaskSecrets("masker-1-value-1_")); // copied value
            Assert.Equal("***", secretMasker2.MaskSecrets("masker-1-value-2_")); // copied value
            Assert.Equal("***", secretMasker2.MaskSecrets("masker-2-value-1_")); // new value
            Assert.Equal("***", secretMasker2.MaskSecrets("masker-1-value-1_masker-1-encoder-1")); // copied value, copied encoder
            Assert.Equal("***", secretMasker2.MaskSecrets("masker-1-value-1_masker-1-encoder-2")); // copied value, copied encoder
            Assert.Equal("***", secretMasker2.MaskSecrets("masker-1-value-1_masker-2-encoder-1")); // copied value, new encoder
            Assert.Equal("***", secretMasker2.MaskSecrets("masker-2-value-1_masker-1-encoder-1")); // new value, copied encoder
            Assert.Equal("***", secretMasker2.MaskSecrets("masker-2-value-1_masker-1-encoder-2")); // new value, copied encoder
            Assert.Equal("***", secretMasker2.MaskSecrets("masker-2-value-1_masker-2-encoder-1")); // new value, new encoder
            Assert.Equal("masker-1-regex-3___", secretMasker2.MaskSecrets("masker-1-regex-3___")); // separate regex storage from original
            Assert.Equal("masker-1-value-3_", secretMasker2.MaskSecrets("masker-1-value-3_")); // separate value storage from original
            Assert.Equal("***masker-1-encoder-3", secretMasker2.MaskSecrets("masker-1-value-1_masker-1-encoder-3")); // separate encoder storage from original
        }
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void SecretMaskerTests_Encoder()
        {
            // Add encoder before values.
            using var secretMasker = CreateSecretMasker();
            secretMasker.AddValueEncoder(x => x.Replace("-", "_"));
            secretMasker.AddValueEncoder(x => x.Replace("-", " "));
            secretMasker.AddValue("value-1");
            secretMasker.AddValue("value-2");
            Assert.Equal("***", secretMasker.MaskSecrets("value-1"));
            Assert.Equal("***", secretMasker.MaskSecrets("value_1"));
            Assert.Equal("***", secretMasker.MaskSecrets("value 1"));
            Assert.Equal("***", secretMasker.MaskSecrets("value-2"));
            Assert.Equal("***", secretMasker.MaskSecrets("value_2"));
            Assert.Equal("***", secretMasker.MaskSecrets("value 2"));
            Assert.Equal("value-3", secretMasker.MaskSecrets("value-3"));

            // Add values after encoders.
            secretMasker.AddValue("value-3");
            Assert.Equal("***", secretMasker.MaskSecrets("value-3"));
            Assert.Equal("***", secretMasker.MaskSecrets("value_3"));
            Assert.Equal("***", secretMasker.MaskSecrets("value 3"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void SecretMaskerTests_Encoder_JsonStringEscape()
        {
            using var secretMasker = CreateSecretMasker();
            secretMasker.AddValueEncoder(ValueEncoders.JsonStringEscape);
            secretMasker.AddValue("carriage-return\r_newline\n_tab\t_backslash\\_double-quote\"");
            Assert.Equal("***", secretMasker.MaskSecrets("carriage-return\r_newline\n_tab\t_backslash\\_double-quote\""));
            Assert.Equal("***", secretMasker.MaskSecrets("carriage-return\\r_newline\\n_tab\\t_backslash\\\\_double-quote\\\""));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void SecretMaskerTests_Encoder_BackslashEscape()
        {
            using var secretMasker = CreateSecretMasker();
            secretMasker.AddValueEncoder(ValueEncoders.BackslashEscape);
            secretMasker.AddValue(@"abc\\def\'\""ghi\t");
            Assert.Equal("***", secretMasker.MaskSecrets(@"abc\\def\'\""ghi\t"));
            Assert.Equal("***", secretMasker.MaskSecrets(@"abc\def'""ghi" + "\t"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void SecretMaskerTests_Encoder_UriDataEscape()
        {
            using var secretMasker = CreateSecretMasker();
            secretMasker.AddValueEncoder(ValueEncoders.UriDataEscape);
            secretMasker.AddValue("hello world");
            Assert.Equal("***", secretMasker.MaskSecrets("hello world"));
            Assert.Equal("***", secretMasker.MaskSecrets("hello%20world"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void SecretMaskerTests_Encoder_UriDataEscape_LargeString()
        {
            // Uri.EscapeDataString cannot receive a string longer than 65519 characters.
            // For unit testing we call a different overload with a smaller segment size (improve unit test speed).

            ValueEncoder encoder = x => ValueEncoders.UriDataEscape(x);

            using (var secretMasker = CreateSecretMasker())
            {
                secretMasker.AddValueEncoder(encoder);
                var value = String.Empty.PadRight(1, ' ');
                secretMasker.AddValue(value);
                Assert.Equal("***", secretMasker.MaskSecrets(value));
                Assert.Equal("***", secretMasker.MaskSecrets(value.Replace(" ", "%20")));
            }

            using (var secretMasker = CreateSecretMasker())
            {
                secretMasker.AddValueEncoder(encoder);
                var value = String.Empty.PadRight(2, ' ');
                secretMasker.AddValue(value);
                Assert.Equal("***", secretMasker.MaskSecrets(value));
                Assert.Equal("***", secretMasker.MaskSecrets(value.Replace(" ", "%20")));
            }

            using (var secretMasker = CreateSecretMasker())
            {
                secretMasker.AddValueEncoder(encoder);
                var value = String.Empty.PadRight(3, ' ');
                secretMasker.AddValue(value);
                Assert.Equal("***", secretMasker.MaskSecrets(value));
                Assert.Equal("***", secretMasker.MaskSecrets(value.Replace(" ", "%20")));
            }

            using (var secretMasker = CreateSecretMasker())
            {
                secretMasker.AddValueEncoder(encoder);
                var value = String.Empty.PadRight(4, ' ');
                secretMasker.AddValue(value);
                Assert.Equal("***", secretMasker.MaskSecrets(value));
                Assert.Equal("***", secretMasker.MaskSecrets(value.Replace(" ", "%20")));
            }

            using (var secretMasker = CreateSecretMasker())
            {
                secretMasker.AddValueEncoder(encoder);
                var value = String.Empty.PadRight(5, ' ');
                secretMasker.AddValue(value);
                Assert.Equal("***", secretMasker.MaskSecrets(value));
                Assert.Equal("***", secretMasker.MaskSecrets(value.Replace(" ", "%20")));
            }

            using (var secretMasker = CreateSecretMasker())
            {
                secretMasker.AddValueEncoder(encoder);
                var value = String.Empty.PadRight(5, ' ');
                secretMasker.AddValue(value);
                Assert.Equal("***", secretMasker.MaskSecrets(value));
                Assert.Equal("***", secretMasker.MaskSecrets(value.Replace(" ", "%20")));
            }

            using (var secretMasker = CreateSecretMasker())
            {
                secretMasker.AddValueEncoder(encoder);
                var value = String.Empty.PadRight(6, ' ');
                secretMasker.AddValue(value);
                Assert.Equal("***", secretMasker.MaskSecrets(value));
                Assert.Equal("***", secretMasker.MaskSecrets(value.Replace(" ", "%20")));
            }


            using (var secretMasker = CreateSecretMasker())
            {
                secretMasker.AddValueEncoder(encoder);
                var value = String.Empty.PadRight(7, ' ');
                secretMasker.AddValue(value);
                Assert.Equal("***", secretMasker.MaskSecrets(value));
                Assert.Equal("***", secretMasker.MaskSecrets(value.Replace(" ", "%20")));
            }

            using (var secretMasker = CreateSecretMasker())
            {
                secretMasker.AddValueEncoder(encoder);
                var value = "𐐷𐐷𐐷𐐷"; // surrogate pair
                secretMasker.AddValue(value);
                Assert.Equal("***", secretMasker.MaskSecrets(value));
            }

            using (var secretMasker = CreateSecretMasker())
            {
                secretMasker.AddValueEncoder(encoder);
                var value = " 𐐷𐐷𐐷𐐷"; // shift by one non-surrogate character to ensure surrogate across segment boundary handled correctly
                secretMasker.AddValue(value);
                Assert.Equal("***", secretMasker.MaskSecrets(value));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void SecretMaskerTests_HandlesEmptyInput()
        {
            using var secretMasker = CreateSecretMasker();
            secretMasker.AddValue("abcd");

            var result = secretMasker.MaskSecrets(null);
            Assert.Equal(string.Empty, result);

            result = secretMasker.MaskSecrets(string.Empty);
            Assert.Equal(string.Empty, result);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void SecretMaskerTests_HandlesNoMasks()
        {
            using var secretMasker = CreateSecretMasker();
            var expected = "abcdefg";
            var actual = secretMasker.MaskSecrets(expected);
            Assert.Equal(expected, actual);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void SecretMaskerTests_ReplacesValue()
        {
            using var secretMasker = CreateSecretMasker();
            secretMasker.AddValue("def");

            var input = "abcdefg";
            var result = secretMasker.MaskSecrets(input);

            Assert.Equal("abc***g", result);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void SecretMaskerTests_ReplacesMultipleInstances()
        {
            using var secretMasker = CreateSecretMasker();
            secretMasker.AddValue("def");

            var input = "abcdefgdef";
            var result = secretMasker.MaskSecrets(input);

            Assert.Equal("abc***g***", result);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void SecretMaskerTests_ReplacesMultipleAdjacentInstances()
        {
            using var secretMasker = CreateSecretMasker();
            secretMasker.AddValue("abc");

            var input = "abcabcdef";
            var result = secretMasker.MaskSecrets(input);

            Assert.Equal("***def", result);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void SecretMaskerTests_ReplacesMultipleSecrets()
        {
            using var secretMasker = CreateSecretMasker();
            secretMasker.AddValue("bcd");
            secretMasker.AddValue("fgh");

            var input = "abcdefghi";
            var result = secretMasker.MaskSecrets(input);

            Assert.Equal("a***e***i", result);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void SecretMaskerTests_ReplacesOverlappingSecrets()
        {
            using var secretMasker = CreateSecretMasker();
            secretMasker.AddValue("def");
            secretMasker.AddValue("bcd");

            var input = "abcdefg";
            var result = secretMasker.MaskSecrets(input);

            // a naive replacement would replace "def" first, and never find "bcd", resulting in "abc***g"
            // or it would replace "bcd" first, and never find "def", resulting in "a***efg"

            Assert.Equal("a***g", result);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void SecretMaskerTests_ReplacesAdjacentSecrets()
        {
            using var secretMasker = CreateSecretMasker();
            secretMasker.AddValue("efg");
            secretMasker.AddValue("bcd");

            var input = "abcdefgh";
            var result = secretMasker.MaskSecrets(input);

            // two adjacent secrets are basically one big secret

            Assert.Equal("a***h", result);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void SecretMaskerTests_MinLengthSetThroughConstructor()
        {
            using var secretMasker = CreateSecretMasker();
            secretMasker.MinSecretLength = 9;

            secretMasker.AddValue("efg");
            secretMasker.AddValue("bcd");

            var input = "abcdefgh";
            var result = secretMasker.MaskSecrets(input);

            // two adjacent secrets are basically one big secret

            Assert.Equal("abcdefgh", result);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void SecretMaskerTests_MinLengthSetThroughProperty()
        {
            using var secretMasker = CreateSecretMasker();
            secretMasker.MinSecretLength = 9;

            secretMasker.AddValue("efg");
            secretMasker.AddValue("bcd");

            var input = "abcdefgh";
            var result = secretMasker.MaskSecrets(input);

            // two adjacent secrets are basically one big secret

            Assert.Equal("abcdefgh", result);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void SecretMaskerTests_MinLengthSetThroughPropertySetTwice()
        {
            using var secretMasker = CreateSecretMasker();

            var minSecretLenFirst = 9;
            secretMasker.MinSecretLength = minSecretLenFirst;

            var minSecretLenSecond = 2;
            secretMasker.MinSecretLength = minSecretLenSecond;

            Assert.Equal(secretMasker.MinSecretLength, minSecretLenSecond);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void SecretMaskerTests_NegativeMinSecretLengthSet()
        {
            using var secretMasker = CreateSecretMasker();
            secretMasker.MinSecretLength = -3;

            secretMasker.AddValue("efg");
            secretMasker.AddValue("bcd");

            var input = "abcdefgh";
            var result = secretMasker.MaskSecrets(input);

            Assert.Equal("a***h", result);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void SecretMaskerTests_RemoveShortSecrets()
        {
            using var secretMasker = CreateSecretMasker();
            secretMasker.MinSecretLength = 3;

            secretMasker.AddValue("efg");
            secretMasker.AddValue("bcd");

            var input = "abcdefgh";
            var result = secretMasker.MaskSecrets(input);

            Assert.Equal("a***h", result);

            secretMasker.MinSecretLength = 4;
            secretMasker.RemoveShortSecretsFromDictionary();

            var result2 = secretMasker.MaskSecrets(input);

            Assert.Equal(input, result2);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void SecretMaskerTests_RemoveShortSecretsBoundaryValues()
        {
            using var secretMasker = CreateSecretMasker();
            secretMasker.MinSecretLength = 0;

            secretMasker.AddValue("bc");
            secretMasker.AddValue("defg");
            secretMasker.AddValue("h12");

            var input = "abcdefgh123";
            var result = secretMasker.MaskSecrets(input);

            Assert.Equal("a***3", result);

            secretMasker.MinSecretLength = 3;
            secretMasker.RemoveShortSecretsFromDictionary();

            var result2 = secretMasker.MaskSecrets(input);

            Assert.Equal("abc***3", result2);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void SecretMaskerTests_RemoveShortRegexes()
        {
            using var secretMasker = CreateSecretMasker();
            secretMasker.MinSecretLength = 0;

            secretMasker.AddRegex("bc");
            secretMasker.AddRegex("defg");
            secretMasker.AddRegex("h12");

            secretMasker.MinSecretLength = 3;
            secretMasker.RemoveShortSecretsFromDictionary();

            var input = "abcdefgh123";
            var result = secretMasker.MaskSecrets(input);

            Assert.Equal("abc***3", result);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void SecretMaskerTests_RemoveEncodedSecrets()
        {
            using var secretMasker = CreateSecretMasker();
            secretMasker.MinSecretLength = 0;

            secretMasker.AddValue("1");
            secretMasker.AddValue("2");
            secretMasker.AddValue("3");
            secretMasker.AddValueEncoder(new ValueEncoder(x => x.Replace("1", "123")));
            secretMasker.AddValueEncoder(new ValueEncoder(x => x.Replace("2", "45")));
            secretMasker.AddValueEncoder(new ValueEncoder(x => x.Replace("3", "6789")));

            secretMasker.MinSecretLength = 3;
            secretMasker.RemoveShortSecretsFromDictionary();

            var input = "123456789";
            var result = secretMasker.MaskSecrets(input);

            Assert.Equal("***45***", result);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void SecretMaskerTests_NotAddShortEncodedSecrets()
        {
            using var secretMasker = CreateSecretMasker();
            secretMasker.MinSecretLength = 3;

            secretMasker.AddValueEncoder(new ValueEncoder(x => x.Replace("123", "ab")));
            secretMasker.AddValue("123");
            secretMasker.AddValue("345");
            secretMasker.AddValueEncoder(new ValueEncoder(x => x.Replace("345", "cd")));

            var input = "ab123cd345";
            var result = secretMasker.MaskSecrets(input);

            Assert.Equal("ab***cd***", result);
        }
    }
}
