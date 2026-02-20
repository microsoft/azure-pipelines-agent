using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Util
{
    public sealed class SslUtilL0
    {
        [Fact]
        public void AddSslPolicyErrorsMesssages_NoErrors_ShouldReturnCorrectLog()
        {
            // Arrange
            var logBuilder = new SslDiagnosticsLogBuilder();

            // Act
            logBuilder.AddSslPolicyErrorsMessages(SslPolicyErrors.None);
            var log = logBuilder.BuildLog();

            // Assert
            Assert.Contains("No SSL policy errors", log);
        }

        [Fact]
        public void AddSslPolicyErrorsMesssages_HasErrors_ShouldReturnCorrectLog()
        {
            // Arrange
            var logBuilder = new SslDiagnosticsLogBuilder();

            // Act
            logBuilder.AddSslPolicyErrorsMessages(SslPolicyErrors.RemoteCertificateNameMismatch);
            logBuilder.AddSslPolicyErrorsMessages(SslPolicyErrors.RemoteCertificateChainErrors);
            var log = logBuilder.BuildLog();

            // Assert
            Assert.Contains("ChainStatus has returned a non empty array", log);
            Assert.Contains("Certificate name mismatch", log);
        }

        [Fact]
        public void AddRequestMessageLog_RequestMessageIsNull_ShouldReturnCorrectLog()
        {
            // Arrange
            HttpRequestMessage requestMessage = null;
            var logBuilder = new SslDiagnosticsLogBuilder();

            // Act
            logBuilder.AddRequestMessageLog(requestMessage);
            var log = logBuilder.BuildLog();

            // Assert
            Assert.Equal($"HttpRequest data is empty{Environment.NewLine}", log);
        }

        [Fact]
        public void AddRequestMessageLog_RequestMessageIsNotNull_ShouldReturnCorrectLog()
        {
            // Arrange
            var requestMessage = new HttpRequestMessage(HttpMethod.Get, "http://localhost");
            var logBuilder = new SslDiagnosticsLogBuilder();
            var log = string.Empty;

            // Act
            using (requestMessage)
            {
                logBuilder.AddRequestMessageLog(requestMessage);
                log = logBuilder.BuildLog();
            }

            // Assert
            Assert.Contains("Requested URI: http://localhost/", log);
            Assert.Contains("Requested method: GET", log);
        }

        [Fact]
        public void AddRequestMessageLog_RequestMessageHasHeaders_ShouldReturnCorrectLog()
        {
            // Arrange
            var requestMessage = new HttpRequestMessage(HttpMethod.Get, "http://localhost");
            requestMessage.Headers.Add("X-TFS-Session", "value1");
            requestMessage.Headers.Add("X-VSS-E2EID", "value2_1");
            requestMessage.Headers.Add("X-VSS-E2EID", "value2_2");
            requestMessage.Headers.Add("User-Agent", "value3");
            requestMessage.Headers.Add("CustomHeader", "CustomValue");
            var logBuilder = new SslDiagnosticsLogBuilder();
            var log = string.Empty;

            // Act
            using (requestMessage)
            {
                logBuilder.AddRequestMessageLog(requestMessage);
                log = logBuilder.BuildLog();
            }

            // Assert
            Assert.Contains("Requested URI: http://localhost/", log);
            Assert.Contains("Requested method: GET", log);
            Assert.Contains("X-TFS-Session: value1", log);
            Assert.Contains("X-VSS-E2EID: value2_1, value2_2", log);
            Assert.Contains("User-Agent: value3", log);
            Assert.DoesNotContain("CustomHeader", log);
        }

        [Fact]
        public void AddCertificateLog_CertificateIsNull_ShouldReturnCorrectLog()
        {
            // Arrange
            var logBuilder = new SslDiagnosticsLogBuilder();

            // Act
            logBuilder.AddCertificateLog(null);
            var log = logBuilder.BuildLog();

            // Assert
            Assert.Equal($"Certificate data is empty{Environment.NewLine}", log);
        }

        [Fact]
        public void AddCertificateLog_CertificateIsNotNull_ShouldReturnCorrectLog()
        {
            // Arrange
            var certificate = GenerateTestCertificate();
            var logBuilder = new SslDiagnosticsLogBuilder();
            var log = string.Empty;

            // Act
            using (certificate)
            {
                logBuilder.AddCertificateLog(certificate);
                log = logBuilder.BuildLog();
            }

            // Assert
            Assert.Contains("Effective date: ", log);
            Assert.Contains("Expiration date: ", log);
            Assert.Contains("Subject: ", log);
            Assert.Contains("Issuer: ", log);
            Assert.Contains("Thumbprint: ", log);
        }

        [Fact]
        public void AddChainLog_ChainIsNull_ShouldReturnCorrectLog()
        {
            // Arrange
            var logBuilder = new SslDiagnosticsLogBuilder();

            // Act
            logBuilder.AddChainLog(null);
            var log = logBuilder.BuildLog();

            // Assert
            Assert.Equal($"ChainElements data is empty{Environment.NewLine}", log);
        }

        [Fact]
        public void AddChainLog_ChainIsNotNull_ShouldReturnCorrectLog()
        {
            // Arrange
            var certificate = GenerateTestCertificate();
            using var chain = new X509Chain();
            chain.Build(certificate);
            var logBuilder = new SslDiagnosticsLogBuilder();
            var log = string.Empty;

            // Act
            using (certificate)
            {
                logBuilder.AddChainLog(chain);
                log = logBuilder.BuildLog();
            }

            // Assert
            Assert.Contains("[ChainElements]", log);
            Assert.Contains("Effective date: ", log);
            Assert.Contains("Expiration date: ", log);
            Assert.Contains("Subject: ", log);
            Assert.Contains("Issuer: ", log);
            Assert.Contains("Thumbprint: ", log);
        }

        private X509Certificate2 GenerateTestCertificate()
        {
            using var ecdsa = ECDsa.Create(); // generate asymmetric key pair
            var req = new CertificateRequest("CN=TestSubject", ecdsa, HashAlgorithmName.SHA256);
            var cert = req.CreateSelfSigned(DateTimeOffset.Now, DateTimeOffset.Now.AddYears(5));

            return cert;
        }
    }
}
