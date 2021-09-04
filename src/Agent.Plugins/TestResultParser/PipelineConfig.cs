// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Agent.Plugins.Log.TestResultParser.Contracts;

namespace Agent.Plugins.Log.TestResultParser.Plugin
{
    public class PipelineConfig : IPipelineConfig
    {
        public Guid Project { get; set; }

        public int BuildId { get; set; }

        public String StageName { get; set; }

        public int StageAttempt { get; set; }

        public String PhaseName { get; set; }

        public int PhaseAttempt { get; set; }

        public String JobName { get; set; }

        public int JobAttempt { get; set; }
    }
}
