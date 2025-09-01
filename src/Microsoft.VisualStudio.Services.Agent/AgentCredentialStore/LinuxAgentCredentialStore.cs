// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using Microsoft.VisualStudio.Services.Agent.Util;
using System.IO;
using System.Runtime.Serialization;
using System.Security.Cryptography;

namespace Microsoft.VisualStudio.Services.Agent
{
    public sealed class LinuxAgentCredentialStore : AgentService, IAgentCredentialStore
    {
        // 'msftvsts' 128 bits iv
        private readonly byte[] iv = new byte[] { 0x36, 0x64, 0x37, 0x33, 0x36, 0x36, 0x37, 0x34, 0x37, 0x36, 0x37, 0x33, 0x37, 0x34, 0x37, 0x33 };

        // 256 bits key
        private byte[] _symmetricKey;
        private string _credStoreFile;
        private Dictionary<string, Credential> _credStore;

        public override void Initialize(IHostContext hostContext)
        {
            base.Initialize(hostContext);

            _credStoreFile = hostContext.GetConfigFile(WellKnownConfigFile.CredentialStore);
            if (File.Exists(_credStoreFile))
            {
                _credStore = IOUtil.LoadObject<Dictionary<string, Credential>>(_credStoreFile);
            }
            else
            {
                _credStore = new Dictionary<string, Credential>(StringComparer.OrdinalIgnoreCase);
            }

            string machineId;
            if (File.Exists("/etc/machine-id"))
            {
                // try use machine-id as encryption key
                // this helps avoid accidental information disclosure, but isn't intended for true security
                machineId = File.ReadAllLines("/etc/machine-id").FirstOrDefault();
                Trace.Info($"machine-id length {machineId?.Length ?? 0}.");

                // machine-id doesn't exist or machine-id is not 256 bits
                if (string.IsNullOrEmpty(machineId) || machineId.Length != 32)
                {
                    Trace.Warning("Can not get valid machine id from '/etc/machine-id'.");
                    machineId = "5f767374735f6167656e745f63726564"; //_vsts_agent_cred
                }
            }
            else
            {
                // /etc/machine-id not exist
                Trace.Warning("/etc/machine-id doesn't exist.");
                machineId = "5f767374735f6167656e745f63726564"; //_vsts_agent_cred
            }

            List<byte> keyBuilder = new List<byte>();
            foreach (var c in machineId)
            {
                keyBuilder.Add(Convert.ToByte(c));
            }

            _symmetricKey = keyBuilder.ToArray();
        }

        public NetworkCredential Write(string target, string username, string password)
        {
            Trace.Entering();
            ArgUtil.NotNullOrEmpty(target, nameof(target));
            ArgUtil.NotNullOrEmpty(username, nameof(username));
            ArgUtil.NotNullOrEmpty(password, nameof(password));

            Trace.Info($"Store credential for '{target}' to cred store.");
            Credential cred = new Credential(username, Encrypt(password));
            _credStore[target] = cred;
            SyncCredentialStoreFile();
            return new NetworkCredential(username, password);
        }

        public NetworkCredential Read(string target)
        {
            Trace.Entering();
            ArgUtil.NotNullOrEmpty(target, nameof(target));
            Trace.Info($"Read credential for '{target}' from cred store.");
            if (_credStore.ContainsKey(target))
            {
                Credential cred = _credStore[target];
                if (!string.IsNullOrEmpty(cred.UserName) && !string.IsNullOrEmpty(cred.Password))
                {
                    Trace.Info($"Return credential for '{target}' from cred store.");
                    return new NetworkCredential(cred.UserName, Decrypt(cred.Password));
                }
            }

            throw new KeyNotFoundException(target);
        }

        public void Delete(string target)
        {
            Trace.Entering();
            ArgUtil.NotNullOrEmpty(target, nameof(target));

            if (_credStore.ContainsKey(target))
            {
                Trace.Info($"Delete credential for '{target}' from cred store.");
                _credStore.Remove(target);
                SyncCredentialStoreFile();
            }
            else
            {
                throw new KeyNotFoundException(target);
            }
        }

        private void SyncCredentialStoreFile()
        {
            Trace.Entering();
            Trace.Info("Sync in-memory credential store with credential store file.");

            // delete cred store file when all creds gone
            if (_credStore.Count == 0)
            {
                IOUtil.DeleteFile(_credStoreFile);
                return;
            }

            if (!File.Exists(_credStoreFile))
            {
                CreateCredentialStoreFile();
            }

            IOUtil.SaveObject(_credStore, _credStoreFile);
        }

        private string Encrypt(string secret)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = _symmetricKey;
                aes.IV = iv;

                // Create a decrytor to perform the stream transform.
                ICryptoTransform encryptor = aes.CreateEncryptor();

                // Create the streams used for encryption.
                using (MemoryStream msEncrypt = new MemoryStream())
                {
                    using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                    {
                        using (StreamWriter swEncrypt = new StreamWriter(csEncrypt))
                        {
                            swEncrypt.Write(secret);
                        }

                        return Convert.ToBase64String(msEncrypt.ToArray());
                    }
                }
            }
        }

        private string Decrypt(string encryptedText)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = _symmetricKey;
                aes.IV = iv;

                // Create a decrytor to perform the stream transform.
                ICryptoTransform decryptor = aes.CreateDecryptor();

                // Create the streams used for decryption.
                using (MemoryStream msDecrypt = new MemoryStream(Convert.FromBase64String(encryptedText)))
                {
                    using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                    {
                        using (StreamReader srDecrypt = new StreamReader(csDecrypt))
                        {
                            // Read the decrypted bytes from the decrypting stream and place them in a string.
                            return srDecrypt.ReadToEnd();
                        }
                    }
                }
            }
        }

        private void CreateCredentialStoreFile()
        {
            File.WriteAllText(_credStoreFile, "");
            File.SetAttributes(_credStoreFile, File.GetAttributes(_credStoreFile) | FileAttributes.Hidden);

            // Try to lock down the .credentials_store file to the owner/group
            var chmodPath = WhichUtil.Which("chmod", trace: Trace);
            if (!String.IsNullOrEmpty(chmodPath))
            {
                var arguments = $"600 {new FileInfo(_credStoreFile).FullName}";
                using (var invoker = HostContext.CreateService<IProcessInvoker>())
                {
                    var exitCode = invoker.ExecuteAsync(HostContext.GetDirectory(WellKnownDirectory.Root), chmodPath, arguments, null, default(CancellationToken)).GetAwaiter().GetResult();
                    if (exitCode == 0)
                    {
                        Trace.Info(StringUtil.SafeLog("Successfully set permissions for credentials store file {0}", _credStoreFile));
                    }
                    else
                    {
                        Trace.Warning(StringUtil.SafeLog("Unable to successfully set permissions for credentials store file {0}. Received exit code {1} from {2}", _credStoreFile, exitCode, chmodPath));
                    }
                }
            }
            else
            {
                Trace.Warning(StringUtil.SafeLog("Unable to locate chmod to set permissions for credentials store file {0}.", _credStoreFile));
            }
        }
    }

    [DataContract]
    internal class Credential
    {
        public Credential()
        { }

        public Credential(string userName, string password)
        {
            UserName = userName;
            Password = password;
        }

        [DataMember(IsRequired = true)]
        public string UserName { get; set; }

        [DataMember(IsRequired = true)]
        public string Password { get; set; }
    }
}
