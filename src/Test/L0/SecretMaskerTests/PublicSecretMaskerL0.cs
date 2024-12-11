// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System;
using Xunit;
using Microsoft.Security.Utilities;
using Agent.Sdk.SecretMasking;

namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    public sealed class PublicSecretMaskerL0
    {
        private ISecretMasker initSecretMasker()
        {
            var testSecretMasker = new SecretMasker();
            testSecretMasker.DefaultRegexRedactionToken = "***";
            testSecretMasker.AddPatterns(new[] { new UrlCredentials() });
            return testSecretMasker;
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void IsSimpleUrlNotMasked()
        {
            using var testSecretMasker = initSecretMasker();

            Assert.Equal(
               "https://simpledomain@example.com",
               testSecretMasker.MaskSecrets("https://simpledomain@example.com"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void IsComplexUrlNotMasked()
        {
            using var testSecretMasker = initSecretMasker();

            Assert.Equal(
                "https://url.com:443/~user/foo=bar+42-18?what=this.is.an.example....~~many@&param=value",
                testSecretMasker.MaskSecrets("https://url.com:443/~user/foo=bar+42-18?what=this.is.an.example....~~many@&param=value"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void IsUserInfoMaskedCorrectly()
        {
            using var testSecretMasker = initSecretMasker();

            Assert.Equal(
               "https://user:***@example.com",
               testSecretMasker.MaskSecrets("https://user:pass@example.com"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void IsUserInfoWithSpecialCharactersMaskedCorrectly()
        {
            using var testSecretMasker = initSecretMasker();

            Assert.Equal(
               "https://user:***@example.com",
               testSecretMasker.MaskSecrets(@"https://user:pass4';.!&*()=,$-+~@example.com"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void IsUserInfoWithDigitsInNameMaskedCorrectly()
        {
            using var testSecretMasker = initSecretMasker();

            Assert.Equal(
               "https://username123:***@example.com",
               testSecretMasker.MaskSecrets(@"https://username123:password@example.com"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void IsUserInfoWithLongPasswordAndNameMaskedCorrectly()
        {
            using var testSecretMasker = initSecretMasker();

            Assert.Equal(
               "https://username_loooooooooooooooooooooooooooooooooooooooooong:***@example.com",
               testSecretMasker.MaskSecrets(@"https://username_loooooooooooooooooooooooooooooooooooooooooong:password_looooooooooooooooooooooooooooooooooooooooooooooooong@example.com"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void IsUserInfoWithEncodedCharactersdInNameMaskedCorrectly()
        {
            using var testSecretMasker = initSecretMasker();

            Assert.Equal(
               "https://username%10%A3%F6:***@example.com",
               testSecretMasker.MaskSecrets(@"https://username%10%A3%F6:password123@example.com"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void IsUserInfoWithEncodedAndEscapedCharactersdInNameMaskedCorrectly()
        {
            using var testSecretMasker = initSecretMasker();

            Assert.Equal(
               "https://username%AZP2510%AZP25A3%AZP25F6:***@example.com",
               testSecretMasker.MaskSecrets(@"https://username%AZP2510%AZP25A3%AZP25F6:password123@example.com"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void SecretMaskerTests_CopyConstructor()
        {
            new RegexPattern("000", "Test", 0, "masker-1-regex-1_*");
            // Setup masker 1
            using var secretMasker1 = new SecretMasker();
            secretMasker1.DefaultRegexRedactionToken = "***";

            secretMasker1.AddRegex(new RegexPattern("000", "Test", 0, "masker-1-regex-1_*"));
            secretMasker1.AddRegex(new RegexPattern("000", "Test", 0, "masker-1-regex-2_*"));
            secretMasker1.AddValue("masker-1-value-1_");
            secretMasker1.AddValue("masker-1-value-2_");
            secretMasker1.AddLiteralEncoder(x => x.Replace("_", "_masker-1-encoder-1"));
            secretMasker1.AddLiteralEncoder(x => x.Replace("_", "_masker-1-encoder-2"));

            // Copy and add to masker 2.
            using var secretMasker2 = secretMasker1.Clone();
            secretMasker2.AddRegex(new RegexPattern("000", "Test", 0, "masker-2-regex-1_*"));
            secretMasker2.AddValue("masker-2-value-1_");
            secretMasker2.AddLiteralEncoder(x => x.Replace("_", "_masker-2-encoder-1"));

            // Add to masker 1.
            secretMasker1.AddRegex(new RegexPattern("000", "Test", 0, "masker-1-regex-3_*"));
            secretMasker1.AddValue("masker-1-value-3_");
            secretMasker1.AddLiteralEncoder(x => x.Replace("_", "_masker-1-encoder-3"));

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
            using var secretMasker = new SecretMasker();
            secretMasker.AddLiteralEncoder(x => x.Replace("-", "_"));
            secretMasker.AddLiteralEncoder(x => x.Replace("-", " "));
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
            using var secretMasker = new SecretMasker();
            secretMasker.AddLiteralEncoder(LiteralEncoders.JsonStringEscape);
            secretMasker.AddValue("carriage-return\r_newline\n_tab\t_backslash\\_double-quote\"");
            Assert.Equal("***", secretMasker.MaskSecrets("carriage-return\r_newline\n_tab\t_backslash\\_double-quote\""));
            Assert.Equal("***", secretMasker.MaskSecrets("carriage-return\\r_newline\\n_tab\\t_backslash\\\\_double-quote\\\""));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void SecretMaskerTests_Encoder_BackslashEscape()
        {
            using var secretMasker = new SecretMasker();
            secretMasker.AddLiteralEncoder(LiteralEncoders.BackslashEscape);
            secretMasker.AddValue(@"abc\\def\'\""ghi\t");
            Assert.Equal("***", secretMasker.MaskSecrets(@"abc\\def\'\""ghi\t"));
            Assert.Equal("***", secretMasker.MaskSecrets(@"abc\def'""ghi" + "\t"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void SecretMaskerTests_Encoder_UriDataEscape()
        {
            using var secretMasker = new SecretMasker();
            secretMasker.AddLiteralEncoder(LiteralEncoders.UriDataEscape);
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

            LiteralEncoder encoder = x => LiteralEncoders.UriDataEscape(x);

            using (var secretMasker = new SecretMasker())
            {
                secretMasker.AddLiteralEncoder(encoder);
                var value = String.Empty.PadRight(1, ' ');
                secretMasker.AddValue(value);
                Assert.Equal("***", secretMasker.MaskSecrets(value));
                Assert.Equal("***", secretMasker.MaskSecrets(value.Replace(" ", "%20")));
            }

            using (var secretMasker = new SecretMasker())
            {
                secretMasker.AddLiteralEncoder(encoder);
                var value = String.Empty.PadRight(2, ' ');
                secretMasker.AddValue(value);
                Assert.Equal("***", secretMasker.MaskSecrets(value));
                Assert.Equal("***", secretMasker.MaskSecrets(value.Replace(" ", "%20")));
            }

            using (var secretMasker = new SecretMasker())
            {
                secretMasker.AddLiteralEncoder(encoder);
                var value = String.Empty.PadRight(3, ' ');
                secretMasker.AddValue(value);
                Assert.Equal("***", secretMasker.MaskSecrets(value));
                Assert.Equal("***", secretMasker.MaskSecrets(value.Replace(" ", "%20")));
            }

            using (var secretMasker = new SecretMasker())
            {
                secretMasker.AddLiteralEncoder(encoder);
                var value = String.Empty.PadRight(4, ' ');
                secretMasker.AddValue(value);
                Assert.Equal("***", secretMasker.MaskSecrets(value));
                Assert.Equal("***", secretMasker.MaskSecrets(value.Replace(" ", "%20")));
            }

            using (var secretMasker = new SecretMasker())
            {
                secretMasker.AddLiteralEncoder(encoder);
                var value = String.Empty.PadRight(5, ' ');
                secretMasker.AddValue(value);
                Assert.Equal("***", secretMasker.MaskSecrets(value));
                Assert.Equal("***", secretMasker.MaskSecrets(value.Replace(" ", "%20")));
            }

            using (var secretMasker = new SecretMasker())
            {
                secretMasker.AddLiteralEncoder(encoder);
                var value = String.Empty.PadRight(5, ' ');
                secretMasker.AddValue(value);
                Assert.Equal("***", secretMasker.MaskSecrets(value));
                Assert.Equal("***", secretMasker.MaskSecrets(value.Replace(" ", "%20")));
            }

            using (var secretMasker = new SecretMasker())
            {
                secretMasker.AddLiteralEncoder(encoder);
                var value = String.Empty.PadRight(6, ' ');
                secretMasker.AddValue(value);
                Assert.Equal("***", secretMasker.MaskSecrets(value));
                Assert.Equal("***", secretMasker.MaskSecrets(value.Replace(" ", "%20")));
            }


            using (var secretMasker = new SecretMasker())
            {
                secretMasker.AddLiteralEncoder(encoder);
                var value = String.Empty.PadRight(7, ' ');
                secretMasker.AddValue(value);
                Assert.Equal("***", secretMasker.MaskSecrets(value));
                Assert.Equal("***", secretMasker.MaskSecrets(value.Replace(" ", "%20")));
            }

            using (var secretMasker = new SecretMasker())
            {
                secretMasker.AddLiteralEncoder(encoder);
                var value = "𐐷𐐷𐐷𐐷"; // surrogate pair
                secretMasker.AddValue(value);
                Assert.Equal("***", secretMasker.MaskSecrets(value));
            }

            using (var secretMasker = new SecretMasker())
            {
                secretMasker.AddLiteralEncoder(encoder);
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
            using var secretMasker = new SecretMasker();
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
            using var secretMasker = new SecretMasker();
            var expected = "abcdefg";
            var actual = secretMasker.MaskSecrets(expected);
            Assert.Equal(expected, actual);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void SecretMaskerTests_ReplacesValue()
        {
            using var secretMasker = new SecretMasker();
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
            using var secretMasker = new SecretMasker();
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
            using var secretMasker = new SecretMasker();
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
            using var secretMasker = new SecretMasker();
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
            using var secretMasker = new SecretMasker();
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
            using var secretMasker = new SecretMasker();
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
            using var secretMasker = new SecretMasker() { MinimumSecretLength = 9 };

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
            using var secretMasker = new SecretMasker { MinimumSecretLength = 9 };

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
            using var secretMasker = new SecretMasker();

            var minSecretLenFirst = 9;
            secretMasker.MinimumSecretLength = minSecretLenFirst;

            var minSecretLenSecond = 2;
            secretMasker.MinimumSecretLength = minSecretLenSecond;

            Assert.Equal(secretMasker.MinimumSecretLength, minSecretLenSecond);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void SecretMaskerTests_NegativeMinimumSecretLengthSet()
        {
            using var secretMasker = new SecretMasker() { MinimumSecretLength = -3 };
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
            using var secretMasker = new SecretMasker() { MinimumSecretLength = 3 };
            secretMasker.AddValue("efg");
            secretMasker.AddValue("bcd");

            var input = "abcdefgh";
            var result = secretMasker.MaskSecrets(input);

            Assert.Equal("a***h", result);

            secretMasker.MinimumSecretLength = 4;
            secretMasker.RemovePatternsThatDoNotMeetLengthLimits();

            var result2 = secretMasker.MaskSecrets(input);

            Assert.Equal(input, result2);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void SecretMaskerTests_RemoveShortSecretsBoundaryValues()
        {
            using var secretMasker = new SecretMasker();
            secretMasker.AddValue("bc");
            secretMasker.AddValue("defg");
            secretMasker.AddValue("h12");

            var input = "abcdefgh123";
            var result = secretMasker.MaskSecrets(input);

            Assert.Equal("a***3", result);

            secretMasker.MinimumSecretLength = 3;
            secretMasker.RemovePatternsThatDoNotMeetLengthLimits();

            var result2 = secretMasker.MaskSecrets(input);

            Assert.Equal("abc***3", result2);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void SecretMaskerTests_RemoveShortRegexes()
        {
            using var secretMasker = new SecretMasker();
            secretMasker.AddRegex(new RegexPattern("000", "Test", 0, "bc"));
            secretMasker.AddRegex(new RegexPattern("000", "Test", 0, "defg"));
            secretMasker.AddRegex(new RegexPattern("000", "Test", 0, "h12"));

            secretMasker.MinimumSecretLength = 3;
            secretMasker.RemovePatternsThatDoNotMeetLengthLimits();

            var input = "abcdefgh123";
            var result = secretMasker.MaskSecrets(input);

            Assert.Equal("abc+++3", result);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void SecretMaskerTests_RemoveEncodedSecrets()
        {
            using var secretMasker = new SecretMasker();
            secretMasker.AddValue("1");
            secretMasker.AddValue("2");
            secretMasker.AddValue("3");
            secretMasker.AddLiteralEncoder(new LiteralEncoder(x => x.Replace("1", "123")));
            secretMasker.AddLiteralEncoder(new LiteralEncoder(x => x.Replace("2", "45")));
            secretMasker.AddLiteralEncoder(new LiteralEncoder(x => x.Replace("3", "6789")));

            secretMasker.MinimumSecretLength = 3;
            secretMasker.RemovePatternsThatDoNotMeetLengthLimits();

            var input = "123456789";
            var result = secretMasker.MaskSecrets(input);

            Assert.Equal("***45***", result);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void SecretMaskerTests_NotAddShortEncodedSecrets()
        {
            using var secretMasker = new SecretMasker() { MinimumSecretLength = 3 };
            secretMasker.AddLiteralEncoder(new LiteralEncoder(x => x.Replace("123", "ab")));
            secretMasker.AddValue("123");
            secretMasker.AddValue("345");
            secretMasker.AddLiteralEncoder(new LiteralEncoder(x => x.Replace("345", "cd")));

            var input = "ab123cd345";
            var result = secretMasker.MaskSecrets(input);

            Assert.Equal("ab***cd***", result);
        }
    }
}