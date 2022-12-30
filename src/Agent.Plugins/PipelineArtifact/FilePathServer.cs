// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Agent.Sdk;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;

namespace Agent.Plugins.PipelineArtifact
{
    public class FilePathServer
    {
        internal async Task UploadAsync(
            AgentTaskPluginExecutionContext context,
            Guid projectId,
            int buildId,
            string artifactName,
            string targetPath,
            string fileSharePath,
            CancellationToken token)
        {
            string artifactPath = Path.Join(fileSharePath, artifactName);

            Directory.CreateDirectory(artifactPath);

            VssConnection connection = context.VssConnection;
            BuildServer buildServer = new BuildServer(connection);
            var propertiesDictionary = new Dictionary<string, string>
            {
                { FileShareArtifactUploadEventProperties.ArtifactName, artifactName },
                { FileShareArtifactUploadEventProperties.ArtifactType, PipelineArtifactConstants.FileShareArtifact },
                { FileShareArtifactUploadEventProperties.ArtifactLocation, fileSharePath }
            };

            // Associate the pipeline artifact with a build artifact.
            var artifact = await buildServer.AssociateArtifactAsync(projectId,
                                                                    buildId,
                                                                    artifactName,
                                                                    context.Variables.GetValueOrDefault(WellKnownDistributedTaskVariables.JobId)?.Value ?? string.Empty,
                                                                    ArtifactResourceTypes.FilePath,
                                                                    fileSharePath,
                                                                    propertiesDictionary,
                                                                    token);

            var parallel = context.GetInput(FileShareArtifactUploadEventProperties.Parallel, required: false);
            var parallelCount = parallel == "true" ? GetParallelCount(context, context.GetInput(FileShareArtifactUploadEventProperties.ParallelCount, required: false)) : 1;

            if (Directory.Exists(fileSharePath))
            {
                FileShareProvider provider = new FileShareProvider(context, connection, context.CreateArtifactsTracer());
                await provider.PublishArtifactAsync(targetPath, artifactPath, parallelCount, token);
                context.Output(StringUtil.Loc("CopyFileComplete", artifactPath));
            }
        }

        internal static class FileShareArtifactUploadEventProperties
        {
            public const string ArtifactName = "artifactname";
            public const string ArtifactLocation = "artifactlocation";
            public const string ArtifactType = "artifacttype";
            public const string ParallelCount = "parallelCount";
            public const string Parallel = "parallel";
        }

        // Enter the degree of parallelism, or number of threads used, to perform the copy. The value must be at least 1 and not greater than 128.
        // This is the same logic as the build artifact tasks https://github.com/microsoft/azure-pipelines-tasks/blob/master/Tasks/PublishBuildArtifactsV1/publishbuildartifacts.ts
        private int GetParallelCount(AgentTaskPluginExecutionContext context, string parallelCount)
        {
            var result = 8;
            if (int.TryParse(parallelCount, out result))
            {
                if (result < 1)
                {
                    context.Output(StringUtil.Loc("UnexpectedParallelCount"));
                    result = 1;
                }
                else if (result > 128)
                {
                    context.Output(StringUtil.Loc("UnexpectedParallelCount"));
                    result = 128;
                }
            }
            else
            {
                throw new ArgumentException(StringUtil.Loc("ParallelCountNotANumber"));
            }

            return result;
        }
    }
}