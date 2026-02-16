// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if NET9_0_OR_GREATER
using System.IO;
using System.Linq;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace Agent.Sdk.Util
{
    public static class CertificateUtil
    {
        /// <summary>
        /// Loads an X509Certificate2 from a file, handling different certificate formats.
        /// For use in .NET 9+ where X509Certificate2 constructors are obsolete.
        /// </summary>
        /// <param name="certificatePath">Path to the certificate file</param>
        /// <param name="password">Optional password for PKCS#12/PFX files</param>
        /// <returns>The loaded X509Certificate2</returns>
        public static X509Certificate2 LoadCertificate(string certificatePath, string password = null)
        {
            var contentType = X509Certificate2.GetCertContentType(certificatePath);
            switch (contentType)
            {
                case X509ContentType.Pkcs12:
                case X509ContentType.Pfx:
                    return X509CertificateLoader.LoadPkcs12FromFile(certificatePath, password);
                case X509ContentType.Pkcs7:
                    return LoadPkcs7Certificate(certificatePath);
                default:
                    return X509CertificateLoader.LoadCertificateFromFile(certificatePath);
            }
        }

        /// <summary>
        /// Loads an end-entity certificate from a PKCS#7 file.
        /// </summary>
        private static X509Certificate2 LoadPkcs7Certificate(string certificatePath)
        {
            var signedCms = new SignedCms();
            signedCms.Decode(File.ReadAllBytes(certificatePath));

            // Find end-entity certificate (non-CA), fallback to first certificate
            return signedCms.Certificates
                .Cast<X509Certificate2>()
                .FirstOrDefault(c =>
                {
                    var bc = c.Extensions.OfType<X509BasicConstraintsExtension>().FirstOrDefault();
                    return bc == null || !bc.CertificateAuthority;
                }) ?? signedCms.Certificates[0];
        }
    }
}
#endif
