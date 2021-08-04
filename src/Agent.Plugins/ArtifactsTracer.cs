// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.Agent.Util;
using Agent.Sdk;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;

namespace Agent.Plugins
{
    public static class ArtifactsTracer
    {
        public static IAppTraceSource CreateArtifactsTracer(this AgentTaskPluginExecutionContext context)
        {
            ArgUtil.NotNull(context, nameof(context));
            bool verbose = context.IsSystemDebugTrue();
            return new CallbackAppTraceSource(
                (str, level) => 
                {
                    if (level == System.Diagnostics.SourceLevels.Warning)
                    {
                        context.Warning(str);
                    }
                    else
                    {
                        context.Output(str);
                    }
                },
                verbose
                    ? System.Diagnostics.SourceLevels.Verbose
                    : System.Diagnostics.SourceLevels.Information);
        }
    }
}