using System.IO;
using System.Runtime.Serialization;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Microsoft.VisualStudio.Services.Agent
{
    //
    // Settings are persisted in this structure
    //
    [DataContract]
    public sealed class AgentSettings
    {
        [DataMember(EmitDefaultValue = false)]
        public bool AcceptTeeEula { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public int AgentId { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public string AgentName { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public int PoolId { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public string PoolName { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public string ServerUrl { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public string WorkFolder { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public bool RunAsService { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public string ServiceName { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public string ServiceDisplayName { get; set; }
    }

    [ServiceLocator(Default = typeof(ConfigurationStore))]
    public interface IConfigurationStore : IAgentService
    {
        string RootFolder { get; }
        bool IsConfigured();
        bool HasCredentials();
        CredentialData GetCredentials();
        AgentSettings GetSettings();
        void SaveCredential(CredentialData credential);
        void SaveSettings(AgentSettings settings);
    }

    public sealed class ConfigurationStore : AgentService, IConfigurationStore
    {
        private string _binPath;
        private string _configFilePath;
        private string _credFilePath;
        private CredentialData _creds;
        private AgentSettings _settings;

        public override void Initialize(IHostContext hostContext)
        {
            base.Initialize(hostContext);

            var currentAssemblyLocation = System.Reflection.Assembly.GetEntryAssembly().Location;
            Trace.Info("currentAssemblyLocation: {0}", currentAssemblyLocation);

            _binPath = IOUtil.GetBinPath();
            Trace.Info("binPath: {0}", _binPath);

            RootFolder = IOUtil.GetRootPath();
            Trace.Info("RootFolder: {0}", RootFolder);

            _configFilePath = IOUtil.GetConfigFilePath();
            Trace.Info("ConfigFilePath: {0}", _configFilePath);

            _credFilePath = IOUtil.GetCredFilePath();
            Trace.Info("CredFilePath: {0}", _credFilePath);
        }

        public string RootFolder { get; private set; }
                     
        public bool HasCredentials()
        {
            Trace.Info("HasCredentials()");
            bool credsStored = (new FileInfo(_credFilePath)).Exists;
            Trace.Info("stored {0}", credsStored);
            return credsStored;
        }        

        public bool IsConfigured()
        {
            Trace.Info("IsConfigured()");
            bool configured = (new FileInfo(_configFilePath)).Exists;
            Trace.Info("IsConfigured: {0}", configured);
            return configured;
        }

        public CredentialData GetCredentials()
        {
            if (_creds == null)
            {
                _creds = IOUtil.LoadObject<CredentialData>(_credFilePath);
            }

            return _creds;
        }

        public AgentSettings GetSettings()
        {
            if (_settings == null)
            {
                _settings = IOUtil.LoadObject<AgentSettings>(_configFilePath);
            }
            
            return _settings;
        }

        public void SaveCredential(CredentialData credential)
        {   
            Trace.Info("Saving {0} credential @ {1}", credential.Scheme, _credFilePath);
            IOUtil.SaveObject(credential, _credFilePath);
            Trace.Info("Credentials Saved.");
        }

        public void SaveSettings(AgentSettings settings)
        {
            IOUtil.SaveObject(settings, _configFilePath);
            Trace.Info("Settings Saved.");
        }
    }
}