// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Agent.Sdk.Util;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Util
{
    /// <summary>
    /// Tests for CertificateUtil.LoadCertificate which works on both .NET 8 and .NET 10.
    /// Tests cover: Cert (DER/PEM) and PFX/PKCS#12 formats.
    /// </summary>
    public sealed class CertificateUtilL0 : IDisposable
    {
        private readonly string _tempDir;

        public CertificateUtilL0()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"CertUtilTests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }

        #region PFX/PKCS#12 Tests (X509ContentType.Pkcs12)

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void LoadCertificate_Pfx_WithPassword_LoadsSuccessfully()
        {
            // Arrange
            var (expectedThumbprint, pfxPath) = CreatePfxCertificate("test-password");

            // Act
            using var loadedCert = CertificateUtil.LoadCertificate(pfxPath, "test-password");

            // Assert
            Assert.NotNull(loadedCert);
            Assert.Equal(expectedThumbprint, loadedCert.Thumbprint);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void LoadCertificate_Pfx_WithoutPassword_LoadsSuccessfully()
        {
            // Arrange
            var (expectedThumbprint, pfxPath) = CreatePfxCertificate(password: null);

            // Act
            using var loadedCert = CertificateUtil.LoadCertificate(pfxPath, password: null);

            // Assert
            Assert.NotNull(loadedCert);
            Assert.Equal(expectedThumbprint, loadedCert.Thumbprint);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void LoadCertificate_Pfx_WrongPassword_ThrowsException()
        {
            // Arrange
            var (_, pfxPath) = CreatePfxCertificate("correct-password");

            // Act & Assert
            Assert.ThrowsAny<CryptographicException>(() =>
                CertificateUtil.LoadCertificate(pfxPath, "wrong-password"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void LoadCertificate_Pfx_PasswordProtectedButNoPasswordProvided_ThrowsException()
        {
            // Arrange
            var (_, pfxPath) = CreatePfxCertificate("some-password");

            // Act & Assert
            Assert.ThrowsAny<CryptographicException>(() =>
                CertificateUtil.LoadCertificate(pfxPath, password: null));
        }

        #endregion

        #region DER Tests (X509ContentType.Cert)

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void LoadCertificate_Der_LoadsSuccessfully()
        {
            // Arrange
            var (expectedThumbprint, derPath) = CreateDerCertificate();

            // Act
            using var loadedCert = CertificateUtil.LoadCertificate(derPath);

            // Assert
            Assert.NotNull(loadedCert);
            Assert.Equal(expectedThumbprint, loadedCert.Thumbprint);
        }

        #endregion

        #region PEM Tests (X509ContentType.Cert)

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void LoadCertificate_Pem_LoadsSuccessfully()
        {
            // Arrange
            var (expectedThumbprint, pemPath) = CreatePemCertificate();

            // Act
            using var loadedCert = CertificateUtil.LoadCertificate(pemPath);

            // Assert
            Assert.NotNull(loadedCert);
            Assert.Equal(expectedThumbprint, loadedCert.Thumbprint);
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Creates a test PFX/PKCS#12 certificate file (X509ContentType.Pkcs12).
        /// </summary>
        private (string thumbprint, string path) CreatePfxCertificate(string password)
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=TestPfxCertificate",
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            using var cert = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddMinutes(-5),
                DateTimeOffset.UtcNow.AddYears(1));

            var pfxPath = Path.Combine(_tempDir, $"test_{Guid.NewGuid():N}.pfx");
            var pfxBytes = cert.Export(X509ContentType.Pfx, password);
            File.WriteAllBytes(pfxPath, pfxBytes);

            return (cert.Thumbprint, pfxPath);
        }

        /// <summary>
        /// Creates a test DER-encoded certificate file (X509ContentType.Cert).
        /// </summary>
        private (string thumbprint, string path) CreateDerCertificate()
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=TestDerCertificate",
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            using var cert = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddMinutes(-5),
                DateTimeOffset.UtcNow.AddYears(1));

            var derPath = Path.Combine(_tempDir, $"test_{Guid.NewGuid():N}.cer");
            var derBytes = cert.Export(X509ContentType.Cert);
            File.WriteAllBytes(derPath, derBytes);

            return (cert.Thumbprint, derPath);
        }

        /// <summary>
        /// Creates a test PEM-encoded certificate file.
        /// </summary>
        private (string thumbprint, string path) CreatePemCertificate()
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=TestPemCertificate",
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            using var cert = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddMinutes(-5),
                DateTimeOffset.UtcNow.AddYears(1));

            var pemPath = Path.Combine(_tempDir, $"test_{Guid.NewGuid():N}.pem");
            var pemContent = cert.ExportCertificatePem();
            File.WriteAllText(pemPath, pemContent);

            return (cert.Thumbprint, pemPath);
        }

        #endregion
    }
}