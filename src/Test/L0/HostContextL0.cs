// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Security.Utilities;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    public sealed class HostContextL0
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void CreateServiceReturnsNewInstance()
        {
            // Arrange.
            using (var _hc = Setup())
            {
                // Act.
                var reference1 = _hc.CreateService<IAgentServer>();
                var reference2 = _hc.CreateService<IAgentServer>();

                // Assert.
                Assert.NotNull(reference1);
                Assert.IsType<AgentServer>(reference1);
                Assert.NotNull(reference2);
                Assert.IsType<AgentServer>(reference2);
                Assert.False(object.ReferenceEquals(reference1, reference2));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void GetServiceReturnsSingleton()
        {
            // Arrange.
            using (var _hc = Setup())
            {

                // Act.
                var reference1 = _hc.GetService<IAgentServer>();
                var reference2 = _hc.GetService<IAgentServer>();

                // Assert.
                Assert.NotNull(reference1);
                Assert.IsType<AgentServer>(reference1);
                Assert.NotNull(reference2);
                Assert.True(object.ReferenceEquals(reference1, reference2));
            }
        }

        private static readonly (string, string)[] _urlSecretCases = new[]
        {
            // Some URLs with secrets to mask.
            //
            // Note that even the new masker will use *** as the redaction token
            // because the URL pattern does not match high-entropy secrets for
            // which a C3ID can be computed.
            ("https://user:pass@example.com/path", "https://user:***@example.com/path"),
            ("http://user:pass@example.com/path", "http://user:***@example.com/path"),
            ("ftp://user:pass@example.com/path", "ftp://user:***@example.com/path"),
            ("https://user:pass@example.com/weird:thing@path", "https://user:***@example.com/weird:thing@path"),
            ("https://user:pass@example.com:8080/path", "https://user:***@example.com:8080/path"),
            ("https://user:pass@example.com:8080/path\nhttps://user2:pass2@example.com:8080/path", "https://user:***@example.com:8080/path\nhttps://user2:***@example.com:8080/path"),
            ("https://user@example.com:8080/path\nhttps://user2:pass2@example.com:8080/path", "https://user@example.com:8080/path\nhttps://user2:***@example.com:8080/path"),
            ("https://user:pass@example.com:8080/path\nhttps://user2@example.com:8080/path", "https://user:***@example.com:8080/path\nhttps://user2@example.com:8080/path"),

            // These two cases stress differences between the URL secret regex
            // in this repo and the one provided by the UrlCredentials pattern in
            // Microsoft.Security.Utilities.Core. We always use the one in this
            // repo but https://github.com/microsoft/security-utilities/issues/175
            // tracks reconciling this. This might land on changing the expected
            // behavior of these cases.
            ("ssh://user:pass@example.com/path", "ssh://user:***@example.com/path"),
            ("//user:pass@example.com/path", "//user:***@example.com/path"),

            // Some URLs without secrets to mask
            ("https://example.com/path", null),
            ("http://example.com/path", null),
            ("ftp://example.com/path", null),
            ("ssh://example.com/path", null),
            ("https://example.com/@path", null),
            ("https://example.com/weird:thing@path", null),
            ("https://example.com:8080/path", null),
        };

        public static readonly SecretCases UrlSecrets_NewMasker = new(_urlSecretCases, useNewMaskerAndRegexes: true);
        public static readonly SecretCases UrlSecrets_LegacyMasker = new(_urlSecretCases, useNewMaskerAndRegexes: false);

        [Theory]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        [MemberData(nameof(UrlSecrets_NewMasker))]
        public void UrlSecrets_NewMasker_Masked(string input, string expected)
        {
            TestSecretMasking(input, expected, useNewMaskerAndRegexes: true);
        }

        [Theory]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        [MemberData(nameof(UrlSecrets_LegacyMasker))]
        public void UrlSecrets_LegacyMasker_Masked(string input, string expected)
        {
            TestSecretMasking(input, expected, useNewMaskerAndRegexes: false);
        }

        private static readonly (string, string)[] _escapedSecretCases = new[]
        {
            // backslash escape
            ("before Mask\\This after", "before *** after"),
            ("before Mask\\\\This after", "before *** after"),
            // uri escape
            ("before Mask%20This after", "before *** after"),
            ("before Mask This after", "before *** after"),
            // json escape
            ("before Mask\tThis after", "before *** after"),
            ("before Mask\\tThis after", "before *** after"),
        };

        private static readonly string[] _unescapedSecretValues = new[]
        {
            @"Mask\This",
            @"Mask This",
            @"Mask\tThis",
        };

        public static readonly SecretCases EscapedSecrets_NewMasker = new(_escapedSecretCases, useNewMaskerAndRegexes: true);
        public static readonly SecretCases EscapedSecrets_LegacyMasker = new(_escapedSecretCases, useNewMaskerAndRegexes: false);

        [Theory]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        [MemberData(nameof(EscapedSecrets_NewMasker))]
        public void EscapedSecrets_NewMasker_Masked(string input, string expected)
        {
            TestSecretMasking(input, expected, useNewMaskerAndRegexes: true, _unescapedSecretValues);
        }

        [Theory]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        [MemberData(nameof(EscapedSecrets_LegacyMasker))]
        public void EscapedSecrets_LegacyMasker_Masked(string input, string expected)
        {
            TestSecretMasking(input, expected, useNewMaskerAndRegexes: false, _unescapedSecretValues);
        }

        private static readonly (string, string)[] _secretsRequiringNewMasker = new[]
        {
            // Some secrets that the new masker with its added regexes SHOULD
            // suppress, but the legacy masker will not suppress. NOTE: String
            // concat used to highlight signatures and avoid false positives
            // from push protection.
            ("deaddeaddeaddeaddeaddeaddeaddeadde/dead+deaddeaddeaddeaddeaddeaddeaddeaddead" + "APIM" + "do9bzQ==", "SEC101/181:AQYnVRHEp9bsvtiS75Hw"),
            ("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" + "ACDb" + "OpqrYA==", "SEC101/160:cgAuNarRt3XE67OyFKtT"),
            ("deaddeaddeaddeaddeaddeaddeaddeadde/dead+deaddeaddeaddeaddeaddeaddeaddeaddead" + "+ABa" + "cEmI0Q==", "SEC101/163:hV8JHmDwlzKVQLDQ4aVz"),
            ("deaddeaddeaddeaddeaddeaddeaddeadde/dead+deaddeaddeaddeaddeaddeaddeaddeaddead" + "+AMC" + "IBB+lg==", "SEC101/170:vGkdeeXzDdYpZG/P/N+U"),
            ("deaddeaddeaddeaddeaddeaddeaddeadde/dead+deaddeaddeaddeaddeaddeaddeaddeaddead" + "+ASt" + "aCQW6A==", "SEC101/152:iFwwHb6GCjF+WxbWkhIp"),
            ("deaddeaddeaddeaddeaddeaddeaddeaddeaddeaddead" + "AzFu" + "FakD8w==", "SEC101/158:DI3pIolg4mUyaYvnQJ9s"),
            ("deaddeaddeaddeaddeaddeaddeaddeaddeaddeadxx" + "AzSe" + "CyiycA", "SEC101/166:ws3fLn9rYjxet8tPxeei"),
            ("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" + "+ACR" + "C5W7f3", "SEC101/176:gfxbCiSbZlGd1NSqkoQg"),
            ("oy2" + "mdeaddeaddeadeadqdeaddeadxxxezodeaddeadwxuq", "SEC101/031:G47Z8IeLmqos+/TXkWoH"),
            ("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" + "AIoT" + "Oumzco=", "SEC101/178:oCE/hp1BfeSLXPJgMqTz"),
            ("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" + "+ASb" + "HpHeAI=", "SEC101/171:ujJlDjBUPI6u49AyMCXk"),
            ("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" + "+AEh" + "G2s/8w=", "SEC101/172:7aH00tlYEZcu0yhnxhm6"),
            ("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" + "+ARm" + "D7h+qo=", "SEC101/173:73UIu7xCGv6ofelm1yqH"),
            ("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" + "AzCa" + "JM04l8=", "SEC101/154:Elbi036ZI8k03jlXzG52"),
            ("xxx" + "8Q~" + "dead.dead.DEAD-DEAD-dead~deadxxxxx", "SEC101/156:vcocI2kI5E2ycoG55kza"),
            ("npm_" + "deaddeaddeaddeaddeaddeaddeaddeaddead", "SEC101/050:bUOMn/+Dx0jUK71D+nHu"),
            ("xxx" + "7Q~" + "dead.dead.DEAD-DEAD-dead~deadxx", "SEC101/156:WNRIG2TMMQjdUEGSNRIQ"),
            ("xxx" + "7Q~" + "dead.dead.DEAD-DEAD-dead~deadxx", "SEC101/156:WNRIG2TMMQjdUEGSNRIQ"),
        };

        public static readonly SecretCases SecretsRequiringNewMasker_NewMasker =
            new(_secretsRequiringNewMasker, useNewMaskerAndRegexes: true, requireNewMaskerAndRegexes: true);

        public static readonly SecretCases SecretsRequiringNewMasker_LegacyMasker =
            new(_secretsRequiringNewMasker, useNewMaskerAndRegexes: false, requireNewMaskerAndRegexes: true);

        [Theory]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        [MemberData(nameof(SecretsRequiringNewMasker_NewMasker))]
        public void SecretsRequiringNewMasker_NewMasker_Masked(string input, string expected)
        {
            TestSecretMasking(input, expected, useNewMaskerAndRegexes: true);
        }

        [Theory]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        [MemberData(nameof(SecretsRequiringNewMasker_LegacyMasker))]
        public void SecretsRequiringNewMasker_LegacyMasker_NotMasked(string input, string expected)
        {
            TestSecretMasking(input, expected, useNewMaskerAndRegexes: false);
        }

        private static readonly (string, string)[] _nonSecrets = new (string, string)[]
        {
            // Some non-secrets that the scanner should NOT suppress.
            ("SSdtIGEgY29tcGxldGVseSBpbm5vY3VvdXMgc3RyaW5nLg==", null),
            ("The password is knock knock knock", null),
        };

        public static readonly SecretCases NonSecrets_NewMasker = new(_nonSecrets, useNewMaskerAndRegexes: true);
        public static readonly SecretCases NonSecrets_LegacyMasker = new(_nonSecrets, useNewMaskerAndRegexes: false);

        [Theory]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        [MemberData(nameof(NonSecrets_NewMasker))]
        public void NonSecrets_NewMasker_NotMasked(string input, string expected)
        {
            TestSecretMasking(input, expected, useNewMaskerAndRegexes: true);
        }

        [Theory]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        [MemberData(nameof(NonSecrets_LegacyMasker))]
        public void NonSecrets_LegacyMasker_NotMasked(string input, string expected)
        {
            TestSecretMasking(input, expected, useNewMaskerAndRegexes: false);
        }

        [Theory]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        [MemberData(nameof(SecretsRequiringNewMasker_NewMasker))]
        public void UserSecretsThatMatchOSSRules_NewMasker_MaskWithStarsNotId(string secret, string expectedIfNotAlsoALiteral)
        {
            _ = expectedIfNotAlsoALiteral; // Unused since this is not the expectatation in this case.

            string input = $"The secret is '{secret}', mask it with stars even if it matches a rule.";
            string expected = "The secret is '***', mask it with stars even if it matches a rule.";

            TestSecretMasking(input,
                              expected,
                              useNewMaskerAndRegexes: true,
                              values: new[] { secret });
        }

        public sealed class SecretCases : TheoryData<string, string>
        {
            public SecretCases((string, string)[] cases, bool useNewMaskerAndRegexes, bool requireNewMaskerAndRegexes = false)
            {
                foreach ((string secret, string redaction) in cases)
                {
                    string expected;
                    if (redaction == null || (requireNewMaskerAndRegexes && !useNewMaskerAndRegexes))
                    {
                        expected = secret;
                    }
                    else
                    {
                        expected = redaction;
                    }
                    Add(secret, expected);
                }
            }
        }

        private void TestSecretMasking(string input, string expected, bool useNewMaskerAndRegexes, string[] values = null, [CallerMemberName] string testName = "")
        {
            // Arrange.
            try
            {
                Environment.SetEnvironmentVariable("AZP_ENABLE_NEW_MASKER_AND_REGEXES", useNewMaskerAndRegexes.ToString());

                using (var _hc = Setup(testName))
                {
                    if (values != null)
                    {
                        foreach (string value in values)
                        {
                            _hc.SecretMasker.AddValue(value, origin: "Test");
                        }
                    }

                    // Act.
                    var result = _hc.SecretMasker.MaskSecrets(input);

                    // Assert.
                    Assert.Equal(expected, result);
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("AZP_ENABLE_NEW_MASKER_AND_REGEXES", null);
            }
        }

        [Fact]
        public void LogFileChangedAccordingToEnvVariable()
        {
            try
            {
                var newPath = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), "logs");
                Environment.SetEnvironmentVariable("AGENT_DIAGLOGPATH", newPath);

                using (var _hc = new HostContext(HostType.Agent))
                {
                    // Act.
                    var diagFolder = _hc.GetDiagDirectory();

                    // Assert
                    Assert.Equal(Path.Combine(newPath, Constants.Path.DiagDirectory), diagFolder);
                    Directory.Exists(diagFolder);
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("AGENT_DIAGLOGPATH", null);
            }
        }

        public HostContext Setup([CallerMemberName] string testName = "")
        {
            var hc = new HostContext(
                hostType: HostType.Agent,
                logFile: Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), $"trace_{nameof(HostContextL0)}_{testName}.log"));
            return hc;
        }
    }
}
