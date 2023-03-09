using Agent.Sdk.Util;
using Microsoft.TeamFoundation.DistributedTask.Logging;
using System;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    public class LoggedSecretMaskerL0 : IDisposable
    {
        SecretMasker _secretMasker;
        private bool disposedValue;

        public LoggedSecretMaskerL0()
        {
            _secretMasker = new SecretMasker();
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void LoggedSecretMasker_MaskingSecrets()
        {
            var lsm = new LoggedSecretMasker(_secretMasker)
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
            var lsm = new LoggedSecretMasker(_secretMasker)
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
            var lsm = new LoggedSecretMasker(_secretMasker)
            {
                MinSecretLength = 3
            };
            var inputMessage = "123456";

            lsm.AddValue("123");
            lsm.RemoveShortSecretsFromDictionary();
            var resultMessage = lsm.MaskSecrets(inputMessage);

            Assert.Equal("***456", resultMessage);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void LoggedSecretMasker_Skipping_ShortSecrets()
        {
            var lsm = new LoggedSecretMasker(_secretMasker)
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
        public void LoggedSecretMasker_Throws_Exception_If_Large_MinSecretLength_Specified()
        {
            var lsm = new LoggedSecretMasker(_secretMasker);

            Assert.Throws<ArgumentException>(() => lsm.MinSecretLength = 5);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void LoggedSecretMasker_Sets_MinSecretLength_To_MaxValue()
        {
            var lsm = new LoggedSecretMasker(_secretMasker);

            try { lsm.MinSecretLength = 5; }
            catch (ArgumentException) { }

            Assert.Equal(4, lsm.MinSecretLength);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void LoggedSecretMasker_NegativeValue_Passed()
        {
            var lsm = new LoggedSecretMasker(_secretMasker)
            {
                MinSecretLength = -2
            };
            var inputMessage = "12345";

            lsm.AddValue("1");
            var resultMessage = lsm.MaskSecrets(inputMessage);

            Assert.Equal("***2345", resultMessage);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                }

                _secretMasker.Dispose();
                _secretMasker = null;

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
