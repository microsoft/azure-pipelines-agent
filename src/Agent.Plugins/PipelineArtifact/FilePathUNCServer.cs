using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Agent.Sdk;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Agent.Plugins.PipelineArtifact
{
    // A wrapper of DedupManifestArtifactClient, providing basic functionalities such as uploading and downloading pipeline artifacts.
    public class FilePathUNCServer
    {
        // Upload from target path to Azure DevOps BlobStore service through DedupManifestArtifactClient, then associate it with the build
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

            // create the artifact. at this point, mkdirP already succeeded so the path is good.
            // the artifact should get cleaned up during retention even if the copy fails in the
            // middle
            Directory.CreateDirectory(artifactPath);

            // 2) associate the pipeline artifact with an build artifact
            VssConnection connection = context.VssConnection;
            BuildServer buildServer = new BuildServer(connection);
            var propertiesDictionary = new Dictionary<string, string>
            {
                { FileShareArtifactUploadEventProperties.ArtifactName, artifactName },
                { FileShareArtifactUploadEventProperties.ArtifactType, PipelineArtifactConstants.FileShareArtifact },
                { FileShareArtifactUploadEventProperties.ArtifactLocation, fileSharePath }
            };

            var artifact = await buildServer.AssociateArtifactAsync(projectId, buildId, artifactName, ArtifactResourceTypes.FilePath, fileSharePath, propertiesDictionary, token);
            var parallel = context.GetInput(FileShareArtifactUploadEventProperties.Parallel, required: false);
            var parallelCount = parallel == "true" ? GetParallelCount(context, context.GetInput(FileShareArtifactUploadEventProperties.ParallelCount, required: false)) : 1;

            // To copy all the files in one directory to another directory.
            // Get the files in the source folder. (To recursively iterate through
            // all subfolders under the current directory, see
            // "How to: Iterate Through a Directory Tree.")
            // Note: Check for target path was performed previously
            // in this code example.
            if (System.IO.Directory.Exists(fileSharePath))
            {
                await FileShareProvider.DirectoryCopyWithMiniMatch(targetPath, artifactPath, context, parallelCount);
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
        private int GetParallelCount(AgentTaskPluginExecutionContext context, string parallelCount)
        {
            var result = 8;
            if(int.TryParse(parallelCount, out result))
            {
                if(result < 1) {
                    context.Output(StringUtil.Loc("UnexpectedParallelCount"));
                    result = 1;
                }else if(result > 128){
                    context.Output(StringUtil.Loc("UnexpectedParallelCount"));
                    result = 128;
                }
            }else {
                throw new ArgumentException(StringUtil.Loc("ParallelCountNotANumber"));
            }

            return result;
        }
    }
}