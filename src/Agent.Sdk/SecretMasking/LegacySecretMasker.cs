// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System;
using Microsoft.TeamFoundation.DistributedTask.Logging;

namespace Agent.Sdk.SecretMasking
{
    /// <summary>
    /// Legacy secret masker that dispatches to <see cref="SecretMasker"/> from
    /// 'Microsoft.TeamFoundation.DistributedTask.Logging'.
    /// </summary>
    public sealed class LegacySecretMasker : IRawSecretMasker
    {
        private ISecretMasker _secretMasker;

        public LegacySecretMasker()
        {
            _secretMasker = new SecretMasker();
        }

        private LegacySecretMasker(ISecretMasker secretMasker)
        {
            _secretMasker = secretMasker;
        }

        public int MinSecretLength
        {
            get => _secretMasker.MinSecretLength;
            set => _secretMasker.MinSecretLength = value;
        }

        public void AddRegex(string pattern)
        {
            _secretMasker.AddRegex(pattern);
        }

        public void AddValue(string value)
        {
            _secretMasker.AddValue(value);
        }

        public void AddValueEncoder(Func<string, string> encoder)
        {
            _secretMasker.AddValueEncoder(x => encoder(x));
        }

        public void Dispose()
        {
            (_secretMasker as IDisposable)?.Dispose();
            _secretMasker = null;
        }

        public string MaskSecrets(string input)
        {
            return _secretMasker.MaskSecrets(input);
        }

        public void RemoveShortSecretsFromDictionary()
        {
            _secretMasker.RemoveShortSecretsFromDictionary();
        }

        public LegacySecretMasker Clone()
        {
            return new LegacySecretMasker(_secretMasker.Clone());
        }
    }
}
