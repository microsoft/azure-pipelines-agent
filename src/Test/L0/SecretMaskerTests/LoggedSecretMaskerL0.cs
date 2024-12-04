// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using Agent.Sdk.Util;
using System;
using Agent.Sdk.SecretMasking;
using Xunit;
using Microsoft.Security.Utilities;

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
            using var lsm = new LoggedSecretMasker(_secretMasker)
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
            using var lsm = new LoggedSecretMasker(_secretMasker)
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
            using var lsm = new LoggedSecretMasker(_secretMasker)
            {
                MinSecretLength = LoggedSecretMasker.MinSecretLengthLimit
            };
            var inputMessage = "1234567";

            lsm.AddValue("12345");
            var resultMessage = lsm.MaskSecrets(inputMessage);

            Assert.Equal("1234567", resultMessage);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void LoggedSecretMasker_ShortSecret_Removes_From_Dictionary_BoundaryValue2()
        {
            using var lsm = new LoggedSecretMasker(_secretMasker)
            {
                MinSecretLength = LoggedSecretMasker.MinSecretLengthLimit
            };
            var inputMessage = "1234567";

            lsm.AddValue("123456");
            var resultMessage = lsm.MaskSecrets(inputMessage);

            Assert.Equal("***7", resultMessage);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void LoggedSecretMasker_Skipping_ShortSecrets()
        {
            using var lsm = new LoggedSecretMasker(_secretMasker)
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
        public void LoggedSecretMasker_Sets_MinSecretLength_To_MaxValue()
        {
            using var lsm = new LoggedSecretMasker(_secretMasker);
            var expectedMinSecretsLengthValue = LoggedSecretMasker.MinSecretLengthLimit;

            lsm.MinSecretLength = LoggedSecretMasker.MinSecretLengthLimit + 1;

            Assert.Equal(expectedMinSecretsLengthValue, lsm.MinSecretLength);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "SecretMasker")]
        public void LoggedSecretMasker_NegativeValue_Passed()
        {
            using var lsm = new LoggedSecretMasker(_secretMasker)
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
