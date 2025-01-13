// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Net;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Microsoft.VisualStudio.Services.Agent
{
    public sealed class NoOpAgentCredentialStore : AgentService, IAgentCredentialStore
    {
        public override void Initialize(IHostContext hostContext)
        {
            base.Initialize(hostContext);
        }

        public void Write(string target, string username, string password)
        {
            Trace.Entering();
            ArgUtil.NotNullOrEmpty(target, nameof(target));
            ArgUtil.NotNullOrEmpty(username, nameof(username));
            ArgUtil.NotNullOrEmpty(password, nameof(password));

            Trace.Info($"Attempt to store credential for '{target}' to cred store.");
        }

        public NetworkCredential Read(string target)
        {
            Trace.Entering();
            ArgUtil.NotNullOrEmpty(target, nameof(target));

            Trace.Info($"Attempt to read credential for '{target}' from cred store.");
            throw new KeyNotFoundException(target);
        }

        public (string UserName, string Password) Read2(string target)
        {
            var ret = Read(target);
            return (ret.UserName, ret.Password);
        }

        public void Delete(string target)
        {
            Trace.Entering();
            ArgUtil.NotNullOrEmpty(target, nameof(target));

            Trace.Info($"Attempt to delete credential for '{target}' from cred store.");
            throw new KeyNotFoundException(target);
        }
    }
}
