// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using Microsoft.VisualStudio.Services.Agent.Util;
using System.IO;

namespace Microsoft.VisualStudio.Services.Agent
{
    public sealed class MacOSAgentCredentialStore : AgentService, IAgentCredentialStore
    {
        // Keychain requires a password, but this is not intended to add security
        private const string _osxAgentCredStoreKeyChainPassword = "A1DC2A63B3D14817A64619FDDBC92264";

        private string _securityUtil;

        private string _agentCredStoreKeyChain;

        public override void Initialize(IHostContext hostContext)
        {
            base.Initialize(hostContext);

            _securityUtil = WhichUtil.Which("security", true, Trace);

            _agentCredStoreKeyChain = hostContext.GetConfigFile(WellKnownConfigFile.CredentialStore);

            // Create osx key chain if it doesn't exists.
            if (!File.Exists(_agentCredStoreKeyChain))
            {
                List<string> securityOut = new List<string>();
                List<string> securityError = new List<string>();
                object outputLock = new object();
                using (var p = HostContext.CreateService<IProcessInvoker>())
                {
                    p.OutputDataReceived += delegate (object sender, ProcessDataReceivedEventArgs stdout)
                    {
                        if (!string.IsNullOrEmpty(stdout.Data))
                        {
                            lock (outputLock)
                            {
                                securityOut.Add(stdout.Data);
                            }
                        }
                    };

                    p.ErrorDataReceived += delegate (object sender, ProcessDataReceivedEventArgs stderr)
                    {
                        if (!string.IsNullOrEmpty(stderr.Data))
                        {
                            lock (outputLock)
                            {
                                securityError.Add(stderr.Data);
                            }
                        }
                    };

                    // make sure the 'security' has access to the key so we won't get prompt at runtime.
                    int exitCode = p.ExecuteAsync(workingDirectory: HostContext.GetDirectory(WellKnownDirectory.Root),
                                                  fileName: _securityUtil,
                                                  arguments: $"create-keychain -p {_osxAgentCredStoreKeyChainPassword} \"{_agentCredStoreKeyChain}\"",
                                                  environment: null,
                                                  cancellationToken: CancellationToken.None).GetAwaiter().GetResult();
                    if (exitCode == 0)
                    {
                        Trace.Info($"Successfully create-keychain for {_agentCredStoreKeyChain}");
                    }
                    else
                    {
                        if (securityOut.Count > 0)
                        {
                            Trace.Error(string.Join(Environment.NewLine, securityOut));
                        }
                        if (securityError.Count > 0)
                        {
                            Trace.Error(string.Join(Environment.NewLine, securityError));
                        }

                        throw new InvalidOperationException($"'security create-keychain' failed with exit code {exitCode}.");
                    }
                }
            }
            else
            {
                // Try unlock and lock the keychain, make sure it's still in good stage
                UnlockKeyChain();
                LockKeyChain();
            }
        }

        public NetworkCredential Write(string target, string username, string password)
        {
            Trace.Entering();
            ArgUtil.NotNullOrEmpty(target, nameof(target));
            ArgUtil.NotNullOrEmpty(username, nameof(username));
            ArgUtil.NotNullOrEmpty(password, nameof(password));

            try
            {
                UnlockKeyChain();

                // base64encode username + ':' + base64encode password
                // OSX keychain requires you provide -s target and -a username to retrieve password
                // So, we will trade both username and password as 'secret' store into keychain
                string usernameBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(username));
                string passwordBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(password));
                string secretForKeyChain = $"{usernameBase64}:{passwordBase64}";

                List<string> securityOut = new List<string>();
                List<string> securityError = new List<string>();
                object outputLock = new object();
                using (var p = HostContext.CreateService<IProcessInvoker>())
                {
                    p.OutputDataReceived += delegate (object sender, ProcessDataReceivedEventArgs stdout)
                    {
                        if (!string.IsNullOrEmpty(stdout.Data))
                        {
                            lock (outputLock)
                            {
                                securityOut.Add(stdout.Data);
                            }
                        }
                    };

                    p.ErrorDataReceived += delegate (object sender, ProcessDataReceivedEventArgs stderr)
                    {
                        if (!string.IsNullOrEmpty(stderr.Data))
                        {
                            lock (outputLock)
                            {
                                securityError.Add(stderr.Data);
                            }
                        }
                    };

                    // make sure the 'security' has access to the key so we won't get prompt at runtime.
                    int exitCode = p.ExecuteAsync(workingDirectory: HostContext.GetDirectory(WellKnownDirectory.Root),
                                                fileName: _securityUtil,
                                                arguments: $"add-generic-password -s {target} -a VSTSAGENT -w {secretForKeyChain} -T \"{_securityUtil}\" \"{_agentCredStoreKeyChain}\"",
                                                environment: null,
                                                cancellationToken: CancellationToken.None).GetAwaiter().GetResult();
                    if (exitCode == 0)
                    {
                        Trace.Info($"Successfully add-generic-password for {target} (VSTSAGENT)");
                    }
                    else
                    {
                        if (securityOut.Count > 0)
                        {
                            Trace.Error(string.Join(Environment.NewLine, securityOut));
                        }
                        if (securityError.Count > 0)
                        {
                            Trace.Error(string.Join(Environment.NewLine, securityError));
                        }

                        throw new InvalidOperationException($"'security add-generic-password' failed with exit code {exitCode}.");
                    }
                }

                return new NetworkCredential(username, password);
            }
            finally
            {
                LockKeyChain();
            }
        }

        public NetworkCredential Read(string target)
        {
            Trace.Entering();
            ArgUtil.NotNullOrEmpty(target, nameof(target));

            try
            {
                UnlockKeyChain();

                string username;
                string password;

                List<string> securityOut = new List<string>();
                List<string> securityError = new List<string>();
                object outputLock = new object();
                using (var p = HostContext.CreateService<IProcessInvoker>())
                {
                    p.OutputDataReceived += delegate (object sender, ProcessDataReceivedEventArgs stdout)
                    {
                        if (!string.IsNullOrEmpty(stdout.Data))
                        {
                            lock (outputLock)
                            {
                                securityOut.Add(stdout.Data);
                            }
                        }
                    };

                    p.ErrorDataReceived += delegate (object sender, ProcessDataReceivedEventArgs stderr)
                    {
                        if (!string.IsNullOrEmpty(stderr.Data))
                        {
                            lock (outputLock)
                            {
                                securityError.Add(stderr.Data);
                            }
                        }
                    };

                    int exitCode = p.ExecuteAsync(workingDirectory: HostContext.GetDirectory(WellKnownDirectory.Root),
                                                  fileName: _securityUtil,
                                                  arguments: $"find-generic-password -s {target} -a VSTSAGENT -w -g \"{_agentCredStoreKeyChain}\"",
                                                  environment: null,
                                                  cancellationToken: CancellationToken.None).GetAwaiter().GetResult();
                    if (exitCode == 0)
                    {
                        string keyChainSecret = securityOut.First();
                        string[] secrets = keyChainSecret.Split(':');
                        if (secrets.Length == 2 && !string.IsNullOrEmpty(secrets[0]) && !string.IsNullOrEmpty(secrets[1]))
                        {
                            Trace.Info($"Successfully find-generic-password for {target} (VSTSAGENT)");
                            username = Encoding.UTF8.GetString(Convert.FromBase64String(secrets[0]));
                            password = Encoding.UTF8.GetString(Convert.FromBase64String(secrets[1]));
                            return new NetworkCredential(username, password);
                        }
                        else
                        {
                            throw new ArgumentOutOfRangeException(nameof(keyChainSecret));
                        }
                    }
                    else
                    {
                        if (securityOut.Count > 0)
                        {
                            Trace.Error(string.Join(Environment.NewLine, securityOut));
                        }
                        if (securityError.Count > 0)
                        {
                            Trace.Error(string.Join(Environment.NewLine, securityError));
                        }

                        throw new InvalidOperationException($"'security find-generic-password' failed with exit code {exitCode}.");
                    }
                }
            }
            finally
            {
                LockKeyChain();
            }
        }

        public void Delete(string target)
        {
            Trace.Entering();
            ArgUtil.NotNullOrEmpty(target, nameof(target));

            try
            {
                UnlockKeyChain();

                List<string> securityOut = new List<string>();
                List<string> securityError = new List<string>();
                object outputLock = new object();

                using (var p = HostContext.CreateService<IProcessInvoker>())
                {
                    p.OutputDataReceived += delegate (object sender, ProcessDataReceivedEventArgs stdout)
                    {
                        if (!string.IsNullOrEmpty(stdout.Data))
                        {
                            lock (outputLock)
                            {
                                securityOut.Add(stdout.Data);
                            }
                        }
                    };

                    p.ErrorDataReceived += delegate (object sender, ProcessDataReceivedEventArgs stderr)
                    {
                        if (!string.IsNullOrEmpty(stderr.Data))
                        {
                            lock (outputLock)
                            {
                                securityError.Add(stderr.Data);
                            }
                        }
                    };

                    int exitCode = p.ExecuteAsync(workingDirectory: HostContext.GetDirectory(WellKnownDirectory.Root),
                                                  fileName: _securityUtil,
                                                  arguments: $"delete-generic-password -s {target} -a VSTSAGENT \"{_agentCredStoreKeyChain}\"",
                                                  environment: null,
                                                  cancellationToken: CancellationToken.None).GetAwaiter().GetResult();
                    if (exitCode == 0)
                    {
                        Trace.Info($"Successfully delete-generic-password for {target} (VSTSAGENT)");
                    }
                    else
                    {
                        if (securityOut.Count > 0)
                        {
                            Trace.Error(string.Join(Environment.NewLine, securityOut));
                        }
                        if (securityError.Count > 0)
                        {
                            Trace.Error(string.Join(Environment.NewLine, securityError));
                        }

                        throw new InvalidOperationException($"'security delete-generic-password' failed with exit code {exitCode}.");
                    }
                }
            }
            finally
            {
                LockKeyChain();
            }
        }

        private void UnlockKeyChain()
        {
            Trace.Entering();
            ArgUtil.NotNullOrEmpty(_securityUtil, nameof(_securityUtil));
            ArgUtil.NotNullOrEmpty(_agentCredStoreKeyChain, nameof(_agentCredStoreKeyChain));

            List<string> securityOut = new List<string>();
            List<string> securityError = new List<string>();
            object outputLock = new object();
            using (var p = HostContext.CreateService<IProcessInvoker>())
            {
                p.OutputDataReceived += delegate (object sender, ProcessDataReceivedEventArgs stdout)
                {
                    if (!string.IsNullOrEmpty(stdout.Data))
                    {
                        lock (outputLock)
                        {
                            securityOut.Add(stdout.Data);
                        }
                    }
                };

                p.ErrorDataReceived += delegate (object sender, ProcessDataReceivedEventArgs stderr)
                {
                    if (!string.IsNullOrEmpty(stderr.Data))
                    {
                        lock (outputLock)
                        {
                            securityError.Add(stderr.Data);
                        }
                    }
                };

                // make sure the 'security' has access to the key so we won't get prompt at runtime.
                int exitCode = p.ExecuteAsync(workingDirectory: HostContext.GetDirectory(WellKnownDirectory.Root),
                                              fileName: _securityUtil,
                                              arguments: $"unlock-keychain -p {_osxAgentCredStoreKeyChainPassword} \"{_agentCredStoreKeyChain}\"",
                                              environment: null,
                                              cancellationToken: CancellationToken.None).GetAwaiter().GetResult();
                if (exitCode == 0)
                {
                    Trace.Info($"Successfully unlock-keychain for {_agentCredStoreKeyChain}");
                }
                else
                {
                    if (securityOut.Count > 0)
                    {
                        Trace.Error(string.Join(Environment.NewLine, securityOut));
                    }
                    if (securityError.Count > 0)
                    {
                        Trace.Error(string.Join(Environment.NewLine, securityError));
                    }

                    throw new InvalidOperationException($"'security unlock-keychain' failed with exit code {exitCode}.");
                }
            }
        }

        private void LockKeyChain()
        {
            Trace.Entering();
            ArgUtil.NotNullOrEmpty(_securityUtil, nameof(_securityUtil));
            ArgUtil.NotNullOrEmpty(_agentCredStoreKeyChain, nameof(_agentCredStoreKeyChain));

            List<string> securityOut = new List<string>();
            List<string> securityError = new List<string>();
            object outputLock = new object();
            using (var p = HostContext.CreateService<IProcessInvoker>())
            {
                p.OutputDataReceived += delegate (object sender, ProcessDataReceivedEventArgs stdout)
                {
                    if (!string.IsNullOrEmpty(stdout.Data))
                    {
                        lock (outputLock)
                        {
                            securityOut.Add(stdout.Data);
                        }
                    }
                };

                p.ErrorDataReceived += delegate (object sender, ProcessDataReceivedEventArgs stderr)
                {
                    if (!string.IsNullOrEmpty(stderr.Data))
                    {
                        lock (outputLock)
                        {
                            securityError.Add(stderr.Data);
                        }
                    }
                };

                // make sure the 'security' has access to the key so we won't get prompt at runtime.
                int exitCode = p.ExecuteAsync(workingDirectory: HostContext.GetDirectory(WellKnownDirectory.Root),
                                              fileName: _securityUtil,
                                              arguments: $"lock-keychain \"{_agentCredStoreKeyChain}\"",
                                              environment: null,
                                              cancellationToken: CancellationToken.None).GetAwaiter().GetResult();
                if (exitCode == 0)
                {
                    Trace.Info($"Successfully lock-keychain for {_agentCredStoreKeyChain}");
                }
                else
                {
                    if (securityOut.Count > 0)
                    {
                        Trace.Error(string.Join(Environment.NewLine, securityOut));
                    }
                    if (securityError.Count > 0)
                    {
                        Trace.Error(string.Join(Environment.NewLine, securityError));
                    }

                    throw new InvalidOperationException($"'security lock-keychain' failed with exit code {exitCode}.");
                }
            }
        }
    }
}
