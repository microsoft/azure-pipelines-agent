using CommandLine;
using Microsoft.VisualStudio.Services.Agent;

namespace Agent.Listener.CommandLine
{
    public class ConfigureOrRemoveBase : BaseCommand
    {
        [Option(Constants.Agent.CommandLine.Args.Auth)]
        public string Auth { get; set; }

        [Option(Constants.Agent.CommandLine.Flags.LaunchBrowser)]
        public bool LaunchBrowser { get; set; }

        [Option(Constants.Agent.CommandLine.Args.Password)]
        public string Password { get; set; }

        [Option(Constants.Agent.CommandLine.Args.Token)]
        public string Token { get; set; }

        [Option(Constants.Agent.CommandLine.Flags.Unattended)]
        public bool Unattended { get; set; }

        [Option(Constants.Agent.CommandLine.Args.UserName)]
        public string UserName { get; set; }

        [Option(Constants.Agent.CommandLine.Args.SpClientId)]
        public string SpClientId { get; set; }

        [Option(Constants.Agent.CommandLine.Args.SpTenantId)]
        public string SpTenantId { get; set; }

        [Option(Constants.Agent.CommandLine.Args.SpClientSecret)]
        public string SpClientSecret { get; set; }
    }
}
