// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Microsoft.VisualStudio.Services.Agent.Tests.L1.Worker
{
    public class FakeConfigurationStore : AgentService, IConfigurationStore
    {
        public string WorkingDirectoryName { get; set; }

        public string RootFolder => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "/TestRuns/" + WorkingDirectoryName;

        public List<SetupInfo> setupInfo => new List<SetupInfo>();

        private AgentSettings _agentSettings;

        public bool IsConfigured()
        {
            return true;
        }

        public bool IsServiceConfigured()
        {
            return true;
        }

        public bool IsAutoLogonConfigured()
        {
            return true;
        }

        public bool HasCredentials()
        {
            return true;
        }

        public CredentialData GetCredentials()
        {
            return null;
        }

        public IEnumerable<SetupInfo> GetSetupInfo()
        {
            return setupInfo;
        }

        public AgentSettings GetSettings()
        {
            if (_agentSettings == null)
            {
                _agentSettings = new AgentSettings
                {
                    AgentName = "TestAgent",
                    WorkFolder = RootFolder + "/w"
                };
            }

            return _agentSettings;
        }

        public void UpdateSettings(AgentSettings agentSettings)
        {
            _agentSettings = agentSettings;
        }

        public void SaveCredential(CredentialData credential)
        {
        }

        public void SaveSettings(AgentSettings settings)
        {
        }

        public void DeleteCredential()
        {
        }

        public void DeleteSettings()
        {
        }

        public void DeleteAutoLogonSettings()
        {
        }

        public void SaveAutoLogonSettings(AutoLogonSettings settings)
        {
        }

        public AutoLogonSettings GetAutoLogonSettings()
        {
            return null;
        }

        public AgentRuntimeOptions GetAgentRuntimeOptions()
        {
            return null;
        }

        public void SaveAgentRuntimeOptions(AgentRuntimeOptions options)
        {
        }

        public void DeleteAgentRuntimeOptions()
        {
        }
    }
}