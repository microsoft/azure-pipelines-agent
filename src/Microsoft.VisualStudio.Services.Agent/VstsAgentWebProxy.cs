// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.Linq;
using System.Net;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Agent.Sdk;
using Agent.Sdk.Knob;
using Agent.Sdk.Util;

namespace Microsoft.VisualStudio.Services.Agent
{
    [ServiceLocator(Default = typeof(VstsAgentWebProxy))]
    public interface IVstsAgentWebProxy : IAgentService
    {
        string ProxyAddress { get; }
        string ProxyUsername { get; }
        string ProxyPassword { get; }
        List<string> ProxyBypassList { get; }
        IWebProxy WebProxy { get; }
        void SetupProxy(string proxyAddress, string proxyUsername, string proxyPassword);
        void SaveProxySetting();
        void LoadProxyBypassList();
        void DeleteProxySetting();
    }

    public class VstsAgentWebProxy : AgentService, IVstsAgentWebProxy
    {
        private readonly List<string> _bypassList = new List<string>();
        private AgentWebProxy _agentWebProxy = new AgentWebProxy();

        public string ProxyAddress { get; private set; }
        public string ProxyUsername { get; private set; }
        public string ProxyPassword { get; private set; }
        public List<string> ProxyBypassList => _bypassList;
        public IWebProxy WebProxy => _agentWebProxy;

        public override void Initialize(IHostContext context)
        {
            base.Initialize(context);
            LoadProxySetting();
        }

        // This should only be called from config
        public void SetupProxy(string proxyAddress, string proxyUsername, string proxyPassword)
        {
            ArgUtil.NotNullOrEmpty(proxyAddress, nameof(proxyAddress));
            Trace.Info($"Update proxy setting from '{ProxyAddress ?? string.Empty}' to'{proxyAddress}'");
            ProxyAddress = proxyAddress;
            ProxyUsername = proxyUsername;
            ProxyPassword = proxyPassword;

            if (string.IsNullOrEmpty(ProxyUsername) || string.IsNullOrEmpty(ProxyPassword))
            {
                Trace.Info($"Config proxy use DefaultNetworkCredentials.");
            }
            else
            {
                Trace.Info($"Config authentication proxy as: {ProxyUsername}.");
            }

            // Ensure proxy bypass list is loaded during the agent config
            LoadProxyBypassList();

            _agentWebProxy.Update(ProxyAddress, ProxyUsername, ProxyPassword, ProxyBypassList);
        }

        // This should only be called from config
        public void SaveProxySetting()
        {
            if (!string.IsNullOrEmpty(ProxyAddress))
            {
                string proxyConfigFile = HostContext.GetConfigFile(WellKnownConfigFile.Proxy);
                IOUtil.DeleteFile(proxyConfigFile);
                Trace.Info($"Store proxy configuration to '{proxyConfigFile}' for proxy '{ProxyAddress}'");
                File.WriteAllText(proxyConfigFile, ProxyAddress);
                File.SetAttributes(proxyConfigFile, File.GetAttributes(proxyConfigFile) | FileAttributes.Hidden);

                string proxyCredFile = HostContext.GetConfigFile(WellKnownConfigFile.ProxyCredentials);
                IOUtil.DeleteFile(proxyCredFile);
                if (!string.IsNullOrEmpty(ProxyUsername) && !string.IsNullOrEmpty(ProxyPassword))
                {
                    string lookupKey = Guid.NewGuid().ToString("D").ToUpperInvariant();
                    Trace.Info($"Store proxy credential lookup key '{lookupKey}' to '{proxyCredFile}'");
                    File.WriteAllText(proxyCredFile, lookupKey);
                    File.SetAttributes(proxyCredFile, File.GetAttributes(proxyCredFile) | FileAttributes.Hidden);

                    var credStore = HostContext.GetService<IAgentCredentialStore>();
                    credStore.Write($"VSTS_AGENT_PROXY_{lookupKey}", ProxyUsername, ProxyPassword);
                }
            }
            else
            {
                Trace.Info("No proxy configuration exist.");
            }
        }

        // This should only be called from unconfig
        public void DeleteProxySetting()
        {
            string proxyCredFile = HostContext.GetConfigFile(WellKnownConfigFile.ProxyCredentials);
            if (File.Exists(proxyCredFile))
            {
                Trace.Info("Delete proxy credential from credential store.");
                string lookupKey = File.ReadAllLines(proxyCredFile).FirstOrDefault();
                if (!string.IsNullOrEmpty(lookupKey))
                {
                    var credStore = HostContext.GetService<IAgentCredentialStore>();
                    credStore.Delete($"VSTS_AGENT_PROXY_{lookupKey}");
                }

                Trace.Info($"Delete .proxycredentials file: {proxyCredFile}");
                IOUtil.DeleteFile(proxyCredFile);
            }

            string proxyBypassFile = HostContext.GetConfigFile(WellKnownConfigFile.ProxyBypass);
            if (File.Exists(proxyBypassFile))
            {
                Trace.Info($"Delete .proxybypass file: {proxyBypassFile}");
                IOUtil.DeleteFile(proxyBypassFile);
            }

            string proxyConfigFile = HostContext.GetConfigFile(WellKnownConfigFile.Proxy);
            Trace.Info($"Delete .proxy file: {proxyConfigFile}");
            IOUtil.DeleteFile(proxyConfigFile);
        }

        public void LoadProxyBypassList()
        {
            string proxyBypassFile = HostContext.GetConfigFile(WellKnownConfigFile.ProxyBypass);
            if (File.Exists(proxyBypassFile))
            {
                Trace.Verbose($"Try read proxy bypass list from file: {proxyBypassFile}.");
                foreach (string bypass in File.ReadAllLines(proxyBypassFile).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()))
                {
                    Trace.Info($"Bypass proxy for: {bypass}.");
                    ProxyBypassList.Add(bypass.Trim());
                }
            }

            var proxyBypassEnv = AgentKnobs.NoProxy.GetValue(HostContext).AsString();

            foreach (string bypass in proxyBypassEnv.Split(new[] { ',', ';' }).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()))
            {
                var saveRegexString = ProcessProxyByPassFromEnv(bypass);

                Trace.Info($"Bypass proxy for: {saveRegexString}.");
                ProxyBypassList.Add(saveRegexString);
            }
        }

        private void LoadProxySetting()
        {
            string proxyConfigFile = HostContext.GetConfigFile(WellKnownConfigFile.Proxy);
            if (File.Exists(proxyConfigFile))
            {
                // we expect the first line of the file is the proxy url
                Trace.Verbose($"Try read proxy setting from file: {proxyConfigFile}.");
                ProxyAddress = File.ReadLines(proxyConfigFile).FirstOrDefault() ?? string.Empty;
                ProxyAddress = ProxyAddress.Trim();
                Trace.Verbose($"{ProxyAddress}");
            }

            if (string.IsNullOrEmpty(ProxyAddress))
            {
                ProxyAddress = AgentKnobs.ProxyAddress.GetValue(HostContext).AsString();
                Trace.Verbose($"Proxy address: {ProxyAddress}");
            }

            if (!string.IsNullOrEmpty(ProxyAddress) && !Uri.IsWellFormedUriString(ProxyAddress, UriKind.Absolute))
            {
                Trace.Error($"The proxy url is not a well formed absolute uri string: {ProxyAddress}.");
                ProxyAddress = string.Empty;
            }

            if (!string.IsNullOrEmpty(ProxyAddress))
            {
                Trace.Info($"Config proxy at: {ProxyAddress}.");

                string proxyCredFile = HostContext.GetConfigFile(WellKnownConfigFile.ProxyCredentials);
                if (File.Exists(proxyCredFile))
                {
                    string lookupKey = File.ReadAllLines(proxyCredFile).FirstOrDefault();
                    if (!string.IsNullOrEmpty(lookupKey))
                    {
                        var credStore = HostContext.GetService<IAgentCredentialStore>();
                        var avoidNetCredentialFF = AgentKnobs.AvoidNetCredentialObjectsOnMac.GetValue(HostContext).AsBoolean();
                        var target = $"VSTS_AGENT_PROXY_{lookupKey}";

                        if (avoidNetCredentialFF)
                        {
                            var proxyCred = credStore.Read2(target);
                            ProxyUsername = proxyCred.UserName;
                            ProxyPassword = proxyCred.Password;
                        }
                        else
                        {
                            NetworkCredential proxyCred = credStore.Read(target);
                            ProxyUsername = proxyCred.UserName;
                            ProxyPassword = proxyCred.Password;
                        }

                    }
                }

                if (string.IsNullOrEmpty(ProxyUsername))
                {
                    ProxyUsername = AgentKnobs.ProxyUsername.GetValue(HostContext).AsString();
                }

                if (string.IsNullOrEmpty(ProxyPassword))
                {
                    ProxyPassword = AgentKnobs.ProxyPassword.GetValue(HostContext).AsString();
                }

                if (!string.IsNullOrEmpty(ProxyPassword))
                {
                    HostContext.SecretMasker.AddValue(ProxyPassword, WellKnownSecretAliases.ProxyPassword);
                }

                if (string.IsNullOrEmpty(ProxyUsername) || string.IsNullOrEmpty(ProxyPassword))
                {
                    Trace.Info($"Config proxy use DefaultNetworkCredentials.");
                }
                else
                {
                    Trace.Info($"Config authentication proxy as: {ProxyUsername}.");
                }

                LoadProxyBypassList();

                _agentWebProxy.Update(ProxyAddress, ProxyUsername, ProxyPassword, ProxyBypassList);
            }
            else
            {
                Trace.Info($"No proxy setting found.");
            }
        }

        /// <summary>
        /// Used to escape dots in proxy bypass hosts that was recieved from no_proxy variable
        /// It is requred since we convert host string to the regular expression pattern and
        /// all the dots that are parts of domains will be interpreted as special symbol in regular expression while converting
        /// this leads to false positive matches while check the patterns for bepassing.
        /// We don't escape dots that are parts of .* wildcard.
        /// Also, we don't escape dots that are already prepended by escaping symbols.
        /// </summary>
        private string ProcessProxyByPassFromEnv(string bypass)
        {
            var regExp = new Regex("(?<!\\\\)([.])(?!\\*)");
            var replace = "\\$1";

            return regExp.Replace(bypass, replace);
        }
    }
}
