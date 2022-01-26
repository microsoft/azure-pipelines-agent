using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Net.Http;
using Agent.Sdk;
using System.Collections.Generic;
using System.Linq;
using System;

namespace Microsoft.VisualStudio.Services.Agent.Util
{
    public sealed class SslUtil
    {
        private ITraceWriter _trace { get; set; }

        public SslUtil(ITraceWriter trace)
        {
            this._trace = trace;
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

            return isRequestSuccessful;
        }

        /// <summary>Logs SSL related data to agent's logs</summary>
        private void LoggingRequestDiagnosticData(HttpRequestMessage requestMessage, X509Certificate2 certificate, X509Chain chain, SslPolicyErrors sslErrors)
        {
            string diagInfo = "Diagnostic data for request:\n";

            if (this._trace != null)
            {
                diagInfo += SslDiagnosticDataProvider.ResolveSslPolicyErrorsMessage(sslErrors);
                diagInfo += SslDiagnosticDataProvider.GetRequestMessageData(requestMessage);
                diagInfo += SslDiagnosticDataProvider.GetCertificateData(certificate);

                this._trace?.Info(diagInfo);
            }
        }
    }

    public static class SslDiagnosticDataProvider
    {
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
        /// Get diagnostic data about the HTTP request.
        /// This method extracts common information about the request itself and the request's headers.
        /// To expand list of headers please update <see cref="_requiredRequestHeaders"/></summary>.
        /// <returns>Diagnostic data as a formatted string</returns>
        public static string GetRequestMessageData(HttpRequestMessage requestMessage)
        {
            // Getting general information about request
            string requestDiagInfoHeader = "HttpRequest";
            string diagInfo = string.Empty;

            if (requestMessage is null)
            {
                return $"{requestDiagInfoHeader} data is empty";
            }

            var requestDiagInfo = new List<KeyValuePair<string, string>>();

            var requestedUri = requestMessage?.RequestUri.ToString();
            var methodType = requestMessage?.Method.ToString();
            requestDiagInfo.Add(new KeyValuePair<string, string>("Requested URI", requestedUri));
            requestDiagInfo.Add(new KeyValuePair<string, string>("Request method", methodType));

            diagInfo = GetFormattedData(requestDiagInfoHeader, requestDiagInfo);

            // Getting informantion from headers
            var requestHeaders = requestMessage?.Headers;

            if (requestHeaders is null)
            {
                return diagInfo;
            }

            string headersDiagInfoHeader = "HttpRequestHeaders";

            var headersDiagInfo = new List<KeyValuePair<string, string>>();
            foreach (var headerKey in _requiredRequestHeaders)
            {
                IEnumerable<string> headerValues;

                if (requestHeaders.TryGetValues(headerKey, out headerValues))
                {
                    var headerValue = string.Join(", ", headerValues.ToArray());
                    if (headerValue != null)
                    {
                        headersDiagInfo.Add(new KeyValuePair<string, string>(headerKey, headerValue.ToString()));
                    }
                }
            }

            diagInfo += GetFormattedData(headersDiagInfoHeader, headersDiagInfo);

            return diagInfo;
        }

        /// <summary>
        /// Get diagnostic data about the certificate.
        /// This method extracts common information about the certificate.
        /// </summary>
        /// <returns>Diagnostic data as a formatted string</returns>
        public static string GetCertificateData(X509Certificate2 certificate)
        {
            string diagInfoHeader = "Certificate";
            var diagInfo = new List<KeyValuePair<string, string>>();

            if (certificate is null)
            {
                return $"{diagInfoHeader} data is empty";
            }

            diagInfo.Add(new KeyValuePair<string, string>("Effective date", certificate?.GetEffectiveDateString()));
            diagInfo.Add(new KeyValuePair<string, string>("Expiration date", certificate?.GetExpirationDateString()));
            diagInfo.Add(new KeyValuePair<string, string>("Issuer", certificate?.Issuer));
            diagInfo.Add(new KeyValuePair<string, string>("Subject", certificate?.Subject));

            return GetFormattedData(diagInfoHeader, diagInfo);
        }

        /// <summary>
        /// Get list of SSL policy errors with descriptions.
        /// This method checks SSL policy errors and mapping them to user-friendly descriptions.
        /// To update SSL policy errors description please update <see cref="_sslPolicyErrorsMapping"/>.
        /// </summary>
        /// <returns>Diagnostic data as a formatted string</returns>
        public static string ResolveSslPolicyErrorsMessage(SslPolicyErrors sslErrors)
        {
            string diagInfoHeader = $"SSL Policy Errors";
            var diagInfo = new List<KeyValuePair<string, string>>();

            if (sslErrors == SslPolicyErrors.None)
            {
                diagInfo.Add(new KeyValuePair<string, string>(sslErrors.ToString(), _sslPolicyErrorsMapping[sslErrors]));
                return GetFormattedData(diagInfoHeader, diagInfo);
            }

            // Since we can get several SSL policy errors we should check all of them
            foreach (SslPolicyErrors errorCode in Enum.GetValues(typeof(SslPolicyErrors)))
            {
                if ((sslErrors & errorCode) != 0)
                {
                    string errorValue = errorCode.ToString();
                    string errorMessage = string.Empty;

                    if (!_sslPolicyErrorsMapping.TryGetValue(errorCode, out errorMessage))
                    {
                        errorMessage = "Could not resolve related error message";
                    }

                    diagInfo.Add(new KeyValuePair<string, string>(errorValue, errorMessage));
                }
            }

            return GetFormattedData(diagInfoHeader, diagInfo);
        }

        /// <summary>Get diagnostic data as formatted text</summary>
        /// <returns>Formatted string</returns>
        private static string GetFormattedData(string diagInfoHeader, List<KeyValuePair<string, string>> diagInfo)
        {
            string formattedData = $"[{diagInfoHeader}]\n";

            foreach (var record in diagInfo)
            {
                formattedData += $"{record.Key}: {record.Value}\n";
            }

            return formattedData;
        }
    }
}
