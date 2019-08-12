using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agent.Sdk;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Agent.Plugins.PipelineArtifact
{
    internal class FileShareProvider: IArtifactProvider
    {
        private readonly AgentTaskPluginExecutionContext context;
        private readonly CallbackAppTraceSource tracer;

        public FileShareProvider(AgentTaskPluginExecutionContext context, CallbackAppTraceSource tracer)
        {
            this.context = context;
            this.tracer = tracer;
        }

        public Task DownloadSingleArtifactAsync(PipelineArtifactDownloadParameters downloadParameters, BuildArtifact buildArtifact, CancellationToken cancellationToken)
        {
            return this.DownloadFileShareAsync(downloadParameters.ProjectId, buildArtifact, downloadParameters.TargetDirectory, downloadParameters.MinimatchFilters, cancellationToken);
        }

        public Task DownloadMultipleArtifactsAsync(PipelineArtifactDownloadParameters downloadParameters, IEnumerable<BuildArtifact> buildArtifacts, CancellationToken cancellationToken)
        {
            return this.DownloadFileShareAsync(downloadParameters.ProjectId, buildArtifacts, downloadParameters.TargetDirectory, downloadParameters.MinimatchFilters, cancellationToken);
        }

        public Task DownloadFileShareAsync(Guid projectId, IEnumerable<BuildArtifact> buildArtifacts, string targetDirectory, IEnumerable<string> minimatchFilters, CancellationToken cancellationToken)
        {
            foreach (var buildArtifact in buildArtifacts)
            {
                var dirPath = Path.Combine(targetDirectory, buildArtifact.Name);
                return DownloadFileShareAsync(projectId, buildArtifact, dirPath, minimatchFilters, cancellationToken, isSingleArtifactDownload: false);
            }

            return Task.FromResult<object>(null);
        }

        private Task DownloadFileShareAsync(Guid projectId, BuildArtifact artifact, string destPath, IEnumerable<string> minimatchPatterns, CancellationToken cancellationToken, bool isSingleArtifactDownload = true)
        {
            IEnumerable<Func<string, bool>> minimatcherFuncs = MinimatchHelper.GetMinimatchFuncs(minimatchPatterns, this.tracer);
            var downloadRootPath = artifact.Resource.Data + Path.DirectorySeparatorChar + artifact.Name;
            return DirectoryCopyWithMiniMatch(downloadRootPath, destPath, this.context, 1, minimatcherFuncs, downloadRootPath);
        }

        internal static Task DirectoryCopyWithMiniMatch(string sourcePath, string destPath, AgentTaskPluginExecutionContext context, int parallelCount = 1, IEnumerable<Func<string, bool>> minimatchFuncs = null, string minimatchRoot = null)
        {
            // If the source path is a file, the system should copy the file to the dest directory directly. 
            if(File.Exists(sourcePath)) {
                context.Output(StringUtil.Loc("CopyFileToDestination", sourcePath, destPath));
                File.Copy(sourcePath, destPath + Path.DirectorySeparatorChar + Path.GetFileName(sourcePath), true);
                return Task.FromResult<object>(null);
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
                return DirectoryCopyWithMiniMatch(subdir.FullName, temppath, context, parallelCount, minimatchFuncs, minimatchRoot);
            }
            return Task.FromResult<object>(null);
        }
    }
}
