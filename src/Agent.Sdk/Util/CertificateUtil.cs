// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Security.Cryptography.X509Certificates;

namespace Agent.Sdk.Util
{
    public static class CertificateUtil
    {
        /// <summary>
        /// Loads an X509Certificate2 from a file, handling different certificate formats.
        /// Uses X509CertificateLoader for .NET 9+ for Cert and Pkcs12 formats.
        /// For all other formats, uses the legacy constructor with warning suppression.
        /// </summary>
        /// <param name="certificatePath">Path to the certificate file</param>
        /// <param name="password">Optional password for PKCS#12/PFX files</param>
        /// <returns>The loaded X509Certificate2</returns>
        public static X509Certificate2 LoadCertificate(string certificatePath, string password = null)
        {
#if NET9_0_OR_GREATER
            System.Diagnostics.Debug.WriteLine("CertificateUtil: Using NET9+ code path (X509CertificateLoader)");
            var contentType = X509Certificate2.GetCertContentType(certificatePath);
            System.Diagnostics.Debug.WriteLine($"CertificateUtil: Certificate content type: {contentType}");
            switch (contentType)
            {
                case X509ContentType.Cert:
                    // DER-encoded or PEM-encoded certificate
                    System.Diagnostics.Debug.WriteLine("CertificateUtil: Loading as DER/PEM certificate using LoadCertificateFromFile");
                    return X509CertificateLoader.LoadCertificateFromFile(certificatePath);

                case X509ContentType.Pkcs12:
                    // Note: X509ContentType.Pfx has the same value (3) as Pkcs12 refer: https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509contenttype?view=net-10.0
                    System.Diagnostics.Debug.WriteLine("CertificateUtil: Loading as PFX/PKCS12 certificate using LoadPkcs12FromFile");
                    return X509CertificateLoader.LoadPkcs12FromFile(certificatePath, password);

                default:
                    // For all other formats (Pkcs7, SerializedCert, SerializedStore, Authenticode, Unknown),
                    // use the legacy constructor with warning suppression
                    System.Diagnostics.Debug.WriteLine($"CertificateUtil: Loading as {contentType} using legacy constructor");
#pragma warning disable SYSLIB0057
                    if (string.IsNullOrEmpty(password))
                    {
                        return new X509Certificate2(certificatePath);
                    }
                    else
                    {
                        return new X509Certificate2(certificatePath, password);
                    }
#pragma warning restore SYSLIB0057
            }
#else 
            // For .NET 8 and earlier, use the traditional constructor
            // The constructor automatically handles all certificate types
            System.Diagnostics.Debug.WriteLine("CertificateUtil: Using legacy code path (X509Certificate2 constructor) for NET8 or earlier");
            if (string.IsNullOrEmpty(password))
            {
                return new X509Certificate2(certificatePath);
            }
            else
            {
                return new X509Certificate2(certificatePath, password);
            }
#endif
        }
    }
}
