// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.


using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Microsoft.VisualStudio.Services.Agent
{
    public class PatternDescriptor
    {
        public string Regex { get; set; }

        public string Moniker { get; set; }

        public ISet<string> SniffLiterals { get; set; }

        public RegexOptions RegexOptions { get; set; }
    }
}