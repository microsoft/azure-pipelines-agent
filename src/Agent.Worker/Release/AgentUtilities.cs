using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Microsoft.TeamFoundation.Core.WebApi;

namespace Microsoft.VisualStudio.Services.Agent.Worker.Release
{
    public static class AgentUtilities
    {
        // Move this to Agent.Common.Util
        public static string GetPrintableEnvironmentVariables(IExecutionContext executionContext)
        {
            StringBuilder builder = new StringBuilder();
            
            if (executionContext.Variables != null && executionContext.Variables.Public != null)
            {
                IList<string> variablesToMask = GetVariablesToMaskForPublicProjects();
                var sortedVariables = executionContext.Variables.Public.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase);
                foreach (var pair in sortedVariables)
                {
                    if (executionContext.Variables.System_TeamProjectVisibility != ProjectVisibility.Public
                        || !variablesToMask.Any(x => string.Equals(x, pair.Key, StringComparison.OrdinalIgnoreCase)))
                    {
                        string varName = pair.Key.ToUpperInvariant().Replace(".", "_").Replace(" ", "_");
                        builder.AppendFormat(
                            "{0}\t\t\t\t[{1}] --> [{2}]",
                            Environment.NewLine,
                            varName,
                            pair.Value);
                    }
                }
            }

            return builder.ToString();
        }

        private static IList<string> GetVariablesToMaskForPublicProjects()
        {
            return new List<string>{ Constants.Variables.Release.ReleaseDeploymentRequestedForEmail, Constants.Variables.Release.ReleaseRequestedForEmail };
        }
    }
}