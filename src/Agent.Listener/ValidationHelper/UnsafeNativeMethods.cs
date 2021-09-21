using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32.SafeHandles;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace Microsoft.VisualStudio.Services.Agent.Listener
{
    /// <summary>
    /// Standard return values from the WinVerifyTrustWrapper API
    /// </summary>
    internal enum WinVerifyTrustResult : uint
    {
        Success = 0,
        ProviderUnknown = 0x800b0001, // The trust provider is not recognized on this system
        ActionUnknown = 0x800b0002, // The trust provider does not support the specified action
        SubjectFormUnknown = 0x800b0003, // The trust provider does not support the form specified for the subject
        SubjectNotTrusted = 0x800b0004, // The subject failed the specified verification action
        FileNotSigned = 0x800b0100,
        UntrustedRootCert = 0x800B0109 // A certificate chain processed, but terminated in a root certificate which is not trusted by the trust provider.
    }

    /// <summary>
    /// Structure provides information about a signer or countersigner
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CRYPT_PROVIDER_SGNR
    {
        public uint cbStruct;
        public ComTypes.FILETIME sftVerifyAsOf;
        public uint csCertChain;
        public IntPtr pasCertChain; // CRYPT_PROVIDER_CERT*              
        public uint dwSignerType;
        public IntPtr psSigner; // CMSG_SIGNER_INFO*              
        public uint dwError;
        public uint csCounterSigners;
        public IntPtr pasCounterSigners; // CRYPT_PROVIDER_SGNR*              
        public IntPtr pChainContext; // PCCERT_CHAIN_CONTEXT          
    }

    /// <summary>
    /// structure contains information used in CertVerifyCertificateChainPolicy 
    /// to establish policy criteria for the verification of certificate chains
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct CERT_CHAIN_POLICY_PARA
    {
        public uint cbSize;
        public uint dwFlags;
        public IntPtr pvExtraPolicyPara;

        public CERT_CHAIN_POLICY_PARA(int size)
        {
            this.cbSize = (uint)size;
            this.dwFlags = 0;
            this.pvExtraPolicyPara = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Structure holds certificate chain status information returned by the CertVerifyCertificateChainPolicy 
    /// function when the certificate chains are validated
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct CERT_CHAIN_POLICY_STATUS
    {
        public uint cbSize;
        public uint dwError;
        public IntPtr lChainIndex;
        public IntPtr lElementIndex;
        public IntPtr pvExtraPolicyStatus;

        public CERT_CHAIN_POLICY_STATUS(int size)
        {
            this.cbSize = (uint)size;
            this.dwError = 0;
            this.lChainIndex = IntPtr.Zero;
            this.lElementIndex = IntPtr.Zero;
            this.pvExtraPolicyStatus = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Interop structure for calling into CERT_STRONG_SIGN_PARA
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct CERT_STRONG_SIGN_PARA
    {
        public uint cbStruct;
        public uint dwInfoChoice;

        [MarshalAs(UnmanagedType.LPStr)]
        public string pszOID;

        public CERT_STRONG_SIGN_PARA(string oId)
        {
            this.cbStruct = (uint)Marshal.SizeOf(typeof(CERT_STRONG_SIGN_PARA));
            this.dwInfoChoice = 2; // CERT_STRONG_SIGN_OID_INFO_CHOICE
            this.pszOID = oId;
        }
    }

    /// <summary>
    /// Sub-structure of WinTrustData for verifying a file
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WinTrustFileInfo
    {
        public uint StructSize;
        public string filePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;

        public WinTrustFileInfo(string filePath)
        {
            this.filePath = filePath;
            this.StructSize = (uint)Marshal.SizeOf(typeof(WinTrustFileInfo));
            this.hFile = IntPtr.Zero;
            this.pgKnownSubject = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Structure provides information about a provider certificate
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CRYPT_PROVIDER_CERT
    {
        public uint cbStruct;
        public IntPtr pCert; // PCCERT_CONTEXT              
        public bool fCommercial;
        public bool fTrustedRoot;
        public bool fSelfSigned;
        public bool fTestCert;
        public uint dwRevokedReason;
        public uint dwConfidence;
        public uint dwError;
        public IntPtr pTrustListContext; // CTL_CONTEXT*              
        public bool fTrustListSignerCert;
        public IntPtr pCtlContext; // PCCTL_CONTEXT              
        public uint dwCtlError;
        public bool fIsCyclic;
        public IntPtr pChainElement; // PCERT_CHAIN_ELEMENT          
    }

    /// <summary>
    /// Defines pinvoke signature for functions that perform security checks
    /// </summary>
    internal static class UnsafeNativeMethods
    {
        public const uint CERT_CHAIN_POLICY_MICROSOFT_ROOT = 7;
        public const uint MICROSOFT_ROOT_CERT_CHAIN_POLICY_ENABLE_TEST_ROOT_FLAG = 0x00010000;
        public const uint MICROSOFT_ROOT_CERT_CHAIN_POLICY_CHECK_APPLICATION_ROOT_FLAG = 0x00020000;
        public static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        // Native method to validate that the file is signed by any authorized publisher
        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
        internal static extern WinVerifyTrustResult WinVerifyTrust(
            IntPtr hwnd,
            [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID,
            [In, Out] WinTrustData pWVTData);

        // Native method to validate that the file is signed by any authorized publisher
        [DllImport("wintrust.dll", EntryPoint = "WinVerifyTrust", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
        internal static extern WinVerifyTrustResult Win8VerifyTrust(
            IntPtr hwnd,
            [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID,
            [In, Out] Win8TrustData pWVTData);

        // Get the trust provider behind the file signature
        // returns CRYPT_PROVIDER_DATA*
        [DllImport("wintrust.dll", SetLastError = false, CharSet = CharSet.Unicode)]
        internal static extern IntPtr WTHelperProvDataFromStateData(IntPtr hStateData);

        // Get the signer from the trust provider
        // returns CRYPT_PROVIDER_SGNR*
        [DllImport("wintrust.dll", SetLastError = false, CharSet = CharSet.Unicode)]
        internal static extern IntPtr WTHelperGetProvSignerFromChain(
            IntPtr pProvData, // CRYPT_PROVIDER_DATA*                  
            uint idxSigner,
            bool fCounterSigner,
            uint idxCounterSigner);

        // Verify the cert chains up to the correct root
        [DllImport("crypt32.dll", CharSet = CharSet.Auto, SetLastError = false)]
        internal static extern bool CertVerifyCertificateChainPolicy(
            IntPtr pszPolicyOID,
            IntPtr pChainContext,
            ref CERT_CHAIN_POLICY_PARA pPolicyPara,
            [In, Out] ref CERT_CHAIN_POLICY_STATUS pPolicyStatus);

        // returns CRYPT_PROVIDER_CERT* 
        [DllImport("wintrust.dll", SetLastError = false, CharSet = CharSet.Unicode)]
        internal static extern IntPtr WTHelperGetProvCertFromChain(
            IntPtr pSgnr, // CRYPT_PROVIDER_SGNR*                  
            uint idxCert);
    }

    /// <summary>
    /// Safe handle for a file
    /// </summary>
    internal class FileInfoSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public FileInfoSafeHandle(WinTrustFileInfo info)
            : base(true)
        {
            this.SetHandle(Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WinTrustFileInfo))));
            Marshal.StructureToPtr(info, this.handle, false);
        }

        protected override bool ReleaseHandle()
        {
            if (this.handle != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(this.handle);
                this.handle = IntPtr.Zero;
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Structure used to specify the signatures on a file
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal class WINTRUST_SIGNATURE_SETTINGS : IDisposable
    {
        public uint cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_SIGNATURE_SETTINGS));
        public uint dwIndex = 0;
        public uint dwFlags = 0;
        public uint cSecondarySigs = 0;
        public uint dwVerifiedSigIndex = 0;
        public IntPtr pCryptoPolicy; // *CCERT_STRONG_SIGN_PARA

        public WINTRUST_SIGNATURE_SETTINGS(CERT_STRONG_SIGN_PARA strongSignParam)
        {
            this.pCryptoPolicy = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(CERT_STRONG_SIGN_PARA)));
            Marshal.StructureToPtr(strongSignParam, this.pCryptoPolicy, false);
        }

        ~WINTRUST_SIGNATURE_SETTINGS()
        {
            this.Dispose(false);
        }

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (this.pCryptoPolicy != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(this.pCryptoPolicy);
                this.pCryptoPolicy = IntPtr.Zero;
            }
        }
    }

    /// <summary>
    /// Interop structure for calling into WinVerifyTrustWrapper
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal class WinTrustData : IDisposable
    {
        public uint StructSize = (uint)Marshal.SizeOf(typeof(WinTrustData));
        public IntPtr PolicyCallbackData = IntPtr.Zero;
        public IntPtr SIPClientData = IntPtr.Zero;
        public uint UIChoice = 2;
        public uint RevocationChecks = 0x00000001; // WTD_REVOKE_WHOLECHAIN
        public uint UnionChoice = 1;
        public FileInfoSafeHandle pFile;
        public uint StateAction = 1; // WTD_STATEACTION_VERIFY
        public IntPtr StateData = IntPtr.Zero;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string URLReference;
        public uint ProvFlags = 0x00000080; // WTD_REVOCATION_CHECK_CHAIN_EXCLUDE_ROOT
        public uint UIContext = 0;

        public WinTrustData()
        {
        }

        public WinTrustData(string fileName)
        {
            WinTrustFileInfo fileInfo = new WinTrustFileInfo(fileName);
            this.pFile = new FileInfoSafeHandle(fileInfo);
        }

        ~WinTrustData()
        {
            this.Dispose(false);
        }

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            // Any hWVTStateData must be released by a call with close.
            if (this.StateData != IntPtr.Zero)
            {
                this.StateAction = 2; // WTD_STATEACTION_CLOSE

                if (Utility.IsWin8OrAbove())
                {
                    UnsafeNativeMethods.Win8VerifyTrust(
                        IntPtr.Zero,
                        UnsafeNativeMethods.WINTRUST_ACTION_GENERIC_VERIFY_V2,
                        (Win8TrustData)this);
                }
                else
                {
                    UnsafeNativeMethods.WinVerifyTrust(
                        IntPtr.Zero,
                        UnsafeNativeMethods.WINTRUST_ACTION_GENERIC_VERIFY_V2,
                        this);
                }
            }

            if (disposing)
            {
                // dispose the file handle
                if (this.pFile != null)
                {
                    this.pFile.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// Interop structure for passing signatures on Win 8 or Higher
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal class Win8TrustData : WinTrustData
    {
        public IntPtr pSignatureSettings; // WINTRUST_SIGNATURE_SETTINGS*

        public Win8TrustData(WinTrustFileInfo fileInfo, WINTRUST_SIGNATURE_SETTINGS signatureSettings)
        {
            this.StructSize = (uint)Marshal.SizeOf(typeof(Win8TrustData));
            this.pFile = new FileInfoSafeHandle(fileInfo);

            this.pSignatureSettings = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WINTRUST_SIGNATURE_SETTINGS)));
            Marshal.StructureToPtr(signatureSettings, this.pSignatureSettings, false);
        }

        ~Win8TrustData()
        {
            this.Dispose(false);
        }

        protected override void Dispose(bool disposing)
        {
            if (this.pSignatureSettings != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(this.pSignatureSettings);
                this.pSignatureSettings = IntPtr.Zero;
            }

            base.Dispose(disposing);
        }
    }
}
