using CommandLine;
using Microsoft.VisualStudio.Services.Agent;

namespace Agent.Listener.CommandLine
{
    [Verb(Constants.Agent.CommandLine.Commands.ReAuth)]
    public class ReAuthAgent : ConfigureOrRemoveBase
    {
    }
}
