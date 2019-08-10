using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Services.Agent.Util;
using Agent.Sdk;
using System.Linq;

namespace Agent.Plugins.PipelineArtifact
{
    internal class FileShareHelper
    {
        private readonly AgentTaskPluginExecutionContext context;
        private readonly CallbackAppTraceSource tracer;

        public FileShareHelper(AgentTaskPluginExecutionContext context, CallbackAppTraceSource tracer)
        {
            this.context = context;
            this.tracer = tracer;
        }

        public void DownloadSingleArtifactAsync(PipelineArtifactDownloadParameters downloadParameters, BuildArtifact buildArtifact, CancellationToken cancellationToken)
        {
            this.DownloadFileShareAsync(downloadParameters.ProjectId, buildArtifact, downloadParameters.TargetDirectory, downloadParameters.MinimatchFilters, cancellationToken);
        }

        public void DownloadMultipleArtifactsAsync(PipelineArtifactDownloadParameters downloadParameters, IEnumerable<BuildArtifact> buildArtifacts, CancellationToken cancellationToken)
        {
            this.DownloadFileShareAsync(downloadParameters.ProjectId, buildArtifacts, downloadParameters.TargetDirectory, downloadParameters.MinimatchFilters, cancellationToken);
        }

        public void DownloadFileShareAsync(Guid projectId, IEnumerable<BuildArtifact> buildArtifacts, string targetDirectory, IEnumerable<string> minimatchFilters, CancellationToken cancellationToken)
        {
            foreach (var buildArtifact in buildArtifacts)
            {
                var dirPath = Path.Combine(targetDirectory, buildArtifact.Name);
                DownloadFileShareAsync(projectId, buildArtifact, dirPath, minimatchFilters, cancellationToken, isSingleArtifactDownload: false);
            }
        }

        private void DownloadFileShareAsync(Guid projectId, BuildArtifact artifact, string destPath, IEnumerable<string> minimatchPatterns, CancellationToken cancellationToken, bool isSingleArtifactDownload = true)
        {
            IEnumerable<Func<string, bool>> minimatcherFuncs = MinimatchHelper.GetMinimatchFuncs(minimatchPatterns, this.tracer);
            DirectoryCopyWithMiniMatch(artifact.Resource.Data + Path.DirectorySeparatorChar + artifact.Name, destPath, this.context, 1, minimatcherFuncs, artifact.Resource.Data + Path.DirectorySeparatorChar + artifact.Name);
        }

        internal static void DirectoryCopyWithMiniMatch(string sourcePath, string destPath, AgentTaskPluginExecutionContext context, int parallelCount = 1, IEnumerable<Func<string, bool>> minimatchFuncs = null, string minimatchRoot = null)
        {
            // If the source path is a file, the system should copy the file to the dest directory directly. 
            if(File.Exists(sourcePath)) {
                context.Output(StringUtil.Loc("CopyFileToDestination", sourcePath, destPath));
                File.Copy(sourcePath, destPath + Path.DirectorySeparatorChar + Path.GetFileName(sourcePath), true);
                return;
            }

            var opts = new ParallelOptions() { MaxDegreeOfParallelism = parallelCount };

            // Get the subdirectories for the specified directory.
            DirectoryInfo dir = new DirectoryInfo(sourcePath);

            // If the destination directory doesn't exist, create it.
            if (!Directory.Exists(destPath))
            {
                Directory.CreateDirectory(destPath);
            }

            DirectoryInfo[] dirs = dir.GetDirectories();
                
            // Get the files in the directory and copy them to the new location.
            FileInfo[] files = dir.GetFiles();
            Parallel.ForEach(files, opts, file => {
                var matchingPath = !string.IsNullOrEmpty(minimatchRoot)? file.FullName.Replace(minimatchRoot + Path.DirectorySeparatorChar, string.Empty): string.Empty;
                if (minimatchFuncs == null || minimatchFuncs.Any(match => match(matchingPath))) 
                {
                    string temppath = Path.Combine(destPath, file.Name);
                    context.Output(StringUtil.Loc("CopyFileToDestination", file, destPath));
                    file.CopyTo(temppath, true);
                }
            });

            // If copying subdirectories, copy them and their contents to new location.
            foreach (DirectoryInfo subdir in dirs)
            {
                string temppath = Path.Combine(destPath, subdir.Name);
                DirectoryCopyWithMiniMatch(subdir.FullName, temppath, context, parallelCount, minimatchFuncs);
            }
        }
    }
}
