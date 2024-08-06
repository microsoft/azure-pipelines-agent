using CommandLine;
using Microsoft.VisualStudio.Services.Agent;

namespace Agent.Listener.CommandLine
{
    [Verb(Constants.Agent.CommandLine.Commands.Configure)]
    public class ConfigureAgent : ConfigureOrRemoveBase
    {
        [Option(Constants.Agent.CommandLine.Flags.AcceptTeeEula)]
        public bool AcceptTeeEula { get; set; }

        [Option(Constants.Agent.CommandLine.Flags.AddDeploymentGroupTags)]
        public bool AddDeploymentGroupTags { get; set; }

        [Option(Constants.Agent.CommandLine.Flags.AddEnvironmentVirtualMachineResourceTags)]
        public bool AddEnvironmentVirtualMachineResourceTags { get; set; }

        [Option(Constants.Agent.CommandLine.Flags.AddMachineGroupTags)]
        public bool AddMachineGroupTags { get; set; }

        [Option(Constants.Agent.CommandLine.Flags.AlwaysExtractTask)]
        public bool AlwaysExtractTask { get; set; }

        [Option(Constants.Agent.CommandLine.Args.Agent)]
        public string Agent { get; set; }

        [Option(Constants.Agent.CommandLine.Args.CollectionName)]
        public string CollectionName { get; set; }

        [Option(Constants.Agent.CommandLine.Flags.DeploymentGroup)]
        public bool DeploymentGroup { get; set; }

        [Option(Constants.Agent.CommandLine.Args.DeploymentGroupName)]
        public string DeploymentGroupName { get; set; }

        [Option(Constants.Agent.CommandLine.Args.DeploymentGroupTags)]
        public string DeploymentGroupTags { get; set; }

        [Option(Constants.Agent.CommandLine.Flags.DeploymentPool)]
        public bool DeploymentPool { get; set; }

        [Option(Constants.Agent.CommandLine.Args.DeploymentPoolName)]
        public string DeploymentPoolName { get; set; }

        [Option(Constants.Agent.CommandLine.Flags.EnableServiceSidTypeUnrestricted)]
        public bool EnableServiceSidTypeUnrestricted { get; set; }

        [Option(Constants.Agent.CommandLine.Flags.Environment)]
        public bool EnvironmentVMResource { get; set; }

        [Option(Constants.Agent.CommandLine.Args.EnvironmentName)]
        public string EnvironmentName { get; set; }

        [Option(Constants.Agent.CommandLine.Args.EnvironmentVMResourceTags)]
        public string EnvironmentVMResourceTags { get; set; }

        [Option(Constants.Agent.CommandLine.Flags.GitUseSChannel)]
        public bool GitUseSChannel { get; set; }

        [Option(Constants.Agent.CommandLine.Flags.DisableLogUploads)]
        public bool DisableLogUploads { get; set; }

        [Option(Constants.Agent.CommandLine.Flags.ReStreamLogsToFiles)]
        public bool ReStreamLogsToFiles { get; set; }

        [Option(Constants.Agent.CommandLine.Flags.MachineGroup)]
        public bool MachineGroup { get; set; }

        [Option(Constants.Agent.CommandLine.Args.MachineGroupName)]
        public string MachineGroupName { get; set; }

        [Option(Constants.Agent.CommandLine.Args.MachineGroupTags)]
        public string MachineGroupTags { get; set; }

        [Option(Constants.Agent.CommandLine.Args.MonitorSocketAddress)]
        public string MonitorSocketAddress { get; set; }

        [Option(Constants.Agent.CommandLine.Args.NotificationPipeName)]
        public string NotificationPipeName { get; set; }

        [Option(Constants.Agent.CommandLine.Args.NotificationSocketAddress)]
        public string NotificationSocketAddress { get; set; }

        [Option(Constants.Agent.CommandLine.Flags.NoRestart)]
        public bool NoRestart { get; set; }

        [Option(Constants.Agent.CommandLine.Flags.OverwriteAutoLogon)]
        public bool OverwriteAutoLogon { get; set; }

        [Option(Constants.Agent.CommandLine.Args.Pool)]
        public string Pool { get; set; }

        [Option(Constants.Agent.CommandLine.Args.ProjectName)]
        public string ProjectName { get; set; }

        [Option(Constants.Agent.CommandLine.Args.ProxyPassword)]
        public string ProxyPassword { get; set; }

        [Option(Constants.Agent.CommandLine.Args.ProxyUserName)]
        public string ProxyUserName { get; set; }

        [Option(Constants.Agent.CommandLine.Args.ProxyUrl)]
        public string ProxyUrl { get; set; }

        [Option(Constants.Agent.CommandLine.Flags.Replace)]
        public bool Replace { get; set; }

        [Option(Constants.Agent.CommandLine.Flags.RunAsAutoLogon)]
        public bool RunAsAutoLogon { get; set; }

        [Option(Constants.Agent.CommandLine.Flags.RunAsService)]
        public bool RunAsService { get; set; }

        [Option(Constants.Agent.CommandLine.Flags.Once)]
        public bool RunOnce { get; set; }

        [Option(Constants.Agent.CommandLine.Flags.PreventServiceStart)]
        public bool PreventServiceStart { get; set; }

        [Option(Constants.Agent.CommandLine.Args.SslCACert)]
        public string SslCACert { get; set; }

        [Option(Constants.Agent.CommandLine.Args.SslClientCert)]
        public string SslClientCert { get; set; }

        [Option(Constants.Agent.CommandLine.Args.SslClientCertArchive)]
        public string SslClientCertArchive { get; set; }

        [Option(Constants.Agent.CommandLine.Args.SslClientCertKey)]
        public string SslClientCertKey { get; set; }

        [Option(Constants.Agent.CommandLine.Args.SslClientCertPassword)]
        public string SslClientCertPassword { get; set; }

        [Option(Constants.Agent.CommandLine.Flags.SslSkipCertValidation)]
        public bool SslSkipCertValidation { get; set; }

        [Option(Constants.Agent.CommandLine.Args.Url)]
        public string Url { get; set; }

        [Option(Constants.Agent.CommandLine.Args.WindowsLogonAccount)]
        public string WindowsLogonAccount { get; set; }

        [Option(Constants.Agent.CommandLine.Args.WindowsLogonPassword)]
        public string WindowsLogonPassword { get; set; }

        [Option(Constants.Agent.CommandLine.Args.Work)]
        public string Work { get; set; }
    }
}
