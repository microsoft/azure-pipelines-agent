using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Agent.Sdk;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;

namespace Agent.Plugins.PipelineArtifact
{
    public abstract class PipelineArtifactTaskPluginBaseV1 : IAgentTaskPlugin
    {
        public abstract Guid Id { get; }
        protected virtual string TargetPath => "targetPath";
        protected virtual string PipelineId => "pipelineId";
        public string Stage => "main";

        public Task RunAsync(AgentTaskPluginExecutionContext context, CancellationToken token)
        {
            return this.ProcessCommandInternalAsync(context, token);
        }

        protected abstract Task ProcessCommandInternalAsync(
            AgentTaskPluginExecutionContext context, 
            CancellationToken token);  

        // Properties set by tasks
        protected static class ArtifactEventProperties
        {
            public static readonly string BuildType = "buildType";
            public static readonly string Project = "project";
            public static readonly string BuildPipelineDefinition = "definition";
            public static readonly string BuildTriggering = "specificBuildWithTriggering";
            public static readonly string BuildVersionToDownload = "buildVersionToDownload";
            public static readonly string BranchName = "branchName";
            public static readonly string Tags = "tags";
            public static readonly string ArtifactName = "artifactName";
            public static readonly string ItemPattern = "itemPattern";
            public static readonly string ArtifactType = "artifactType";
            public static readonly string FileSharePath = "fileSharePath";
            public static readonly string Parallel = "parallel";
            public static readonly string ParallelCount = "parallelCount";
        }
    }

    // Caller: PublishPipelineArtifact task
    // Can be invoked from a build run or a release run should a build be set as the artifact. 
    public class PublishPipelineArtifactTaskV1 : PipelineArtifactTaskPluginBaseV1
    {
        public override Guid Id => PipelineArtifactPluginConstants.PublishPipelineArtifactTaskId;
        protected override string TargetPath => "path";

        private static readonly Regex jobIdentifierRgx = new Regex("[^a-zA-Z0-9 - .]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly string pipelineArtifactType = "pipelineartifact";
        private static readonly string fileShareType = "filepath";

        protected override async Task ProcessCommandInternalAsync(
            AgentTaskPluginExecutionContext context, 
            CancellationToken token)
        {
            string artifactName = context.GetInput(ArtifactEventProperties.ArtifactName, required: false);
            string targetPath = context.GetInput(TargetPath, required: true);
            string artifactType = context.GetInput(ArtifactEventProperties.ArtifactType, required: true);
            artifactType = artifactType.ToLower();

            string defaultWorkingDirectory = context.Variables.GetValueOrDefault("system.defaultworkingdirectory").Value;

            targetPath = Path.IsPathFullyQualified(targetPath) ? targetPath : Path.GetFullPath(Path.Combine(defaultWorkingDirectory, targetPath));
   
            // Project ID
            Guid projectId = new Guid(context.Variables.GetValueOrDefault(BuildVariables.TeamProjectId)?.Value ?? Guid.Empty.ToString());
            ArgUtil.NotEmpty(projectId, nameof(projectId));

            // Build ID
            string buildIdStr = context.Variables.GetValueOrDefault(BuildVariables.BuildId)?.Value ?? string.Empty;
            if (!int.TryParse(buildIdStr, out int buildId))
            {
                // This should not happen since the build id comes from build environment. But a user may override that so we must be careful.
                throw new ArgumentException(StringUtil.Loc("BuildIdIsNotValid", buildIdStr));
            }
            
            if(artifactType == pipelineArtifactType) {
                string hostType = context.Variables.GetValueOrDefault(WellKnownDistributedTaskVariables.HostType)?.Value; 
                if (!string.Equals(hostType, "Build", StringComparison.OrdinalIgnoreCase)) {
                    throw new InvalidOperationException(
                        StringUtil.Loc("CannotUploadFromCurrentEnvironment", hostType ?? string.Empty)); 
                }

                if (String.IsNullOrWhiteSpace(artifactName))
                {
                    string jobIdentifier = context.Variables.GetValueOrDefault(WellKnownDistributedTaskVariables.JobIdentifier).Value;
                    var normalizedJobIdentifier = NormalizeJobIdentifier(jobIdentifier);
                    artifactName = normalizedJobIdentifier;
                }

                if(!PipelineArtifactPathHelper.IsValidArtifactName(artifactName)) {
                    throw new ArgumentException(StringUtil.Loc("ArtifactNameIsNotValid", artifactName));
                }

                string fullPath = Path.GetFullPath(targetPath);
                bool isFile = File.Exists(fullPath);
                bool isDir = Directory.Exists(fullPath);
                if (!isFile && !isDir)
                {
                    // if local path is neither file nor folder
                    throw new FileNotFoundException(StringUtil.Loc("PathDoesNotExist", targetPath));
                }

                // Upload to VSTS BlobStore, and associate the artifact with the build.
                context.Output(StringUtil.Loc("UploadingPipelineArtifact", fullPath, buildId));
                PipelineArtifactServer server = new PipelineArtifactServer();
                await server.UploadAsync(context, projectId, buildId, artifactName, fullPath, token);
                context.Output(StringUtil.Loc("UploadArtifactFinished"));

            }else if (artifactType == fileShareType){
                string fileSharePath = context.GetInput(ArtifactEventProperties.FileSharePath, required: true);
                string artifactPath = Path.Join(fileSharePath, artifactName);

                if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows)){
                    // create the artifact. at this point, mkdirP already succeeded so the path is good.
                    // the artifact should get cleaned up during retention even if the copy fails in the
                    // middle
                    Directory.CreateDirectory(artifactPath);

                    // 2) associate the pipeline artifact with an build artifact
                    VssConnection connection = context.VssConnection;
                    BuildServer buildHelper = new BuildServer(connection);
                    Dictionary<string, string> propertiesDictionary = new Dictionary<string, string>();
                    propertiesDictionary.Add(FileShareArtifactUploadEventProperties.ArtifactName, artifactName);
                    propertiesDictionary.Add(FileShareArtifactUploadEventProperties.ArtifactType, fileShareType);
                    propertiesDictionary.Add(FileShareArtifactUploadEventProperties.ArtifactLocation, fileSharePath);

                    var artifact = await buildHelper.AssociateArtifact(projectId, buildId, artifactName, ArtifactResourceTypes.FilePath, fileSharePath, propertiesDictionary, token);
                    var parallel = context.GetInput(ArtifactEventProperties.Parallel, required: false);

                    var parallelCount = 1;
                    if(parallel == "true") 
                    {
                        parallelCount = GetParallelCount(context, context.GetInput(ArtifactEventProperties.ParallelCount, required: false));
                    }

                    // To copy all the files in one directory to another directory.
                    // Get the files in the source folder. (To recursively iterate through
                    // all subfolders under the current directory, see
                    // "How to: Iterate Through a Directory Tree.")
                    // Note: Check for target path was performed previously
                    // in this code example.
                    if (System.IO.Directory.Exists(fileSharePath))
                    {
                        DirectoryCopy(targetPath, artifactPath, true, parallelCount, context);
                    }
                   
                }else {
                    // file share artifacts are not currently supported on OSX/Linux.
                    throw new InvalidOperationException(StringUtil.Loc("FileShareOperatingSystemNotSupported"));
                }
            }
        }

        private void DirectoryCopy(string sourceName, string destName, bool copySubDirs, int parallelCount, AgentTaskPluginExecutionContext context)
        {
            // If the source path is a file, the system should copy the file to the dest directory directly. 
            if(File.Exists(sourceName)) {
                context.Output(StringUtil.Loc("CopyFileToDestination", sourceName, destName));
                File.Copy(sourceName, destName + Path.DirectorySeparatorChar + Path.GetFileName(sourceName), true);
                return;
            }

            // Get the subdirectories for the specified directory.
            DirectoryInfo dir = new DirectoryInfo(sourceName);
            var opts = new ParallelOptions() { MaxDegreeOfParallelism = parallelCount };
            // If the destination directory doesn't exist, create it.
            if (!Directory.Exists(destName))
            {
                Directory.CreateDirectory(destName);
            }

            DirectoryInfo[] dirs = dir.GetDirectories();
                
            // Get the files in the directory and copy them to the new location.
            FileInfo[] files = dir.GetFiles();
            Parallel.ForEach(files, opts, file => {
                string temppath = Path.Combine(destName, file.Name);
                context.Output(StringUtil.Loc("CopyFileToDestination", file, destName));
                file.CopyTo(temppath, true);
            });

            // If copying subdirectories, copy them and their contents to new location.
            if (copySubDirs)
            {
                foreach (DirectoryInfo subdir in dirs)
                {
                    string temppath = Path.Combine(destName, subdir.Name);
                    DirectoryCopy(subdir.FullName, temppath, copySubDirs, parallelCount, context);
                }
            }
        }
     
        private string NormalizeJobIdentifier(string jobIdentifier)
        {
            jobIdentifier = jobIdentifierRgx.Replace(jobIdentifier, string.Empty).Replace(".default", string.Empty);
            return jobIdentifier;
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

        // used for escaping the path to the Invoke-Robocopy.ps1 script that is passed to the powershell command
        private string pathToScriptPSString(string filePath)
        {
            // remove double quotes
            var result = filePath.Replace("\"", "");

            // double-up single quotes and enclose in single quotes. this is to create a single-quoted string in powershell.
            result = result.Replace("'", "''");
            return "'" + result + "'";
        }

        // used for escaping file paths that are ultimately passed to robocopy (via the powershell command)
        private string pathToRobocopyPSString(string filePath)
        {
            // the path needs to be fixed-up due to a robocopy quirk handling trailing backslashes.
            //
            // according to http://ss64.com/nt/robocopy.html:
            //   If either the source or desination are a "quoted long foldername" do not include a
            //   trailing backslash as this will be treated as an escape character, i.e. "C:\some path\"
            //   will fail but "C:\some path\\" or "C:\some path\." or "C:\some path" will work.
            //
            // furthermore, PowerShell implicitly double-quotes arguments to external commands when the
            // argument contains unquoted spaces.
            //
            // note, details on PowerShell quoting rules for external commands can be found in the
            // source code here:
            // https://github.com/PowerShell/PowerShell/blob/v0.6.0/src/System.Management.Automation/engine/NativeCommandParameterBinder.cs
            
            // remove double quotes
            var result = filePath.Replace("\"", "");

            // append a "." if the path ends with a backslash. e.g. "C:\some path\" -> "C:\some path\."
            if (result.EndsWith("\\")) {
                result += '.';
            }

            // double-up single quotes and enclose in single quotes. this is to create a single-quoted string in powershell.
            result = result.Replace("'", "''");
            return "'" + result + "'";
        }
    }

    internal static class FileShareArtifactUploadEventProperties
    {
        public static readonly string ArtifactName = "artifactname";
        public static readonly string ArtifactLocation = "artifactlocation";
        public static readonly string ArtifactType = "artifacttype";
        public static readonly string Browsable = "Browsable";
    }

    // Can be invoked from a build run or a release run should a build be set as the artifact. 
    public class DownloadPipelineArtifactTaskV1 : PipelineArtifactTaskPluginBaseV1
    {
        // Same as https://github.com/Microsoft/vsts-tasks/blob/master/Tasks/DownloadPipelineArtifactV1/task.json
        public override Guid Id => PipelineArtifactPluginConstants.DownloadPipelineArtifactTaskId;
        static readonly string buildTypeCurrent = "current";
        static readonly string buildTypeSpecific = "specific";
        static readonly string buildVersionToDownloadLatest = "latest";
        static readonly string buildVersionToDownloadSpecific = "specific";
        static readonly string buildVersionToDownloadLatestFromBranch = "latestFromBranch";

        protected override async Task ProcessCommandInternalAsync(
            AgentTaskPluginExecutionContext context, 
            CancellationToken token)
        {
            ArgUtil.NotNull(context, nameof(context));
            string artifactName = this.GetArtifactName(context);
            string branchName = context.GetInput(ArtifactEventProperties.BranchName, required: false);
            string buildPipelineDefinition = context.GetInput(ArtifactEventProperties.BuildPipelineDefinition, required: false);
            string buildType = context.GetInput(ArtifactEventProperties.BuildType, required: true);
            string buildTriggering = context.GetInput(ArtifactEventProperties.BuildTriggering, required: false);
            string buildVersionToDownload = context.GetInput(ArtifactEventProperties.BuildVersionToDownload, required: false);
            string targetPath = context.GetInput(TargetPath, required: true);
            string environmentBuildId = context.Variables.GetValueOrDefault(BuildVariables.BuildId)?.Value ?? string.Empty; // BuildID provided by environment.
            string itemPattern = context.GetInput(ArtifactEventProperties.ItemPattern, required: false);
            string projectName = context.GetInput(ArtifactEventProperties.Project, required: false);
            string tags = context.GetInput(ArtifactEventProperties.Tags, required: false);
            string userSpecifiedpipelineId = context.GetInput(PipelineId, required: false);

            string[] minimatchPatterns = itemPattern.Split(
                new[] { "\n" },
                StringSplitOptions.RemoveEmptyEntries
            );

            string[] tagsInput = tags.Split(
                new[] { "," },
                StringSplitOptions.None
            );

            PipelineArtifactServer server = new PipelineArtifactServer();
            PipelineArtifactDownloadParameters downloadParameters;
            if (buildType == buildTypeCurrent)
            {
                // TODO: use a constant for project id, which is currently defined in Microsoft.VisualStudio.Services.Agent.Constants.Variables.System.TeamProjectId (Ting)
                string projectIdStr = context.Variables.GetValueOrDefault("system.teamProjectId")?.Value;
                if(String.IsNullOrEmpty(projectIdStr))
                {
                    throw new ArgumentNullException("Project ID cannot be null.");
                }
                Guid projectId = Guid.Parse(projectIdStr);
                ArgUtil.NotEmpty(projectId, nameof(projectId));

                int pipelineId = 0;
                if (int.TryParse(environmentBuildId, out pipelineId) && pipelineId != 0)
                {
                    context.Output(StringUtil.Loc("DownloadingFromBuild", pipelineId));
                }
                else
                {
                    string hostType = context.Variables.GetValueOrDefault("system.hosttype")?.Value;
                    if (string.Equals(hostType, "Release", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(hostType, "DeploymentGroup", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(StringUtil.Loc("BuildIdIsNotAvailable", hostType ?? string.Empty));
                    }
                    else if (!string.Equals(hostType, "Build", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(StringUtil.Loc("CannotDownloadFromCurrentEnvironment", hostType ?? string.Empty));
                    }
                    else
                    {
                        // This should not happen since the build id comes from build environment. But a user may override that so we must be careful.
                        throw new ArgumentException(StringUtil.Loc("BuildIdIsNotValid", environmentBuildId));
                    }
                }
                downloadParameters = new PipelineArtifactDownloadParameters
                {
                    ProjectRetrievalOptions = BuildArtifactRetrievalOptions.RetrieveByProjectId,
                    ProjectId = projectId,
                    PipelineId = pipelineId,
                    ArtifactName = artifactName,
                    TargetDirectory = targetPath,
                    MinimatchFilters = minimatchPatterns,
                    MinimatchFilterWithArtifactName = false
                };
            }
            else if (buildType == buildTypeSpecific)
            {
                int pipelineId;
                if (buildVersionToDownload == buildVersionToDownloadLatest)
                {
                    pipelineId = await this.GetpipelineIdAsync(context, buildPipelineDefinition, buildVersionToDownload, projectName, tagsInput);
                }
                else if (buildVersionToDownload == buildVersionToDownloadSpecific)
                {
                    pipelineId = Int32.Parse(userSpecifiedpipelineId);
                }
                else if (buildVersionToDownload == buildVersionToDownloadLatestFromBranch)
                {
                    pipelineId = await this.GetpipelineIdAsync(context, buildPipelineDefinition, buildVersionToDownload, projectName, tagsInput, branchName);
                }
                else
                {
                    throw new InvalidOperationException("Unreachable code!");
                }
                downloadParameters = new PipelineArtifactDownloadParameters
                {
                    ProjectRetrievalOptions = BuildArtifactRetrievalOptions.RetrieveByProjectName,
                    ProjectName = projectName,
                    PipelineId = pipelineId,
                    ArtifactName = artifactName,
                    TargetDirectory = targetPath,
                    MinimatchFilters = minimatchPatterns,
                    MinimatchFilterWithArtifactName = false
                };
            }
            else
            {
                throw new InvalidOperationException($"Build type '{buildType}' is not recognized.");
            }

            string fullPath = this.CreateDirectoryIfDoesntExist(targetPath);

            DownloadOptions downloadOptions;
            if (string.IsNullOrEmpty(downloadParameters.ArtifactName))
            {
                downloadOptions = DownloadOptions.MultiDownload;
            }
            else
            {
                downloadOptions = DownloadOptions.SingleDownload;
            }

            context.Output(StringUtil.Loc("DownloadArtifactTo", targetPath));
            await server.DownloadAsync(context, downloadParameters, downloadOptions, token);
            context.Output(StringUtil.Loc("DownloadArtifactFinished"));
        }

        protected virtual string GetArtifactName(AgentTaskPluginExecutionContext context)
        {
            return context.GetInput(ArtifactEventProperties.ArtifactName, required: true);
        }

        private string CreateDirectoryIfDoesntExist(string targetPath)
        {
            string fullPath = Path.GetFullPath(targetPath);
            bool dirExists = Directory.Exists(fullPath);
            if (!dirExists)
            {
                Directory.CreateDirectory(fullPath);
            }
            return fullPath;
        }

        private async Task<int> GetpipelineIdAsync(AgentTaskPluginExecutionContext context, string buildPipelineDefinition, string buildVersionToDownload, string project, string[] tagFilters, string branchName=null)
        {
            var definitions = new List<int>() { Int32.Parse(buildPipelineDefinition) };
            VssConnection connection = context.VssConnection;
            BuildHttpClient buildHttpClient = connection.GetClient<BuildHttpClient>();
            List<Build> list;
            if (buildVersionToDownload == "latest")
            {
                list = await buildHttpClient.GetBuildsAsync(project, definitions, tagFilters: tagFilters, queryOrder: BuildQueryOrder.FinishTimeDescending, resultFilter: BuildResult.Succeeded);
            }
            else if (buildVersionToDownload == "latestFromBranch")
            {
                list = await buildHttpClient.GetBuildsAsync(project, definitions, branchName: branchName, tagFilters: tagFilters, queryOrder: BuildQueryOrder.FinishTimeDescending, resultFilter: BuildResult.Succeeded);
            }
            else
            {
                throw new InvalidOperationException("Unreachable code!");
            }

            if (list.Count > 0)
            {
                return list.First().Id;
            }
            else
            {
                throw new ArgumentException("No builds currently exist in the build definition supplied.");
            }
        }
    }

    public class DownloadPipelineArtifactTaskV1_1_0 : DownloadPipelineArtifactTaskV1
    {
        protected override string TargetPath => "downloadPath";
        protected override string PipelineId => "buildId";

        protected override string GetArtifactName(AgentTaskPluginExecutionContext context)
        {
            return context.GetInput(ArtifactEventProperties.ArtifactName, required: false);
        }
    }

    public class DownloadPipelineArtifactTaskV1_1_1 : DownloadPipelineArtifactTaskV1
    {
        protected override string GetArtifactName(AgentTaskPluginExecutionContext context)
        {
            return context.GetInput(ArtifactEventProperties.ArtifactName, required: false);
        }
    }

    // 1.1.2 is the same as 1.1.0 because we reverted 1.1.1 change.
    public class DownloadPipelineArtifactTaskV1_1_2 : DownloadPipelineArtifactTaskV1_1_0
    {
    }

    // 1.1.3 is the same as 1.1.0 because we reverted 1.1.1 change and the minimum agent version.
    public class DownloadPipelineArtifactTaskV1_1_3 : DownloadPipelineArtifactTaskV1_1_0
    {
    }
}