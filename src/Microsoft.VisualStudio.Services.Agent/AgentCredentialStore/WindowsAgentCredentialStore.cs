// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Microsoft.VisualStudio.Services.Agent
{
    // Windows credential store is per user.
    // This is a limitation for user configure the agent run as windows service, when user's current login account is different with the service run as account.
    // Ex: I login the box as domain\admin, configure the agent as windows service and run as domian\buildserver
    // domain\buildserver won't read the stored credential from domain\admin's windows credential store.
    // To workaround this limitation.
    // Anytime we try to save a credential:
    //   1. store it into current user's windows credential store 
    //   2. use DP-API do a machine level encrypt and store the encrypted content on disk.
    // At the first time we try to read the credential:
    //   1. read from current user's windows credential store, delete the DP-API encrypted backup content on disk if the windows credential store read succeed.
    //   2. if credential not found in current user's windows credential store, read from the DP-API encrypted backup content on disk, 
    //      write the credential back the current user's windows credential store and delete the backup on disk.
    [SupportedOSPlatform("windows")]
    public sealed class WindowsAgentCredentialStore : AgentService, IAgentCredentialStore
    {
        private string _credStoreFile;
        private Dictionary<string, string> _credStore;

        public override void Initialize(IHostContext hostContext)
        {
            base.Initialize(hostContext);

            _credStoreFile = hostContext.GetConfigFile(WellKnownConfigFile.CredentialStore);
            if (File.Exists(_credStoreFile))
            {
                _credStore = IOUtil.LoadObject<Dictionary<string, string>>(_credStoreFile);
            }
            else
            {
                _credStore = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        public void Write(string target, string username, string password)
        {
            Trace.Entering();
            ArgUtil.NotNullOrEmpty(target, nameof(target));
            ArgUtil.NotNullOrEmpty(username, nameof(username));
            ArgUtil.NotNullOrEmpty(password, nameof(password));

            // save to .credential_store file first, then Windows credential store
            string usernameBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(username));
            string passwordBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(password));

            // Base64Username:Base64Password -> DP-API machine level encrypt -> Base64Encoding
            string encryptedUsernamePassword = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes($"{usernameBase64}:{passwordBase64}"), null, DataProtectionScope.LocalMachine));
            Trace.Info($"Credentials for '{target}' written to credential store file.");
            _credStore[target] = encryptedUsernamePassword;

            // save to .credential_store file
            SyncCredentialStoreFile();

            // save to Windows Credential Store
            WriteInternal(target, username, password);
        }

        public NetworkCredential Read(string target)
        {
            Trace.Entering();
            ArgUtil.NotNullOrEmpty(target, nameof(target));
            IntPtr credPtr = IntPtr.Zero;
            try
            {
                if (CredRead(target, CredentialType.Generic, 0, out credPtr))
                {
                    Credential credStruct = (Credential)Marshal.PtrToStructure(credPtr, typeof(Credential));
                    int passwordLength = (int)credStruct.CredentialBlobSize;
                    string password = passwordLength > 0 ? Marshal.PtrToStringUni(credStruct.CredentialBlob, passwordLength / sizeof(char)) : String.Empty;
                    string username = Marshal.PtrToStringUni(credStruct.UserName);
                    Trace.Info($"Credentials for '{target}' read from windows credential store.");

                    // delete from .credential_store file since we are able to read it from windows credential store
                    if (_credStore.Remove(target))
                    {
                        Trace.Info($"Delete credentials for '{target}' from credential store file.");
                        SyncCredentialStoreFile();
                    }

                    return new NetworkCredential(username, password);
                }
                else
                {
                    // Can't read from Windows Credential Store, fail back to .credential_store file
                    if (_credStore.ContainsKey(target) && !string.IsNullOrEmpty(_credStore[target]))
                    {
                        Trace.Info($"Credentials for '{target}' read from credential store file.");

                        // Base64Decode -> DP-API machine level decrypt -> Base64Username:Base64Password -> Base64Decode
                        string decryptedUsernamePassword = Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(_credStore[target]), null, DataProtectionScope.LocalMachine));

                        string[] credential = decryptedUsernamePassword.Split(':');
                        if (credential.Length == 2 && !string.IsNullOrEmpty(credential[0]) && !string.IsNullOrEmpty(credential[1]))
                        {
                            string username = Encoding.UTF8.GetString(Convert.FromBase64String(credential[0]));
                            string password = Encoding.UTF8.GetString(Convert.FromBase64String(credential[1]));

                            // store back to windows credential store for current user
                            NetworkCredential creds = WriteInternal(target, username, password);

                            // delete from .credential_store file since we are able to write the credential to windows credential store for current user.
                            if (_credStore.Remove(target))
                            {
                                Trace.Info($"Delete credentials for '{target}' from credential store file.");
                                SyncCredentialStoreFile();
                            }

                            return creds;
                        }
                        else
                        {
                            throw new ArgumentOutOfRangeException(nameof(decryptedUsernamePassword));
                        }
                    }

                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"CredRead throw an error for '{target}'");
                }
            }
            finally
            {
                if (credPtr != IntPtr.Zero)
                {
                    CredFree(credPtr);
                }
            }
        }

        public (string UserName, string Password) Read2(string target)
        {
            // NetworkCredential objects cause crashes only on macOS => we can invoke the original implemantation here
            var ret = Read(target);
            return (ret.UserName, ret.Password);
        }

        public void Delete(string target)
        {
            Trace.Entering();
            ArgUtil.NotNullOrEmpty(target, nameof(target));

            // remove from .credential_store file
            if (_credStore.Remove(target))
            {
                Trace.Info($"Delete credentials for '{target}' from credential store file.");
                SyncCredentialStoreFile();
            }

            // remove from windows credential store
            if (!CredDelete(target, CredentialType.Generic, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Failed to delete credentials for {target}");
            }
            else
            {
                Trace.Info($"Credentials for '{target}' deleted from windows credential store.");
            }
        }

        private NetworkCredential WriteInternal(string target, string username, string password)
        {
            // save to Windows Credential Store
            Credential credential = new Credential()
            {
                Type = CredentialType.Generic,
                Persist = (UInt32)CredentialPersist.LocalMachine,
                TargetName = Marshal.StringToCoTaskMemUni(target),
                UserName = Marshal.StringToCoTaskMemUni(username),
                CredentialBlob = Marshal.StringToCoTaskMemUni(password),
                CredentialBlobSize = (UInt32)Encoding.Unicode.GetByteCount(password),
                AttributeCount = 0,
                Comment = IntPtr.Zero,
                Attributes = IntPtr.Zero,
                TargetAlias = IntPtr.Zero
            };

            try
            {
                if (CredWrite(ref credential, 0))
                {
                    Trace.Info($"Credentials for '{target}' written to windows credential store.");
                    return new NetworkCredential(username, password);
                }
                else
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new Win32Exception(error, "Failed to write credentials");
                }
            }
            finally
            {
                if (credential.CredentialBlob != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(credential.CredentialBlob);
                }
                if (credential.TargetName != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(credential.TargetName);
                }
                if (credential.UserName != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(credential.UserName);
                }
            }
        }

        private void SyncCredentialStoreFile()
        {
            Trace.Info("Sync in-memory credential store with credential store file.");

            // delete the cred store file first anyway, since it's a readonly file.
            IOUtil.DeleteFile(_credStoreFile);

            // delete cred store file when all creds gone
            if (_credStore.Count == 0)
            {
                return;
            }
            else
            {
                IOUtil.SaveObject(_credStore, _credStoreFile);
                File.SetAttributes(_credStoreFile, File.GetAttributes(_credStoreFile) | FileAttributes.Hidden);
            }
        }

        [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool CredDelete(string target, CredentialType type, int reservedFlag);

        [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool CredRead(string target, CredentialType type, int reservedFlag, out IntPtr CredentialPtr);

        [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool CredWrite([In] ref Credential userCredential, [In] UInt32 flags);

        [DllImport("Advapi32.dll", EntryPoint = "CredFree", SetLastError = true)]
        internal static extern bool CredFree([In] IntPtr cred);

        internal enum CredentialPersist : UInt32
        {
            Session = 0x01,
            LocalMachine = 0x02
        }

        internal enum CredentialType : uint
        {
            Generic = 0x01,
            DomainPassword = 0x02,
            DomainCertificate = 0x03
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct Credential
        {
            public UInt32 Flags;
            public CredentialType Type;
            public IntPtr TargetName;
            public IntPtr Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public UInt32 CredentialBlobSize;
            public IntPtr CredentialBlob;
            public UInt32 Persist;
            public UInt32 AttributeCount;
            public IntPtr Attributes;
            public IntPtr TargetAlias;
            public IntPtr UserName;
        }
    }
}
