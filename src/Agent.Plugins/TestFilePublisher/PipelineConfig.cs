using System;
using System.Collections.Generic;

namespace Agent.Plugins.Log.TestFilePublisher.Plugin
{
    public class PipelineConfig
    {
        public Guid ProjectGuid { get; set; }

        public string ProjectName { get; set; }

        public int BuildId { get; set; }

        public string BuildUri { get; set; }

        public IList<string> SearchFolders { get; set; }

        public string Pattern { get; set; }
    }
}
