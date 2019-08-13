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
            Dictionary<string, string> propertiesDictionary = new Dictionary<string, string>();
            propertiesDictionary.Add(FileShareArtifactUploadEventProperties.ArtifactName, artifactName);
            propertiesDictionary.Add(FileShareArtifactUploadEventProperties.ArtifactType, PipelineArtifactConstants.FileShareArtifact);
            propertiesDictionary.Add(FileShareArtifactUploadEventProperties.ArtifactLocation, fileSharePath);

            var artifact = await buildServer.AssociateArtifactAsync(projectId, buildId, artifactName, ArtifactResourceTypes.FilePath, fileSharePath, propertiesDictionary, token);
            var parallel = context.GetInput(FileShareArtifactUploadEventProperties.Parallel, required: false);

            var parallelCount = 1;
            if(parallel == "true") 
            {
                parallelCount = GetParallelCount(context, context.GetInput(FileShareArtifactUploadEventProperties.ParallelCount, required: false));
            }

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
            public static readonly string ArtifactName = "artifactname";
            public static readonly string ArtifactLocation = "artifactlocation";
            public static readonly string ArtifactType = "artifacttype";
            public static readonly string ParallelCount = "parallelCount";
            public static readonly string Parallel = "parallel";
        }

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