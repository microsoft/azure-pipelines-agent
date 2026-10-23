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
            var contentType = X509Certificate2.GetCertContentType(certificatePath);
            switch (contentType)
            {
                case X509ContentType.Cert:
                    // DER-encoded or PEM-encoded certificate
                    return X509CertificateLoader.LoadCertificateFromFile(certificatePath);

                case X509ContentType.Pkcs12:
                    // Note: X509ContentType.Pfx has the same value (3) as Pkcs12 refer: https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509contenttype?view=net-10.0
                    return X509CertificateLoader.LoadPkcs12FromFile(certificatePath, password);

                default:
                    // For all other formats (Pkcs7, SerializedCert, SerializedStore, Authenticode, Unknown),
                    // use the legacy constructor with warning suppression
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
