using Microsoft.VisualStudio.Services.Agent.Listener.Diagnostics;
using System;
using System.Collections.Generic;
using System.Text;

namespace Agent.Listener.Diagnostics
{
    class DiagnosticSuite
    {
        public string SuiteName { get; set; }
        public List<IDiagnosticInfo> DiagnosticInfo { get; set; }
        public List<IDiagnosticTest> DiagnosticTests { get; set; }
    }
}
