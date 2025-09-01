// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Microsoft.VisualStudio.Services.Agent.Listener.Configuration
{
    public class RSAFileKeyManager : AgentService, IRSAKeyManager
    {
        private string _keyFile;
        private IHostContext _context;

        public RSA CreateKey(bool enableAgentKeyStoreInNamedContainer, bool useCng)
        {
            RSACryptoServiceProvider rsa = null;
            if (!File.Exists(_keyFile))
            {
                Trace.Info("Creating new RSA key using 2048-bit key length");

                rsa = new RSACryptoServiceProvider(2048);

                // Now write the parameters to disk
                IOUtil.SaveObject(new RSAParametersSerializable("", false, rsa.ExportParameters(true)), _keyFile);
                Trace.Info(StringUtil.SafeLog("Successfully saved RSA key parameters to file {0}", _keyFile));

                // Try to lock down the credentials_key file to the owner/group
                var chmodPath = WhichUtil.Which("chmod", trace: Trace);
                if (!String.IsNullOrEmpty(chmodPath))
                {
                    var arguments = $"600 {new FileInfo(_keyFile).FullName}";
                    using (var invoker = _context.CreateService<IProcessInvoker>())
                    {
                        var exitCode = invoker.ExecuteAsync(HostContext.GetDirectory(WellKnownDirectory.Root), chmodPath, arguments, null, default(CancellationToken)).GetAwaiter().GetResult();
                        if (exitCode == 0)
                        {
                            Trace.Info(StringUtil.SafeLog("Successfully set permissions for RSA key parameters file {0}", _keyFile));
                        }
                        else
                        {
                            Trace.Warning(StringUtil.SafeLog("Unable to succesfully set permissions for RSA key parameters file {0}. Received exit code {1} from {2}", _keyFile, exitCode, chmodPath));
                        }
                    }
                }
                else
                {
                    Trace.Warning(StringUtil.SafeLog("Unable to locate chmod to set permissions for RSA key parameters file {0}.", _keyFile));
                }
            }
            else
            {
                Trace.Info(StringUtil.SafeLog("Found existing RSA key parameters file {0}", _keyFile));

                rsa = new RSACryptoServiceProvider();
                rsa.ImportParameters(IOUtil.LoadObject<RSAParametersSerializable>(_keyFile).RSAParameters);
            }

            return rsa;
        }

        public void DeleteKey()
        {
            if (File.Exists(_keyFile))
            {
                Trace.Info(StringUtil.SafeLog("Deleting RSA key parameters file {0}", _keyFile));
                File.Delete(_keyFile);
            }
        }

        public RSA GetKey()
        {
            if (!File.Exists(_keyFile))
            {
                throw new CryptographicException(StringUtil.Loc("RSAKeyFileNotFound", _keyFile));
            }

            Trace.Info(StringUtil.SafeLog("Loading RSA key parameters from file {0}", _keyFile));

            var parameters = IOUtil.LoadObject<RSAParametersSerializable>(_keyFile).RSAParameters;
            var rsa = new RSACryptoServiceProvider();
            rsa.ImportParameters(parameters);
            return rsa;
        }

        void IAgentService.Initialize(IHostContext context)
        {
            base.Initialize(context);

            _context = context;
            _keyFile = context.GetConfigFile(WellKnownConfigFile.RSACredentials);
        }
    }
}
