// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace Agent.Plugins.Log.TestFilePublisher
{
    public class PipelineConfig
    {
        public Guid ProjectGuid { get; set; }

        public string ProjectName { get; set; }

        public int BuildId { get; set; }

        public string BuildUri { get; set; }

        public IList<string> SearchFolders { get; } = new List<string>();

        public IList<string> Patterns { get; } = new List<string>();

        public string StageName { get; set; }

        public string PhaseName { get; set; }

        public string JobName { get; set; }

        public int StageAttempt { get; set; }

        public int PhaseAttempt { get; set; }

        public int JobAttempt { get; set; }
    }
}
