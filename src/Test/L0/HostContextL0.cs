// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
            return new HostContext(
                hostType: HostType.Agent,
                logFile: Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), $"trace_{nameof(HostContextL0)}_{testName}.log"));
        }
    }
}
