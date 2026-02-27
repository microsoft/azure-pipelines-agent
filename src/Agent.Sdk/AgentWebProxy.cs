// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Agent.Sdk
{
    public class AgentWebProxySettings
    {
        public static string AgentProxyUrlKey = "Agent.ProxyUrl".ToLower();
        public static string AgentProxyUsernameKey = "Agent.ProxyUsername".ToLower();
        public static string AgentProxyPasswordKey = "Agent.ProxyPassword".ToLower();
        public static string AgentProxyBypassListKey = "Agent.ProxyBypassList".ToLower();
        public static string AgentUseBasicAuthForProxyKey = "Agent.UseBasicAuthForProxy".ToLower();
        public string ProxyAddress { get; set; }
        public string ProxyUsername { get; set; }
        public string ProxyPassword { get; set; }
        public List<string> ProxyBypassList { get; set; }
        public bool UseBasicAuthForProxy { get; set; }
        public IWebProxy WebProxy { get; set; }
    }

    public class AgentWebProxy : IWebProxy
    {
        private string _proxyAddress;
        private readonly List<Regex> _regExBypassList = new List<Regex>();
        private bool _useBasicAuthForProxy = false; // Flag to control Basic auth usage

        public ICredentials Credentials { get; set; }

        public AgentWebProxy()
        {
        }

        public AgentWebProxy(string proxyAddress, string proxyUsername, string proxyPassword, List<string> proxyBypassList)
        {
            Update(proxyAddress, proxyUsername, proxyPassword, proxyBypassList, false);
        }

        public AgentWebProxy(string proxyAddress, string proxyUsername, string proxyPassword, List<string> proxyBypassList, bool useBasicAuthForProxy = false)
        {
            Update(proxyAddress, proxyUsername, proxyPassword, proxyBypassList, useBasicAuthForProxy);
        }

        public void Update(string proxyAddress, string proxyUsername, string proxyPassword, List<string> proxyBypassList, bool useBasicAuthForProxy = false)
        {
            _useBasicAuthForProxy = useBasicAuthForProxy;
            _proxyAddress = proxyAddress?.Trim();

            if (string.IsNullOrEmpty(proxyUsername) || string.IsNullOrEmpty(proxyPassword))
            {
                Credentials = CredentialCache.DefaultNetworkCredentials;
            }
            else
            {
                if (_useBasicAuthForProxy)
                {
                    // Use CredentialCache to force Basic authentication and avoid NTLM negotiation issues
                    // This fixes the 407 Proxy Authentication Required errors that occur when .NET
                    // attempts NTLM authentication but fails to fall back to Basic authentication properly
                    var credentialCache = new CredentialCache();
                    var proxyUri = new Uri(_proxyAddress);
                    credentialCache.Add(proxyUri, "Basic", new NetworkCredential(proxyUsername, proxyPassword));
                    Credentials = credentialCache;
                }
                else
                {
                    // Default behavior: Use NetworkCredential (default logic for .NET)
                    Credentials = new NetworkCredential(proxyUsername, proxyPassword);
                }
            }

            if (proxyBypassList != null)
            {
                foreach (string bypass in proxyBypassList)
                {
                    if (string.IsNullOrWhiteSpace(bypass))
                    {
                        continue;
                    }
                    else
                    {
                        try
                        {
                            Regex bypassRegex = new Regex(bypass.Trim(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ECMAScript);
                            _regExBypassList.Add(bypassRegex);
                        }
                        catch (Exception)
                        {
                            // eat all exceptions
                        }
                    }
                }
            }
        }

        public Uri GetProxy(Uri destination)
        {
            if (IsBypassed(destination))
            {
                return destination;
            }
            else
            {
                return new Uri(_proxyAddress);
            }
        }

        public bool IsBypassed(Uri uri)
        {
            ArgUtil.NotNull(uri, nameof(uri));
            return string.IsNullOrEmpty(_proxyAddress) || uri.IsLoopback || IsMatchInBypassList(uri);
        }

        private bool IsMatchInBypassList(Uri input)
        {
            string matchUriString = input.IsDefaultPort ?
                input.Scheme + "://" + input.Host :
                input.Scheme + "://" + input.Host + ":" + input.Port.ToString();

            foreach (Regex r in _regExBypassList)
            {
                if (r.IsMatch(matchUriString))
                {
                    return true;
                }
            }

            return false;
        }
    }
}