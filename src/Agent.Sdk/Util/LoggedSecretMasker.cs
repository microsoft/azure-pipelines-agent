using Microsoft.TeamFoundation.DistributedTask.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Agent.Sdk.Util
{
    /// <summary>
    /// Extended secret masker service, that allows to log origins of secrets
    /// </summary>
    public class LoggedSecretMasker : ILoggedSecretMasker
    {
        private ISecretMasker _secretMasker;
        private ITraceWriter _trace;

        private void Trace(string msg)
        {
            this._trace?.Info(msg);
        }

        public LoggedSecretMasker(ISecretMasker secretMasker)
        {
            this._secretMasker = secretMasker;
        }

        public void SetTrace(ITraceWriter trace)
        {
            this._trace = trace;
        }

        public void AddValue(string pattern)
        {
            this._secretMasker.AddValue(pattern);
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

        public void AddRegex(string pattern)
        {
            this._secretMasker.AddRegex(pattern);
        }

        /// <summary>
        /// Overloading of AddRegex method with additional logic for logging origin of provided secret
        /// </summary>
        /// <param name="pattern"></param>
        /// <param name="origin"></param>
        public void AddRegex(string pattern, string origin)
        {
            this.Trace($"Setting up regex for origin: {origin}.");
            if (pattern == null)
            {
                this.Trace($"Pattern is empty.");
                return;
            }

            AddRegex(pattern);
        }

        public void AddValueEncoder(ValueEncoder encoder)
        {
            this._secretMasker.AddValueEncoder(encoder);
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

        public ISecretMasker Clone()
        {
            return new LoggedSecretMasker(this._secretMasker.Clone());
        }

        public string MaskSecrets(string input)
        {
            return this._secretMasker.MaskSecrets(input);
        }
    }
}
