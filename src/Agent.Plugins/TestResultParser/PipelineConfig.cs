using System;
using Agent.Plugins.Log.TestResultParser.Contracts;

namespace Agent.Plugins.Log.TestResultParser.Plugin
{
    public class PipelineConfig : IPipelineConfig
    {
        public Guid Project { get; set; }

        public int BuildId { get; set; }

        // add pipeline info (name and attempt for stage, phase and job respectively) in here like:
        // public int StageName { get; set; }
    }
}
