// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
#if NET9_0_OR_GREATER
using System.Security.Cryptography.Pkcs;
#endif

namespace Agent.Sdk.Util
{
    public static class CertificateUtil
    {
        /// <summary>
        /// Loads an X509Certificate2 from a file, handling different certificate formats.
        /// Uses X509CertificateLoader for .NET 9+ and falls back to X509Certificate2 constructor for earlier versions.
        /// Supports: Cert (DER/PEM), PFX/PKCS#12, PKCS#7, SerializedCert, SerializedStore, and Authenticode formats.
        /// Note: SerializedCert, SerializedStore, and Authenticode are Windows-only and use legacy APIs.
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
                    return X509CertificateLoader.LoadCertificateFromFile(certificatePath);

                case X509ContentType.Pkcs12:
                    return X509CertificateLoader.LoadPkcs12FromFile(certificatePath, password);

                case X509ContentType.Pkcs7:
                    return LoadPkcs7Certificate(certificatePath);

                case X509ContentType.SerializedCert:
                case X509ContentType.Authenticode:
#pragma warning disable SYSLIB0057
                    return new X509Certificate2(certificatePath);
#pragma warning restore SYSLIB0057

                case X509ContentType.SerializedStore:
#pragma warning disable SYSLIB0057
                    var collection = new X509Certificate2Collection();
                    collection.Import(certificatePath);
                    if (collection.Count == 0)
                    {
                        throw new InvalidOperationException("The serialized store does not contain any certificates.");
                    }
                    return collection[0];
#pragma warning restore SYSLIB0057

                case X509ContentType.Unknown:
                default:
                    return X509CertificateLoader.LoadCertificateFromFile(certificatePath);
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

#if NET9_0_OR_GREATER
        /// <summary>
        /// Loads a certificate from a PKCS#7 file.
        /// </summary>
        private static X509Certificate2 LoadPkcs7Certificate(string certificatePath)
        {
            var signedCms = new SignedCms();
            signedCms.Decode(File.ReadAllBytes(certificatePath));

            if (signedCms.Certificates.Count == 0)
            {
                throw new InvalidOperationException("The PKCS#7 file does not contain any certificates.");
            }

            return signedCms.Certificates[0];
        }
#endif
    }
}
