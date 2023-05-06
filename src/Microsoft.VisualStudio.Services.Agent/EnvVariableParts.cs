// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.VisualStudio.Services.Agent
{
    public class EnvVariableParts
    {
        public EnvVariableParts(string prefix, string suffix)
        {
            Prefix = prefix;
            Suffix = suffix;
        }

        public string Prefix { get; }
        public string Suffix { get; }
    }
}
