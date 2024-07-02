
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System;
using ValueEncoder = Microsoft.TeamFoundation.DistributedTask.Logging.ValueEncoder;
using ISecretMaskerVSO = Microsoft.TeamFoundation.DistributedTask.Logging.ISecretMasker;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Agent.Sdk.SecretMasking
{
    /// <summary>
    /// Extended secret masker service, that allows to log origins of secrets
    /// </summary>
    public class LoggedSecretMasker : SecretMasker, ILoggedSecretMasker
    {
        private ITraceWriter _trace;

        private void Trace(string msg)
        {
            this._trace?.Info(msg);
        }

        public void SetTrace(ITraceWriter trace)
        {
            this._trace = trace;
        }

        /// <summary>
        /// Overloading of AddValue method with additional logic for logging origin of provided secret
        /// </summary>
        /// <param name="value">Secret to be added</param>
        /// <param name="origin">Origin of the secret</param>
        public void AddValue(string value, string origin)
        {
            this.Trace($"Setting up value for origin: {origin}");
            if (value == null)
            {
                this.Trace($"Value is empty.");
                return;
            }

            AddValue(value);
        }

        /// <summary>
        /// Overloading of AddRegex method with additional logic for logging origin of provided secret
        /// </summary>
        /// <param name="pattern"></param>
        /// <param name="origin"></param>
        public void AddRegex(string pattern, string origin, string moniker, ISet<string> sniffLiterals, RegexOptions regexOptions)
        {
            this.Trace($"Setting up regex for origin: {origin}.");
            if (pattern == null)
            {
                this.Trace($"Pattern is empty.");
                return;
            }

            AddRegex(pattern, moniker, sniffLiterals, regexOptions);
        }

        // We don't allow to skip secrets longer than 5 characters.
        // Note: the secret that will be ignored is of length n-1.
        public static int MinSecretLengthLimit => 6;

        public override int MinSecretLength
        {
            get
            {
                return base.MinSecretLength;
            }
            set
            {
                if (value > MinSecretLengthLimit)
                {
                    base.MinSecretLength = MinSecretLengthLimit;
                }
                else
                {
                    base.MinSecretLength = value;
                }
            }
        }

        public void RemoveShortSecretsFromDictionary()
        {
            this._trace?.Info("Removing short secrets from masking dictionary");
            base.RemoveShortSecretsFromDictionary();
        }

        /// <summary>
        /// Overloading of AddValueEncoder method with additional logic for logging origin of provided secret
        /// </summary>
        /// <param name="encoder"></param>
        /// <param name="origin"></param>
        public void AddValueEncoder(ValueEncoder encoder, string origin)
        {
            this.Trace($"Setting up value for origin: {origin}");
            this.Trace($"Length: {encoder.ToString().Length}.");
            if (encoder == null)
            {
                this.Trace($"Encoder is empty.");
                return;
            }

            AddValueEncoder(encoder);
        }

        ISecretMaskerVSO ISecretMaskerVSO.Clone() => this.Clone();

        public IDictionary<string, string> GetTelemetry()
        {
            var result = new Dictionary<string, string>
            {
                { nameof(ElapsedMaskingTime), ElapsedMaskingTime.ToString() },
            };

            result["Redactions"] = string.Join(',', ReplacementTokens);
            return result;
        }
    }
}
