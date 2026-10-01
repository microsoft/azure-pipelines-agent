using Agent.Sdk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Microsoft.VisualStudio.Services.Agent.Util
{
    public sealed class SslUtil
    {
        private readonly ITraceWriter _trace;

        private readonly bool _ignoreCertificateErrors;

        public SslUtil(ITraceWriter trace, bool ignoreCertificateErrors = false)
        {
            this._trace = trace;
            this._ignoreCertificateErrors = ignoreCertificateErrors;
        }

        /// <summary>Implementation of custom callback function that logs SSL-related data from the web request to the agent's logs.</summary>
        /// <returns>`true` if web request was successful, otherwise `false`</returns>
        public bool RequestStatusCustomValidation(HttpRequestMessage requestMessage, X509Certificate2 certificate, X509Chain chain, SslPolicyErrors sslErrors)
        {
            bool isRequestSuccessful = (sslErrors == SslPolicyErrors.None);

            if (!isRequestSuccessful)
            {
                LoggingRequestDiagnosticData(requestMessage, certificate, chain, sslErrors);
            }

            if (this._ignoreCertificateErrors)
            {
                this._trace?.Info("Ignoring certificate errors.");
            }

            return this._ignoreCertificateErrors || isRequestSuccessful;
        }

        /// <summary>Logs SSL related data to agent's logs</summary>
        private void LoggingRequestDiagnosticData(HttpRequestMessage requestMessage, X509Certificate2 certificate, X509Chain chain, SslPolicyErrors sslErrors)
        {
            if (this._trace != null)
            {
                var logBuilder = new SslDiagnosticsLogBuilder();
                logBuilder.AddSslPolicyErrorsMessages(sslErrors);
                logBuilder.AddRequestMessageLog(requestMessage);
                logBuilder.AddCertificateLog(certificate);
                logBuilder.AddChainLog(chain);

                var formattedLog = logBuilder.BuildLog();

                this._trace?.Info($"Diagnostic data for request:{Environment.NewLine}{formattedLog}");
            }
        }
    }

    internal sealed class SslDiagnosticsLogBuilder
    {
        private readonly StringBuilder _logBuilder = new StringBuilder();

        /// <summary>A predefined list of headers to get from the request</summary>
        private static readonly string[] _requiredRequestHeaders = new[]
        {
            "X-TFS-Session",
            "X-VSS-E2EID",
            "User-Agent"
        };

        /// <summary>User-friendly description of SSL policy errors</summary>
        private static readonly Dictionary<SslPolicyErrors, string> _sslPolicyErrorsMapping = new Dictionary<SslPolicyErrors, string>
        {
            {SslPolicyErrors.None, "No SSL policy errors"},
            {SslPolicyErrors.RemoteCertificateChainErrors, "ChainStatus has returned a non empty array"},
            {SslPolicyErrors.RemoteCertificateNameMismatch, "Certificate name mismatch"},
            {SslPolicyErrors.RemoteCertificateNotAvailable, "Certificate not available"}
        };

        /// <summary>
        /// Add diagnostics data about the HTTP request.
        /// This method extracts common information about the request itself and the request's headers.
        /// To expand list of headers please update <see cref="_requiredRequestHeaders"/></summary>.
        public void AddRequestMessageLog(HttpRequestMessage requestMessage)
        {
            // Getting general information about request
            if (requestMessage is null)
            {
                _logBuilder.AppendLine($"HttpRequest data is empty");
                return;
            }


            var requestedUri = requestMessage?.RequestUri.ToString();
            var methodType = requestMessage?.Method.ToString();
            _logBuilder.AppendLine($"[HttpRequest]");
            _logBuilder.AppendLine($"Requested URI: {requestedUri}");
            _logBuilder.AppendLine($"Requested method: {methodType}");

            // Getting informantion from headers
            var requestHeaders = requestMessage?.Headers;

            if (requestHeaders is null || !requestHeaders.Any())
            {
                return;
            }

            _logBuilder.AppendLine($"[HttpRequestHeaders]");
            foreach (var headerKey in _requiredRequestHeaders)
            {
                IEnumerable<string> headerValues;

                if (requestHeaders.TryGetValues(headerKey, out headerValues))
                {
                    _logBuilder.AppendLine($"{headerKey}: {string.Join(", ", headerValues.ToArray())}");
                }
            }
        }

        /// <summary>
        /// Add diagnostics data about the certificate.
        /// This method extracts common information about the certificate.
        /// </summary>
        public void AddCertificateLog(X509Certificate2 certificate)
        {
            var diagInfo = new List<KeyValuePair<string, string>>();

            if (certificate is null)
            {
                _logBuilder.AppendLine($"Certificate data is empty");
                return;
            }

            _logBuilder.AppendLine($"[Certificate]");
            AddCertificateData(certificate);
        }

        /// <summary>
        /// Add diagnostics data about the chain.
        /// This method extracts common information about the chain.
        /// </summary>
        public void AddChainLog(X509Chain chain)
        {
            if (chain is null || chain.ChainElements is null)
            {
                _logBuilder.AppendLine($"ChainElements data is empty");
                return;
            }

            _logBuilder.AppendLine("[ChainElements]");
            foreach (var chainElement in chain.ChainElements)
            {
                AddCertificateData(chainElement.Certificate);
                foreach (var status in chainElement.ChainElementStatus)
                {
                    _logBuilder.AppendLine($"Status: {status.Status}");
                    _logBuilder.AppendLine($"Status Information: {status.StatusInformation}");
                }
            }
        }

        /// <summary>
        /// Add list of SSL policy errors with descriptions.
        /// This method checks SSL policy errors and mapping them to user-friendly descriptions.
        /// To update SSL policy errors description please update <see cref="_sslPolicyErrorsMapping"/>.
        /// </summary>
        public void AddSslPolicyErrorsMessages(SslPolicyErrors sslErrors)
        {
            _logBuilder.AppendLine($"[SSL Policy Errors]");

            if (sslErrors == SslPolicyErrors.None)
            {
                _logBuilder.AppendLine($"No SSL policy errors");
            }

            // Since we can get several SSL policy errors we should check all of them
            foreach (SslPolicyErrors errorCode in Enum.GetValues(typeof(SslPolicyErrors)))
            {
                if ((sslErrors & errorCode) != 0)
                {
                    if (!_sslPolicyErrorsMapping.ContainsKey(errorCode))
                    {
                        _logBuilder.AppendLine($"{errorCode.ToString()}: Could not resolve related error message");
                    }
                    else
                    {
                        _logBuilder.AppendLine($"{errorCode.ToString()}: {_sslPolicyErrorsMapping[errorCode]}");
                    }
                }
            }
        }

        public string BuildLog()
        {
            return _logBuilder.ToString();
        }


        private void AddCertificateData(X509Certificate2 certificate)
        {
            _logBuilder.AppendLine($"Effective date: {certificate?.GetEffectiveDateString()}");
            _logBuilder.AppendLine($"Expiration date: {certificate?.GetExpirationDateString()}");
            _logBuilder.AppendLine($"Issuer: {certificate?.Issuer}");
            _logBuilder.AppendLine($"Subject: {certificate?.Subject}");
            _logBuilder.AppendLine($"Thumbprint: {certificate?.Thumbprint}");
        }
    }
}
