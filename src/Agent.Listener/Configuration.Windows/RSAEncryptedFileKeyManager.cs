// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.VisualStudio.Services.Agent.Listener.Configuration
{
    [SupportedOSPlatform("windows")]
    public class RSAEncryptedFileKeyManager : AgentService, IRSAKeyManager
    {
        private string _keyFile;
        private IHostContext _context;

        public RSA CreateKey(bool enableAgentKeyStoreInNamedContainer, bool useCng)
        {
            if (enableAgentKeyStoreInNamedContainer)
            {
                return CreateKeyStoreKeyInNamedContainer(useCng);
            }
            else
            {
                return CreateKeyStoreKeyInFile(useCng);
            }
        }

        private RSA CreateKeyStoreKeyInNamedContainer(bool useCng)
        {
            RSA rsa;
            if (!File.Exists(_keyFile))
            {
                if (useCng)
                {
                    Trace.Info("Creating new RSA key using 2048-bit key length");

                    var cspKeyCreationParameters = new CngKeyCreationParameters();
                    cspKeyCreationParameters.KeyCreationOptions = CngKeyCreationOptions.None;
                    cspKeyCreationParameters.Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider;
                    cspKeyCreationParameters.Parameters.Add(new CngProperty("Length", BitConverter.GetBytes(2048), CngPropertyOptions.None));
                    string keyContainerName = "AgentKeyContainer" + Guid.NewGuid().ToString();
#pragma warning disable CA2000 // Dispose objects before losing scope
                    var cngKey = CngKey.Create(CngAlgorithm.Rsa, keyContainerName, cspKeyCreationParameters);
#pragma warning restore CA2000 // Dispose objects before losing scope
                    rsa = new RSACng(cngKey);

                    // Now write the parameters to disk
                    SaveParameters(default(RSAParameters), keyContainerName, useCng);
                    Trace.Info("Successfully saved containerName to file {0} in container {1}", _keyFile, keyContainerName);
                }
                else
                {
                    Trace.Info("Creating new RSA key using 2048-bit key length");

                    CspParameters Params = new CspParameters();
                    Params.KeyContainerName = "AgentKeyContainer" + Guid.NewGuid().ToString();
                    Params.Flags |= CspProviderFlags.UseMachineKeyStore;
                    using (var csp = new RSACryptoServiceProvider(2048, Params))
                    {
                        rsa = RSA.Create(csp.ExportParameters(includePrivateParameters: true));
                    }

                    // Now write the parameters to disk
                    SaveParameters(default(RSAParameters), Params.KeyContainerName, useCng);
                    Trace.Info("Successfully saved containerName to file {0} in container {1}", _keyFile, Params.KeyContainerName);
                }
            }
            else
            {
                Trace.Info("Found existing RSA key parameters file {0}", _keyFile);

                var result = LoadParameters();

                if(string.IsNullOrEmpty(result.containerName))
                {
                    Trace.Info("Container name not present; reading RSA key from file");
                    return CreateKeyStoreKeyInFile(useCng);
                }

                CspParameters Params = new CspParameters();
                Params.KeyContainerName = result.containerName;
                Params.Flags |= CspProviderFlags.UseMachineKeyStore;
                using (var csp = new RSACryptoServiceProvider(Params))
                {
                    rsa = RSA.Create(csp.ExportParameters(includePrivateParameters: true));
                }
            }

            return rsa;

            // References:
            // https://stackoverflow.com/questions/2274596/how-to-store-a-public-key-in-a-machine-level-rsa-key-container
            // https://social.msdn.microsoft.com/Forums/en-US/e3902420-3a82-42cf-a4a3-de230ebcea56/how-to-store-a-public-key-in-a-machinelevel-rsa-key-container?forum=netfxbcl
            // https://security.stackexchange.com/questions/234477/windows-certificates-where-is-private-key-located
        }

        private RSA CreateKeyStoreKeyInFile(bool useCng)
        {
            RSA rsa = null;
            if (!File.Exists(_keyFile))
            {
                Trace.Info("Creating new RSA key using 2048-bit key length");

                rsa = RSA.Create(2048);

                // Now write the parameters to disk
                SaveParameters(rsa.ExportParameters(true), string.Empty, false);
                Trace.Info("Successfully saved RSA key parameters to file {0}", _keyFile);
            }
            else
            {
                Trace.Info("Found existing RSA key parameters file {0}", _keyFile);

                var result = LoadParameters();

                if(!string.IsNullOrEmpty(result.containerName))
                {
                    Trace.Info("Keyfile has ContainerName, so we must read from named container");
                    return CreateKeyStoreKeyInNamedContainer(useCng);
                }

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

        public RSA GetKey()
        {
            return GetKeyFromFile();
        }

        private RSA GetKeyFromNamedContainer()
        {
            if (!File.Exists(_keyFile))
            {
                throw new CryptographicException(StringUtil.Loc("RSAKeyFileNotFound", _keyFile));
            }

            Trace.Info("Loading RSA key parameters from file {0}", _keyFile);

            var result = LoadParameters();

            if (string.IsNullOrEmpty(result.containerName))
            {
                // we should not get here.  GetKeyFromNamedContainer is only called from GetKeyFromFile when result.containerName is not empty
                return GetKeyFromFile();
            }

            if (result.useCng)
            {
                Trace.Info("Using CNG api");
#pragma warning disable CA2000 // Dispose objects before losing scope
                // disposed by by call to rsa.Dispose()
                var cngKey = CngKey.Open(result.containerName, CngProvider.MicrosoftSoftwareKeyStorageProvider, CngKeyOpenOptions.UserKey);
#pragma warning restore CA2000 // Dispose objects before losing scope

                var rsa = new RSACng(cngKey);
                return rsa;
            }
            else
            {
                Trace.Info("Using RSACryptoServiceProvider");
                CspParameters Params = new CspParameters();
                Params.KeyContainerName = result.containerName;
                Params.Flags |= CspProviderFlags.UseMachineKeyStore;
                using (var csp = new RSACryptoServiceProvider(Params))
                {
                    return RSA.Create(csp.ExportParameters(includePrivateParameters: true));
                }
            }
        }

        private RSA GetKeyFromFile()
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

            return RSA.Create(result.rsaParameters);
        }

        private (string containerName, bool useCng, RSAParameters rsaParameters) LoadParameters()
        {
            var encryptedBytes = File.ReadAllBytes(_keyFile);
            var parametersString = Encoding.UTF8.GetString(ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.LocalMachine));
            var deserialized = StringUtil.ConvertFromJson<RSAParametersSerializable>(parametersString);
            return (deserialized.ContainerName, deserialized.UseCng, deserialized.RSAParameters);
        }

        private void SaveParameters(RSAParameters parameters, string containerName, bool useCng)
        {
            var parametersString = StringUtil.ConvertToJson(new RSAParametersSerializable(containerName, useCng, parameters));
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
