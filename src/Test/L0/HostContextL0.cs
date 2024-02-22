// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
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

        [Theory]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        // some URLs with secrets to mask
        [InlineData("https://user:pass@example.com/path", "https://user:***@example.com/path")]
        [InlineData("http://user:pass@example.com/path", "http://user:***@example.com/path")]
        [InlineData("ftp://user:pass@example.com/path", "ftp://user:***@example.com/path")]
        [InlineData("https://user:pass@example.com/weird:thing@path", "https://user:***@example.com/weird:thing@path")]
        [InlineData("https://user:pass@example.com:8080/path", "https://user:***@example.com:8080/path")]
        [InlineData("https://user:pass@example.com:8080/path\nhttps://user2:pass2@example.com:8080/path", "https://user:***@example.com:8080/path\nhttps://user2:***@example.com:8080/path")]
        [InlineData("https://user@example.com:8080/path\nhttps://user2:pass2@example.com:8080/path", "https://user@example.com:8080/path\nhttps://user2:***@example.com:8080/path")]
        [InlineData("https://user:pass@example.com:8080/path\nhttps://user2@example.com:8080/path", "https://user:***@example.com:8080/path\nhttps://user2@example.com:8080/path")]
        // some URLs without secrets to mask
        [InlineData("https://example.com/path", "https://example.com/path")]
        [InlineData("http://example.com/path", "http://example.com/path")]
        [InlineData("ftp://example.com/path", "ftp://example.com/path")]
        [InlineData("ssh://example.com/path", "ssh://example.com/path")]
        [InlineData("https://example.com/@path", "https://example.com/@path")]
        [InlineData("https://example.com/weird:thing@path", "https://example.com/weird:thing@path")]
        [InlineData("https://example.com:8080/path", "https://example.com:8080/path")]
        public void UrlSecretsAreMasked(string input, string expected)
        {
            // Arrange.
            using (var _hc = Setup())
            {
                // Act.
                var result = _hc.SecretMasker.MaskSecrets(input);

                // Assert.
                Assert.Equal(expected, result);
            }
        }

        [Theory]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        // Some secrets that the scanner SHOULD suppress.
        [InlineData("deaddeaddeaddeaddeaddeaddeaddeadde/dead+deaddeaddeaddeaddeaddeaddeaddeaddeadAPIMxxxxxQ==", "SEC102/102.Unclassified64ByteBase64String:1DC39072DA446911FE3E87EB697FB22ED6E2F75D7ECE4D0CE7CF4288CE0094D1")]
        [InlineData("deaddeaddeaddeaddeaddeaddeaddeadde/dead+deaddeaddeaddeaddeaddeaddeaddeaddeadACDbxxxxxQ==", "SEC102/102.Unclassified64ByteBase64String:6AB186D06C8C6FBA25D39806913A70A4D77AB97C526D42B8C8DA6D441DE9F3C5")]
        [InlineData("deaddeaddeaddeaddeaddeaddeaddeadde/dead+deaddeaddeaddeaddeaddeaddeaddeaddead+ABaxxxxxQ==", "SEC102/102.Unclassified64ByteBase64String:E1BB911668718D50C0C2CE7B9C93A5BB75A17212EA583A8BB060A014058C0802")]
        [InlineData("deaddeaddeaddeaddeaddeaddeaddeadde/dead+deaddeaddeaddeaddeaddeaddeaddeaddead+AMCxxxxxQ==", "SEC102/102.Unclassified64ByteBase64String:7B3706299058BAC1622245A964D8DBBEF97A0C43C863F2702C4A1AD0413B3FC9")]
        [InlineData("deaddeaddeaddeaddeaddeaddeaddeadde/dead+deaddeaddeaddeaddeaddeaddeaddeaddead+AStxxxxxQ==", "SEC102/102.Unclassified64ByteBase64String:58FF6B874E1B4014CF17C429A1E235E08466A0199090A0235975A35A87B8D440")]
        [InlineData("deaddeaddeaddeaddeaddeaddeaddeaddeaddeaddeadAzFuxdeadQ==", "SEC101/158.AzureFunctionIdentifiableKey:FF8E9A7C2A792029814C755C6704D9427F302E954DEF0FD5EE649BF9163E1F24")]
        [InlineData("deaddeaddeaddeaddeaddeaddeaddeaddeaddeadxxAzSeDeadxx", "SEC101/167.AzureSearchIdentifiableKey:EAEC92AA13ECA43594A8FEED69D8B7F4696569E990718DCBE1B3872634540670")]
        [InlineData("deaddeaddeaddeaddeaddeaddeaddeaddeaddeadde+ACRDeadxx", "SEC101/176.AzureContainerRegistryIdentifiableKey:CE62C55A2D3C220DA0CBFE292B5A6839EC7F747C5B5A7A55A4E5D7D76F1C7D32")]
        [InlineData("oy2mdeaddeaddeadeadqdeaddeadxxxezodeaddeadwxuq", "SEC101/031.NuGetApiKey:FC93CD537067C7F452073F24C7043D5F58E11B6F49546316BBE06BAA5747317E")]
        [InlineData("deaddeaddeaddeaddeaddeaddeaddeadxAIoTDeadxx=", "SEC102/101.Unclassified32ByteBase64String:2B0ADEB74FC9CDA3CD5D1066D85190407C57B8CAF45FCA7D50E26282AD61530C")]
        [InlineData("deaddeaddeaddeaddeaddeaddeaddeadx+ASbDeadxx=", "SEC102/101.Unclassified32ByteBase64String:83F68F21FC0D7C5990929446509BFF80D604899064CA152D3524BBEECF7F6993")]
        [InlineData("deaddeaddeaddeaddeaddeaddeaddeadx+AEhDeadxx=", "SEC102/101.Unclassified32ByteBase64String:E636DCD8D5F02304CE4B24DE2344B2D24C4B46BFD062EEF4D7673227720351C9")]
        [InlineData("deaddeaddeaddeaddeaddeaddeaddeadx+ARmDeadxx=", "SEC102/101.Unclassified32ByteBase64String:9DEFFD24DE5F1DB24292B814B01868BC33E9298DF2BF3318C2B063B4D689A0BC")]
        [InlineData("deaddeaddeaddeaddeaddeaddeaddeaddAzCaDeadxx=", "SEC101/154.AzureCacheForRedisIdentifiableKey:29894C9E3F5B60A1477AB08ABAE127152FAA20DD36C162B0FF21F16EF19233E5")]
        [InlineData("npm_deaddeaddeaddeaddeaddeaddeaddeaddead", "SEC101/050.NpmAuthorKey:E06C20B8696373D4AEE3057CB1A577DC7A0F7F97BEE352D3C49B48B6328E1CBC")]
        [InlineData("xxx8Q~dead.dead.DEAD-DEAD-dead~deadxxxxx", "SEC101/156.AadClientAppSecret:44DB247A273E912A1C3B45AC2732734CEAED00508AB85C3D4E801596CFF5B1D8")]
        [InlineData("xxx7Q~dead.dead.DEAD-DEAD-dead~deadxx", "SEC101/156.AadClientAppSecret:23F12851970BB19BD76A448449F16F85BF4AFE915AD14BAFEE635F15021CE6BB")]
        // Some secrets that the scanner should NOT suppress.
        [InlineData("SSdtIGEgY29tcGxldGVseSBpbm5vY3VvdXMgc3RyaW5nLg==", null)]
        [InlineData("The password is knock knock knock", null)]
        public void OtherSecretsAreMasked(string input, string expectedValue)
        {
            // Arrange.

            foreach (string knobValue in new[] { "true", null })
            {
                string expected = expectedValue;

                // A null value in expected means we expect the input pattern
                // to be returned (as it is unmasked). When our knob is null,
                // indicating use of the legacy masker, we always expect "***"
                // when masking. The new masker emits a redaction token that
                // contains a security model id and the hash of the redacted thing.
                expected = knobValue == null
                    ? expected == null ? input : "***"
                    : expected == null ? input : expected;

                try
                {
                    Environment.SetEnvironmentVariable("AZP_ENABLE_NEW_SECRET_MASKER", knobValue);

                    using (var _hc = Setup())
                    {
                        // Act.
                        var result = _hc.SecretMasker.MaskSecrets(input);

                        // Assert.
                        Assert.Equal(expected, result);
                    }
                }
                finally
                {
                    Environment.SetEnvironmentVariable("AZP_ENABLE_NEW_SECRET_MASKER", null);
                }
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
            hc.AddAdditionalMaskingRegexes();
            return hc;
        }
    }
}
