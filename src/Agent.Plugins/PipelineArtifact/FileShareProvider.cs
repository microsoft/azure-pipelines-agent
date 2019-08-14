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
        private const int defaultParallelCount = 1;

        public FileShareProvider(AgentTaskPluginExecutionContext context, CallbackAppTraceSource tracer)
        {
            this.context = context;
            this.tracer = tracer;
        }

        public Task DownloadSingleArtifactAsync(PipelineArtifactDownloadParameters downloadParameters, BuildArtifact buildArtifact, CancellationToken cancellationToken)
        {
            this.CopyFileShare(downloadParameters.ProjectId, buildArtifact, downloadParameters.TargetDirectory, downloadParameters.MinimatchFilters, cancellationToken);
            return Task.CompletedTask;
        }

        public Task DownloadMultipleArtifactsAsync(PipelineArtifactDownloadParameters downloadParameters, IEnumerable<BuildArtifact> buildArtifacts, CancellationToken cancellationToken)
        {
            foreach (var buildArtifact in buildArtifacts)
            {
                var dirPath = Path.Combine(downloadParameters.TargetDirectory, buildArtifact.Name);
                this.CopyFileShare(downloadParameters.ProjectId, buildArtifact, dirPath, downloadParameters.MinimatchFilters, cancellationToken, isSingleArtifactDownload: false);
            }

            return Task.CompletedTask;
        }

        public Task PublishArtifactAsync(string sourcePath, string destPath, int parallelCount)
        {
            this.DirectoryCopyWithMiniMatch(sourcePath, destPath, parallelCount);
            return Task.CompletedTask;
        }

        private void CopyFileShare(Guid projectId, BuildArtifact artifact, string destPath, IEnumerable<string> minimatchPatterns, CancellationToken cancellationToken, bool isSingleArtifactDownload = true)
        {
            var downloadRootPath = artifact.Resource.Data + Path.DirectorySeparatorChar + artifact.Name;
            minimatchPatterns = minimatchPatterns.Select(pattern => Path.Combine(downloadRootPath, pattern));
            IEnumerable<Func<string, bool>> minimatcherFuncs = MinimatchHelper.GetMinimatchFuncs( minimatchPatterns, this.tracer);
            DirectoryCopyWithMiniMatch(downloadRootPath, destPath, defaultParallelCount, minimatcherFuncs);
        }

        private void DirectoryCopyWithMiniMatch(string sourcePath, string destPath, int parallelCount = defaultParallelCount, IEnumerable<Func<string, bool>> minimatchFuncs = null)
        {
            // If the source path is a file, the system should copy the file to the dest directory directly. 
            if(File.Exists(sourcePath)) {
                this.context.Output(StringUtil.Loc("CopyFileToDestination", sourcePath, destPath));
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
                if (minimatchFuncs == null || minimatchFuncs.Any(match => match(file.FullName))) 
                {
                    string temppath = Path.Combine(destPath, file.Name);
                    this.context.Output(StringUtil.Loc("CopyFileToDestination", file, destPath));
                    file.CopyTo(temppath, true);
                }
            });

            // If copying subdirectories, copy them and their contents to new location.
            foreach (DirectoryInfo subdir in dirs)
            {
                string temppath = Path.Combine(destPath, subdir.Name);
                DirectoryCopyWithMiniMatch(subdir.FullName, temppath, parallelCount, minimatchFuncs);
            }
        }
    }
}
