using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace Microsoft.VisualStudio.Services.Agent.Listener
{
    /// <summary>
    /// Utility class that encapsulates security checks for the downloaded installer
    /// </summary>
    public static class InstallerVerifier
    {
        /// <summary>
        /// Extended key usage identifier
        /// </summary>
        public const string EXTENDED_KEY_USAGE = "2.5.29.37";

        /// <summary>
        /// The enhanced key usage OID that should be present in the certificate used to
        /// authenticode sign cabinet files and mpb.
        /// </summary>
        public const string CODE_SIGNING_ENHANCED_KEY_USAGE = "1.3.6.1.5.5.7.3.3";

        /// <summary>
        /// Utility method to verify that mp was signed by Microsoft
        /// </summary>
        /// <param name="filePath">Path to the file to check the signature on.</param>
        public static void VerifyFileSignedByMicrosoft(string filePath, Tracing trace, string expectedEKU = CODE_SIGNING_ENHANCED_KEY_USAGE)
        {
            // proceed with authenticode checks
            WinTrustData winTrustData = VerifyFileAuthenticodeSignatureHelper(filePath, trace);
            try
            {
                IntPtr pProviderData = UnsafeNativeMethods.WTHelperProvDataFromStateData(winTrustData.StateData);
                if (pProviderData == IntPtr.Zero)
                {
                    throw new Win32Exception(string.Format(CultureInfo.CurrentCulture, "File {0} WTHelperProvDataFromStateData returned null.", filePath));
                }

                IntPtr pSigner = UnsafeNativeMethods.WTHelperGetProvSignerFromChain(pProviderData, 0, false, 0);
                if (pSigner == IntPtr.Zero)
                {
                    throw new Win32Exception(string.Format(CultureInfo.CurrentCulture, "File {0} WTHelperGetProvSignerFromChain returned null.", filePath));
                }

                CRYPT_PROVIDER_SGNR provSigner = (CRYPT_PROVIDER_SGNR)Marshal.PtrToStructure(pSigner, typeof(CRYPT_PROVIDER_SGNR));

                CERT_CHAIN_POLICY_PARA policyPara = new CERT_CHAIN_POLICY_PARA(Marshal.SizeOf(typeof(CERT_CHAIN_POLICY_PARA)));
                CERT_CHAIN_POLICY_STATUS policyStatus = new CERT_CHAIN_POLICY_STATUS(Marshal.SizeOf(typeof(CERT_CHAIN_POLICY_STATUS)));

                if (!UnsafeNativeMethods.CertVerifyCertificateChainPolicy(
                                                            new IntPtr(UnsafeNativeMethods.CERT_CHAIN_POLICY_MICROSOFT_ROOT),
                                                            provSigner.pChainContext,
                                                            ref policyPara,
                                                            ref policyStatus))
                {
                    throw new Win32Exception(string.Format(CultureInfo.CurrentCulture, "File {0} CertVerifyCertificateChainPolicy wasn't able to check for the policy", filePath));
                }

                //If using SHA-2 validation the root certificate is different from the older SHA-1 certificate
                if (policyStatus.dwError != 0)
                {
                    policyPara.dwFlags = UnsafeNativeMethods.MICROSOFT_ROOT_CERT_CHAIN_POLICY_CHECK_APPLICATION_ROOT_FLAG;
                    if (!UnsafeNativeMethods.CertVerifyCertificateChainPolicy(
                                                                new IntPtr(UnsafeNativeMethods.CERT_CHAIN_POLICY_MICROSOFT_ROOT),
                                                                provSigner.pChainContext,
                                                                ref policyPara,
                                                                ref policyStatus))
                    {
                        throw new Win32Exception(string.Format(CultureInfo.CurrentCulture, "File {0} CertVerifyCertificateChainPolicy wasn't able to check for the policy", filePath));
                    }

                    if (policyStatus.dwError != 0)
                    {
#if DEBUG
                        {
                            policyPara.dwFlags = UnsafeNativeMethods.MICROSOFT_ROOT_CERT_CHAIN_POLICY_ENABLE_TEST_ROOT_FLAG;
                            if (!UnsafeNativeMethods.CertVerifyCertificateChainPolicy(
                                                                        new IntPtr(UnsafeNativeMethods.CERT_CHAIN_POLICY_MICROSOFT_ROOT),
                                                                        provSigner.pChainContext,
                                                                        ref policyPara,
                                                                        ref policyStatus))
                            {
                                throw new Win32Exception(string.Format(CultureInfo.CurrentCulture, "File {0} CertVerifyCertificateChainPolicy wasn't able to check for the policy", filePath));
                            }

                            if (policyStatus.dwError != 0)
                            {
                                trace.Error("policyStatus: " + policyStatus.ToString());
                                trace.Error("policyStatus.pvExtraPolicyStatus: " + policyStatus.pvExtraPolicyStatus);
                                trace.Error(String.Format("Error occurred while calling WinVerifyTrust: {0}", string.Format(CultureInfo.CurrentCulture, "File {0} does not have a valid MS or ms-test signature.", filePath)));
                                // throw new VerificationException(string.Format(CultureInfo.CurrentCulture, "File {0} does not have a valid MS or ms-test signature.", filePath));
                            }
                        }
#else
                        throw new VerificationException(string.Format(CultureInfo.CurrentCulture, "File {0} does not have a valid MS signature.", filePath));
#endif
                    }
                }

                trace.Info(String.Format("File {0} has a valid MS or ms-test signature.", filePath));

                // Get the certificate used to sign the file
                IntPtr pProviderCertificate = UnsafeNativeMethods.WTHelperGetProvCertFromChain(pSigner, 0);
                if (pProviderCertificate == IntPtr.Zero)
                {
                    throw new Win32Exception(string.Format(CultureInfo.CurrentCulture, "WTHelperGetProvCertFromChain returned null."));
                }

                CRYPT_PROVIDER_CERT provCert = (CRYPT_PROVIDER_CERT)Marshal.PtrToStructure(pProviderCertificate, typeof(CRYPT_PROVIDER_CERT));

                // Check for our EKU in the certificate
                using (X509Certificate2 x509Cert = new X509Certificate2(provCert.pCert))
                {
                    if (((X509EnhancedKeyUsageExtension)x509Cert.Extensions[EXTENDED_KEY_USAGE]).EnhancedKeyUsages[expectedEKU] == null)
                    {
                        // throw new exception
                        throw new VerificationException(string.Format(CultureInfo.CurrentCulture, "Authenticode signature for file {0} is not signed with a certificate containing the EKU {1}.", filePath, expectedEKU));
                    }

                    trace.Info(String.Format("Authenticode signature for file {0} is signed with a certificate containing the EKU {1}.", filePath, expectedEKU));
                }
            }
            finally
            {
                // dispose winTrustData object
                winTrustData.Dispose();
            }
        }

        /// <summary>
        /// Helper method to verify that published mp, mpb, or cabinet files have a valid authenticode signature
        /// </summary>
        /// <param name="filePath">Path to the file to check the signature on.</param>
        /// <returns>WinTrustData object</returns>
        private static WinTrustData VerifyFileAuthenticodeSignatureHelper(string filePath, Tracing trace)
        {
            WinTrustData trustData = null;
            WinTrustFileInfo fileInfo = new WinTrustFileInfo(filePath);
            WINTRUST_SIGNATURE_SETTINGS signatureSettings = null;
            WinVerifyTrustResult result;

            if (Utility.IsWin8OrAbove())
            {
                // On Windows 8 and above we have the APIs to enforce stronger checks
                const string szOID_CERT_STRONG_SIGN_OS_1 = "1.3.6.1.4.1.311.72.1.1"; //this specifies to enforce SHA-2 based hashes and other strong key requirements
                signatureSettings = new WINTRUST_SIGNATURE_SETTINGS(new CERT_STRONG_SIGN_PARA(szOID_CERT_STRONG_SIGN_OS_1));
                trustData = new Win8TrustData(fileInfo, signatureSettings);
            }
            else
            {
                // no signature settings
                trustData = new WinTrustData(filePath);
            }

            try
            {
                result = UnsafeNativeMethods.WinVerifyTrust(
                IntPtr.Zero,
                UnsafeNativeMethods.WINTRUST_ACTION_GENERIC_VERIFY_V2,
                trustData);


                if (result == WinVerifyTrustResult.FileNotSigned)
                {
                    throw new VerificationException(string.Format(CultureInfo.CurrentCulture, "File {0} does not have a valid authenticode signature.", filePath));
                }
                else if (result != WinVerifyTrustResult.Success)
                {
                    var winTrustResultErrorString = String.Format("{0} ({1})", GetVerboseWinVerifyTrustResultErrorString(result), ConvertWinVerifyTrustResultToHex(result));
                    throw new VerificationException(string.Format(CultureInfo.CurrentCulture, "WinVerifyTrustWrapper on file {0} failed with unexpected error: {1}", filePath, winTrustResultErrorString));
                }

            }
            catch (Exception ex)
            {
                trace.Error(String.Format("Error occurred while calling WinVerifyTrust: {0}", ex));

                // free all objects (trustData and signatureSettings)
                if (signatureSettings != null)
                {
                    signatureSettings.Dispose();
                }

                trustData.Dispose();
                throw;
            }

            trace.Info(String.Format("File {0} has a valid authenticode signature.", filePath));

            // only free signatureSettings
            if (signatureSettings != null)
            {
                signatureSettings.Dispose();

                // zero out the psignature pointer in trustData to be safe
                Marshal.FreeHGlobal(((Win8TrustData)trustData).pSignatureSettings);
                ((Win8TrustData)trustData).pSignatureSettings = IntPtr.Zero;
            }

            return trustData;
        }

        private static string GetVerboseWinVerifyTrustResultErrorString(WinVerifyTrustResult result)
        {
            switch (result)
            {
                case WinVerifyTrustResult.ActionUnknown:
                    return "Trust provider does not support the specified action";
                case WinVerifyTrustResult.FileNotSigned:
                    return "File was not signed";
                case WinVerifyTrustResult.ProviderUnknown:
                    return "Trust provider is not recognized on this system";
                case WinVerifyTrustResult.SubjectFormUnknown:
                    return "Trust provider does not support the form specified for the subject";
                case WinVerifyTrustResult.SubjectNotTrusted:
                    return "Subject failed the specified verification action";
                case WinVerifyTrustResult.UntrustedRootCert:
                    return "A certification chain processed correctly but terminated in a root certificate that is not trusted by the trust provider";
                default:
                    return "Unknown WinVerifyTrustResult value";
            }
        }

        private static string ConvertWinVerifyTrustResultToHex(WinVerifyTrustResult result)
        {
            return "0x" + result.ToString("X");
        }
    }
}
