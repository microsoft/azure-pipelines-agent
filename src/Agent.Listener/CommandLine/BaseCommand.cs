using CommandLine;
using Microsoft.VisualStudio.Services.Agent;

namespace Agent.Listener.CommandLine
{
    public class BaseCommand
    {
        [Option(Constants.Agent.CommandLine.Flags.Help)]
        public bool Help { get; set; }

        [Option(Constants.Agent.CommandLine.Flags.Version)]
        public bool Version { get; set; }
    }
}
