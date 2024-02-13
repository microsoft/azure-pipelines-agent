// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Microsoft.VisualStudio.Services.Agent.Worker.Release
{
    public static class AgentUtilities
    {
        // Move this to Agent.Common.Util
        public static string GetPrintableEnvironmentVariables(IEnumerable<Variable> variables)
        {
            StringBuilder builder = new StringBuilder();

            if (variables != null)
            {
                var sortedVariables = variables.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase);
                foreach (var variable in sortedVariables)
                {
                    string varName = VarUtil.ConvertToEnvVariableFormat(variable.Name, variable.PreserveCase);
                    builder.AppendFormat(
                        "{0}\t\t\t\t[{1}] --> [{2}]",
                        Environment.NewLine,
                        varName,
                        variable.Value);
                }
            }

            return builder.ToString();
        }
    }
}