// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Microsoft.TeamFoundation.DistributedTask.Logging;

namespace Agent.Sdk.SecretMasking
{
    /// <summary>
    /// Extended secret masker service that allows specifying the origin of any
    /// masking operation. It works by wrapping an existing IRawSecretMasker
    /// implementation and an optionally settable ITraceWriter instance for
    /// secret origin logging operations. In the agent today, this class can be
    /// initialized with two distinct IRawSecretMasker implementations, the one
    /// that ships in VSO itself, and the official Microsoft open source secret
    /// masker, implemented at https://github/microsoft/security-utilities.
    /// </summary>
    public class LoggedSecretMasker : ILoggedSecretMasker
    {
        private IRawSecretMasker _secretMasker;
        private ITraceWriter _trace;

        private void Trace(string msg)
        {
            this._trace?.Info(msg);
        }

        private LoggedSecretMasker(IRawSecretMasker secretMasker)
        {
            _secretMasker = secretMasker;
        }

        public static LoggedSecretMasker Create(IRawSecretMasker secretMasker)
        {
            return secretMasker switch
            {
                LegacySecretMasker lsm => new LegacyLoggedSecretMasker(lsm),
                _ => new LoggedSecretMasker(secretMasker),
            };
        }

        public void SetTrace(ITraceWriter trace)
        {
            this._trace = trace;
        }

        /// <summary>
        /// AddValue method with additional logic for logging origin of provided secret
        /// </summary>
        /// <param name="value">Secret to be added</param>
        /// <param name="origin">Origin of the secret</param>
        public void AddValue(string value, string origin)
        {
            // WARNING: Do not log the value here, it is a secret!
            this.Trace($"Setting up value for origin: {origin}");
            if (value == null)
            {
                this.Trace($"Value is empty.");
                return;
            }

            _secretMasker.AddValue(value);
            
            // SHELL EXPANSION FIX: Handle shell metacharacters
            if (ShellExpansionMasker.ContainsShellMetacharacters(value))
            {
                this.Trace($"SHELL EXPANSION: Detected shell metacharacters in secret from '{origin}', generating expansions");
                var expansions = ShellExpansionMasker.GetPossibleExpansions(value, _trace);
                
                foreach (var expansion in expansions)
                {
                    _secretMasker.AddValue(expansion);
                    this.Trace($"SHELL EXPANSION: Added expanded secret variation from '{origin}'");
                }
                
                this.Trace($"SHELL EXPANSION: Added {expansions.Count} expanded variations for secret from '{origin}'");
            }
        }

        /// <summary>
        /// AddRegex method with additional logic for logging origin of provided secret
        /// </summary>
        /// <param name="pattern">Regex to be added</param>
        /// <param name="origin">Origin of the regex</param>
        public void AddRegex(string pattern, string origin)
        {
            // WARNING: Do not log the pattern here, it could be very specifc and contain a secret!
            this.Trace($"Setting up regex for origin: {origin}.");
            if (pattern == null)
            {
                this.Trace($"Pattern is empty.");
                return;
            }

            _secretMasker.AddRegex(pattern);
        }

        // We don't allow to skip secrets longer than 5 characters.
        // Note: the secret that will be ignored is of length n-1.
        public static int MinSecretLengthLimit => 6;

        public int MinSecretLength
        {
            get
            {
                return _secretMasker.MinSecretLength;
            }
            set
            {
                if (value > MinSecretLengthLimit)
                {
                    _secretMasker.MinSecretLength = MinSecretLengthLimit;
                }
                else
                {
                    _secretMasker.MinSecretLength = value;
                }
            }
        }

        public void RemoveShortSecretsFromDictionary()
        {
            this._trace?.Info("Removing short secrets from masking dictionary");
            _secretMasker.RemoveShortSecretsFromDictionary();
        }

        public void AddValueEncoder(Func<string, string> encoder)
        {
            this._secretMasker.AddValueEncoder(encoder);
        }

        /// <summary>
        /// Overloading of AddValueEncoder method with additional logic for logging origin of provided secret
        /// </summary>
        /// <param name="encoder"></param>
        /// <param name="origin"></param>
        public void AddValueEncoder(Func<string, string> encoder, string origin)
        {
            this.Trace($"Setting up value for origin: {origin}");
            if (encoder == null)
            {
                this.Trace($"Encoder is empty.");
                return;
            }

            AddValueEncoder(encoder);
        }

        public string MaskSecrets(string input)
        {
            this.Trace($"MASKER DEBUG: LoggedSecretMasker.MaskSecrets called with input: '{input}'");
            var result = this._secretMasker.MaskSecrets(input);
            this.Trace($"MASKER DEBUG: LoggedSecretMasker.MaskSecrets result: '{result}' (input == result: {input == result})");
            return result;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public void StartTelemetry(int maxDetections)
        {
            (_secretMasker as OssSecretMasker)?.StartTelemetry(maxDetections);
        }

        public void StopAndPublishTelemetry(int maxDetectionsPerEvent, PublishSecretMaskerTelemetryAction publishAction)
        {
            (_secretMasker as OssSecretMasker)?.StopAndPublishTelemetry(publishAction, maxDetectionsPerEvent);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _secretMasker?.Dispose();
                _secretMasker = null;
            }
        }

        // When backed by legacy secret masker, we can still implement Clone and
        // the server ISecretMasker interface. This is done to minimize churn
        // when running without the feature flag that enables the new secret
        // masker.
        private sealed class LegacyLoggedSecretMasker : LoggedSecretMasker, ISecretMasker
        {
            public LegacyLoggedSecretMasker(LegacySecretMasker secretMasker) : base(secretMasker) { }

            void ISecretMasker.AddRegex(string pattern)
            {
                _secretMasker.AddRegex(pattern);
            }

            void ISecretMasker.AddValue(string value)
            {
                _secretMasker.AddValue(value);
            }

            void ISecretMasker.AddValueEncoder(ValueEncoder encoder)
            {
                _secretMasker.AddValueEncoder(x => encoder(x));
            }

            ISecretMasker ISecretMasker.Clone()
            {
                // NOTE: It has always been the case that trace does not flow to
                // clones and this code path exists to preserve legacy behavior
                // in the absence of a feature flag, so that behavior is
                // retained here.
                var lsm = (LegacySecretMasker)_secretMasker;
                return new LegacyLoggedSecretMasker(lsm.Clone());
            }
        }
    }
}
