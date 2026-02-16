// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Linq;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using Microsoft.VisualStudio.Services.Common;

namespace Agent.Sdk
{
    public class AgentCertificateSettings
    {
        public bool SkipServerCertificateValidation { get; set; }
        public string CACertificateFile { get; set; }
        public string ClientCertificateFile { get; set; }
        public string ClientCertificatePrivateKeyFile { get; set; }
        public string ClientCertificateArchiveFile { get; set; }
        public string ClientCertificatePassword { get; set; }
        public IVssClientCertificateManager VssClientCertificateManager { get; set; }
    }

    public class AgentClientCertificateManager : IVssClientCertificateManager
    {
        private readonly X509Certificate2Collection _clientCertificates = new X509Certificate2Collection();
        public X509Certificate2Collection ClientCertificates => _clientCertificates;

        public AgentClientCertificateManager()
        {
        }

        public AgentClientCertificateManager(string clientCertificateArchiveFile, string clientCertificatePassword)
        {
            AddClientCertificate(clientCertificateArchiveFile, clientCertificatePassword);
        }

        public void AddClientCertificate(string clientCertificateArchiveFile, string clientCertificatePassword)
        {
            if (!string.IsNullOrEmpty(clientCertificateArchiveFile))
            {
#if NET9_0_OR_GREATER
                var contentType = X509Certificate2.GetCertContentType(clientCertificateArchiveFile);
                switch (contentType)
                {
                    case X509ContentType.Pkcs12:
                    case X509ContentType.Pfx:
                        _clientCertificates.Add(X509CertificateLoader.LoadPkcs12FromFile(clientCertificateArchiveFile, clientCertificatePassword));
                        break;
                    case X509ContentType.Pkcs7:
                        var signedCms = new SignedCms();
                        signedCms.Decode(File.ReadAllBytes(clientCertificateArchiveFile));
                        // Find end-entity certificate (non-CA), fallback to first certificate
                        var cert = signedCms.Certificates
                            .Cast<X509Certificate2>()
                            .FirstOrDefault(c =>
                            {
                                var bc = c.Extensions.OfType<X509BasicConstraintsExtension>().FirstOrDefault();
                                return bc == null || !bc.CertificateAuthority;
                            }) ?? signedCms.Certificates[0];
                        _clientCertificates.Add(cert);
                        break;
                    default:
                        _clientCertificates.Add(X509CertificateLoader.LoadCertificateFromFile(clientCertificateArchiveFile));
                        break;
                }
#else
                _clientCertificates.Add(new X509Certificate2(clientCertificateArchiveFile, clientCertificatePassword));
#endif
            }
        }
    }
}