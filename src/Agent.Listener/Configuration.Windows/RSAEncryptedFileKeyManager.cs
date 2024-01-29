// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.VisualStudio.Services.Agent.Listener.Configuration
{
    public class RSAEncryptedFileKeyManager : AgentService, IRSAKeyManager
    {
        private string _keyFile;
        private IHostContext _context;

        public RSACryptoServiceProvider CreateKey(bool enableAgentKeyStoreInNamedContainer)
        {
            if (enableAgentKeyStoreInNamedContainer)
            {
                return CreateKeyStoreKeyInNamedContainer();
            }
            else
            {
                return CreateKeyStoreKeyInFile();
            }
        }

        private RSACryptoServiceProvider CreateKeyStoreKeyInNamedContainer()
        {
            RSACryptoServiceProvider rsa;
            if (!File.Exists(_keyFile))
            {
                Trace.Info("Creating new RSA key using 2048-bit key length");

                CspParameters Params = new CspParameters();
                Params.KeyContainerName = "AgentKeyContainer" + Guid.NewGuid().ToString();
                Params.Flags |= CspProviderFlags.UseNonExportableKey | CspProviderFlags.UseMachineKeyStore;
                rsa = new RSACryptoServiceProvider(2048, Params);

                // Now write the parameters to disk
                SaveParameters(default(RSAParameters), Params.KeyContainerName);                
                Trace.Info("Successfully saved containerName to file {0} in container {1}", _keyFile, Params.KeyContainerName);
            }
            else
            {
                Trace.Info("Found existing RSA key parameters file {0}", _keyFile);

                var result = LoadParameters();

                if(string.IsNullOrEmpty(result.containerName))
                {
                    Trace.Info("Container name not present; reading RSA key from file");
                    return CreateKeyStoreKeyInFile();
                }

                CspParameters Params = new CspParameters();
                Params.KeyContainerName = result.containerName;
                Params.Flags |= CspProviderFlags.UseNonExportableKey | CspProviderFlags.UseMachineKeyStore;
                rsa = new RSACryptoServiceProvider(Params);
            }

            return rsa;

            // References:
            // https://stackoverflow.com/questions/2274596/how-to-store-a-public-key-in-a-machine-level-rsa-key-container
            // https://social.msdn.microsoft.com/Forums/en-US/e3902420-3a82-42cf-a4a3-de230ebcea56/how-to-store-a-public-key-in-a-machinelevel-rsa-key-container?forum=netfxbcl
            // https://security.stackexchange.com/questions/234477/windows-certificates-where-is-private-key-located
        }

        private RSACryptoServiceProvider CreateKeyStoreKeyInFile()
        {
            RSACryptoServiceProvider rsa = null;
            if (!File.Exists(_keyFile))
            {
                Trace.Info("Creating new RSA key using 2048-bit key length");

                rsa = new RSACryptoServiceProvider(2048);

                // Now write the parameters to disk
                SaveParameters(rsa.ExportParameters(true), string.Empty);
                Trace.Info("Successfully saved RSA key parameters to file {0}", _keyFile);
            }
            else
            {
                Trace.Info("Found existing RSA key parameters file {0}", _keyFile);

                var result = LoadParameters();

                if(!string.IsNullOrEmpty(result.containerName))
                {
                    Trace.Info("Keyfile has ContainerName, so we must read from named container");
                    return CreateKeyStoreKeyInNamedContainer();
                }

                rsa = new RSACryptoServiceProvider();
                rsa.ImportParameters(result.rsaParameters);
            }

            return rsa;
        }

        public void DeleteKey()
        {
            if (File.Exists(_keyFile))
            {
                Trace.Info("Deleting RSA key parameters file {0}", _keyFile);
                File.Delete(_keyFile);
            }
        }

        public RSACryptoServiceProvider GetKey(bool enableAgentKeyStoreInNamedContainer)
        {
            if (enableAgentKeyStoreInNamedContainer)
            {
                return GetKeyFromNamedContainer();
            }
            else
            {
                return GetKeyFromFile();
            }
        }

        private RSACryptoServiceProvider GetKeyFromNamedContainer()
        {
            if (!File.Exists(_keyFile))
            {
                throw new CryptographicException(StringUtil.Loc("RSAKeyFileNotFound", _keyFile));
            }

            Trace.Info("Loading RSA key parameters from file {0}", _keyFile);

            var result = LoadParameters();

            if (string.IsNullOrEmpty(result.containerName))
            {
                return GetKeyFromFile();
            }

            CspParameters Params = new CspParameters();
            Params.KeyContainerName = result.containerName;
            Params.Flags |= CspProviderFlags.UseNonExportableKey | CspProviderFlags.UseMachineKeyStore;
            var rsa = new RSACryptoServiceProvider(Params);
            return rsa;
        }

        private RSACryptoServiceProvider GetKeyFromFile()
        {
            if (!File.Exists(_keyFile))
            {
                throw new CryptographicException(StringUtil.Loc("RSAKeyFileNotFound", _keyFile));
            }

            Trace.Info("Loading RSA key parameters from file {0}", _keyFile);

            var result = LoadParameters();

            if(!string.IsNullOrEmpty(result.containerName))
            {
                Trace.Info("Keyfile has ContainerName, reading from NamedContainer");
                return GetKeyFromNamedContainer();
            }

            var rsa = new RSACryptoServiceProvider();
            rsa.ImportParameters(result.rsaParameters);
            return rsa;
        }

        private (string containerName, RSAParameters rsaParameters) LoadParameters()
        {
            var encryptedBytes = File.ReadAllBytes(_keyFile);
            var parametersString = Encoding.UTF8.GetString(ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.LocalMachine));
            var deserialized = StringUtil.ConvertFromJson<RSAParametersSerializable>(parametersString);
            return (deserialized.ContainerName, deserialized.RSAParameters);
        }

        private void SaveParameters(RSAParameters parameters, string containerName)
        {
            var parametersString = StringUtil.ConvertToJson(new RSAParametersSerializable(containerName, parameters));
            var encryptedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(parametersString), null, DataProtectionScope.LocalMachine);
            File.WriteAllBytes(_keyFile, encryptedBytes);
            File.SetAttributes(_keyFile, File.GetAttributes(_keyFile) | FileAttributes.Hidden);
        }

        void IAgentService.Initialize(IHostContext context)
        {
            base.Initialize(context);

            _context = context;
            _keyFile = context.GetConfigFile(WellKnownConfigFile.RSACredentials);
        }
    }
}
