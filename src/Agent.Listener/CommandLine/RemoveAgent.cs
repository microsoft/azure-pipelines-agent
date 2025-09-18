using CommandLine;
using Microsoft.VisualStudio.Services.Agent;

namespace Agent.Listener.CommandLine
{
    [Verb(Constants.Agent.CommandLine.Commands.Remove)]
    public class RemoveAgent : ConfigureOrRemoveBase
    {
        [Option(Constants.Agent.CommandLine.Args.Url)]
        public string Url { get; set; }
    }
}
