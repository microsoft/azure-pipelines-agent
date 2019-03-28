using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;
using Microsoft.VisualStudio.Services.BlobStore.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Newtonsoft.Json;
using Agent.Sdk;
using Microsoft.VisualStudio.Services.FileContainer;
using Microsoft.VisualStudio.Services.FileContainer.Client;
using Microsoft.TeamFoundation.Core.WebApi;

namespace Agent.Plugins.PipelineArtifact
{    
    // A wrapper of BuildDropManager, providing basic functionalities such as uploading and downloading pipeline artifacts.
    public class PipelineArtifactServer
    {
        public static readonly string RootId = "RootId";
        public static readonly string ProofNodes = "ProofNodes";
        public const string PipelineArtifactTypeName = "PipelineArtifact";
        public const string FileContainerArtifactTypeName = "Container";

        // Upload from target path to VSTS BlobStore service through BuildDropManager, then associate it with the build
        internal async Task UploadAsync(
            AgentTaskPluginExecutionContext context,
            Guid projectId,
            int buildId,
            string name,
            string source,
            CancellationToken cancellationToken)
        {
            VssConnection connection = context.VssConnection;
            var buildDropManager = this.CreateBulidDropManager(context, connection);

            //Upload the pipeline artifact.
            var result = await buildDropManager.PublishAsync(source, cancellationToken);

            // 2) associate the pipeline artifact with an build artifact
            BuildServer buildHelper = new BuildServer(connection);
            Dictionary<string, string> propertiesDictionary = new Dictionary<string, string>();
            propertiesDictionary.Add(RootId, result.RootId.ValueString);
            propertiesDictionary.Add(ProofNodes, StringUtil.ConvertToJson(result.ProofNodes.ToArray()));
            var artifact = await buildHelper.AssociateArtifact(projectId, buildId, name, ArtifactResourceTypes.PipelineArtifact, result.ManifestId.ValueString, propertiesDictionary, cancellationToken);
            context.Output(StringUtil.Loc("AssociateArtifactWithBuild", artifact.Id, buildId));
        }

        // Download pipeline artifact from VSTS BlobStore service through BuildDropManager to a target path
        // Old V0 function
        internal Task DownloadAsync(
            AgentTaskPluginExecutionContext context,
            Guid projectId,
            int buildId,
            string artifactName,
            string targetDir,
            CancellationToken cancellationToken)
        {
            var downloadParameters = new PipelineArtifactDownloadParameters
            {
                ProjectRetrievalOptions = BuildArtifactRetrievalOptions.RetrieveByProjectId,
                ProjectId = projectId,
                BuildId = buildId,
                ArtifactName = artifactName,
                TargetDirectory = targetDir
            };

            return this.DownloadAsync(context, downloadParameters, cancellationToken);
        }

        // Download with minimatch patterns.
        internal async Task DownloadAsync(
            AgentTaskPluginExecutionContext context,
            PipelineArtifactDownloadParameters downloadParameters,
            CancellationToken cancellationToken)
        {
            VssConnection connection = context.VssConnection;
            var buildDropManager = this.CreateBulidDropManager(context, connection);
            BuildServer buildHelper = new BuildServer(connection);
            
            // download all pipeline artifacts if artifact name is missing
            if (string.IsNullOrEmpty(downloadParameters.ArtifactName))
            {
                List<BuildArtifact> artifacts;
                if (downloadParameters.ProjectRetrievalOptions == BuildArtifactRetrievalOptions.RetrieveByProjectId)
                {
                    artifacts = await buildHelper.GetArtifactsAsync(downloadParameters.ProjectId, downloadParameters.BuildId, cancellationToken);
                }
                else if (downloadParameters.ProjectRetrievalOptions == BuildArtifactRetrievalOptions.RetrieveByProjectName)
                {
                    if (string.IsNullOrEmpty(downloadParameters.ProjectName))
                    {
                        throw new InvalidOperationException("Project name can't be empty when trying to fetch build artifacts!");
                    }
                    else
                    {
                        artifacts = await buildHelper.GetArtifactsWithProjectNameAsync(downloadParameters.ProjectName, downloadParameters.BuildId, cancellationToken);
                    }
                }
                else
                {
                    throw new InvalidOperationException("Unreachable code!");
                }

                IEnumerable<BuildArtifact> pipelineArtifacts = artifacts.Where(a => (a.Resource.Type == PipelineArtifactTypeName) || (a.Resource.Type == FileContainerArtifactTypeName));
                if (pipelineArtifacts.Count() == 0)
                {
                    throw new ArgumentException("Could not find any pipeline artifacts in the build.");
                }
                else
                {
                    context.Output(StringUtil.Loc("DownloadingMultiplePipelineArtifacts", pipelineArtifacts.Count()));
                    await DownloadArtifactsAsync(
                        connection,
                        downloadParameters.ProjectId,
                        buildDropManager,
                        pipelineArtifacts,
                        downloadParameters.TargetDirectory,
                        downloadParameters.MinimatchFilters,
                        cancellationToken);
                }
            }
            else
            {
                // 1) get manifest id from artifact data
                BuildArtifact buildArtifact;
                if (downloadParameters.ProjectRetrievalOptions == BuildArtifactRetrievalOptions.RetrieveByProjectId)
                {
                    buildArtifact = await buildHelper.GetArtifact(downloadParameters.ProjectId, downloadParameters.BuildId, downloadParameters.ArtifactName, cancellationToken);
                }
                else if (downloadParameters.ProjectRetrievalOptions == BuildArtifactRetrievalOptions.RetrieveByProjectName)
                {
                    if (string.IsNullOrEmpty(downloadParameters.ProjectName))
                    {
                        throw new InvalidOperationException("Project name can't be empty when trying to fetch build artifacts!");
                    }
                    else
                    {
                        buildArtifact = await buildHelper.GetArtifactWithProjectNameAsync(downloadParameters.ProjectName, downloadParameters.BuildId, downloadParameters.ArtifactName, cancellationToken);
                    }
                }
                else
                {
                    throw new InvalidOperationException("Unreachable code!");
                }

                await DownloadArtifactsAsync(
                    connection,
                    downloadParameters.ProjectId,
                    buildDropManager, 
                    new List<BuildArtifact>(){ buildArtifact },
                    downloadParameters.TargetDirectory, 
                    downloadParameters.MinimatchFilters, 
                    cancellationToken);
            }
        }

        private BuildDropManager CreateBulidDropManager(AgentTaskPluginExecutionContext context, VssConnection connection)
        {
            var dedupStoreHttpClient = connection.GetClient<DedupStoreHttpClient>();
            var tracer = new CallbackAppTraceSource(str => context.Output(str), System.Diagnostics.SourceLevels.Information);
            dedupStoreHttpClient.SetTracer(tracer);
            var client = new DedupStoreClientWithDataport(dedupStoreHttpClient, 16 * Environment.ProcessorCount);
            var buildDropManager = new BuildDropManager(client, tracer);
            return buildDropManager;
        }

        private async Task DownloadArtifactsAsync(
            VssConnection connection,
            Guid projectId,
            BuildDropManager buildDropManager,
            IEnumerable<BuildArtifact> buildArtifacts,
            string targetDirectory,
            string[] minimatchFilters,
            CancellationToken cancellationToken)
        {
            var artifactsByType = (from a in buildArtifacts
                                   group a by a.Resource.Type into g
                                   select g).ToDictionary(g => g.Key);

            if (artifactsByType.ContainsKey(PipelineArtifactTypeName))
            {
                var pipelineArtifactsToDownload = artifactsByType[PipelineArtifactTypeName].AsEnumerable();
                await DownloadPipelineArtifactsAsync(buildDropManager, pipelineArtifactsToDownload, targetDirectory, minimatchFilters, cancellationToken);
            }

            if (artifactsByType.ContainsKey(FileContainerArtifactTypeName))
            {
                var fileContainersToDownload = artifactsByType[FileContainerArtifactTypeName].AsEnumerable();
                await DownloadFileContainersAsync(connection, projectId, fileContainersToDownload, targetDirectory, minimatchFilters, cancellationToken);
            }
        }

        private async Task DownloadFileContainersAsync(VssConnection connection, Guid projectId, IEnumerable<BuildArtifact> buildArtifacts, string targetDirectory, string[] minimatchFilters, CancellationToken cancellationToken)
        {
            if (buildArtifacts.Count() == 0)
            {
                return;
            }
            else if (buildArtifacts.Count() > 1)
            {
                foreach (var buildArtifact in buildArtifacts)
                {
                    var specificPath = Path.Combine(targetDirectory, buildArtifact.Name);
                    await DownloadFileContainerAsync(connection, projectId, buildArtifact, specificPath, cancellationToken);
                }
            }
            else
            {
                await DownloadFileContainerAsync(connection, projectId, buildArtifacts.Single(), targetDirectory, cancellationToken);
            }
        }

        private Tuple<long, string> ParseContainerId(string resourceData)
        {
            // Example of resourceData: "#/7029766/artifacttool-alpine-x64-Debug"
            var segments = resourceData.Split('/');

            long containerId;
            if (segments.Length == 3 && segments[0] == "#" && long.TryParse(segments[1], out containerId))
            {
                return new Tuple<long, string>(
                    containerId,
                    segments[2]
                    );
            }
            else
            {
                var message = $"Resource data value '{resourceData}' was not expected.";
                throw new ArgumentException(message, "resourceData");
            }
        }

        private async Task DownloadFileContainerAsync(VssConnection connection, Guid projectId, BuildArtifact artifact, string rootPath, CancellationToken cancellationToken)
        {
            var containerIdAndRoot = ParseContainerId(artifact.Resource.Data);

            var containerClient = connection.GetClient<FileContainerHttpClient>();

            var items = await containerClient.QueryContainerItemsAsync(
                containerIdAndRoot.Item1,
                projectId,
                containerIdAndRoot.Item2
                );

            // Group items by type because we want to create the folder structure first.
            var groupedItems = from i in items
                               group i by i.ItemType into g
                               select g;

            // Now create the folders.
            var folderItems = groupedItems.Single(i => i.Key == ContainerItemType.Folder);
            Parallel.ForEach(folderItems, (folder) =>
            {
                var targetPath = ResolveTargetPath(rootPath, folder);
                Directory.CreateDirectory(targetPath);
            });

            // Then download the files.
            var fileItems = groupedItems.Single(i => i.Key == ContainerItemType.File);
            Parallel.ForEach(fileItems, (file) =>
            {
                var targetPath = ResolveTargetPath(rootPath, file);
                DownloadFileFromContainerAsync(containerIdAndRoot, projectId, containerClient, file, targetPath, cancellationToken).Wait();
            });
        }

        private async Task DownloadFileFromContainerAsync(Tuple<long, string> containerIdAndRoot, Guid scopeIdentifier, FileContainerHttpClient containerClient, FileContainerItem item, string targetPath, CancellationToken cancellationToken)
        {
            var stream = await containerClient.DownloadFileAsync(
                containerIdAndRoot.Item1,
                item.Path,
                cancellationToken,
                scopeIdentifier
                );

            using (var fileStream = new FileStream(targetPath, FileMode.Create))
            {
                stream.CopyTo(fileStream);
            }
        }

        private string ResolveTargetPath(string rootPath, FileContainerItem item)
        {
            var indexOfFirstPathSeperator = item.Path.IndexOf('/');
            var itemPathWithoutDirectoryPrefix = indexOfFirstPathSeperator != -1 ? item.Path.Substring(indexOfFirstPathSeperator + 1) : string.Empty;
            var targetPath = System.IO.Path.Combine(rootPath, itemPathWithoutDirectoryPrefix);
            return targetPath;
        }


        private Task DownloadPipelineArtifactsAsync(
            BuildDropManager buildDropManager,
            IEnumerable<BuildArtifact> buildArtifacts,
            string targetDirectory,
            string[] minimatchFilters,
            CancellationToken cancellationToken)
        {
            IDictionary<string, DedupIdentifier> artifactNameAndManifestId = new Dictionary<string, DedupIdentifier>();
            foreach (var buildArtifact in buildArtifacts)
            {
                if (buildArtifact.Resource.Type != PipelineArtifactTypeName)
                {
                    throw new ArgumentException("The artifact is not of the type Pipeline Artifact.");
                }
                artifactNameAndManifestId.Add(buildArtifact.Name, DedupIdentifier.Create(buildArtifact.Resource.Data));
            }

            // 2) download to the target path
            DownloadPipelineArtifactOptions options = DownloadPipelineArtifactOptions.CreateWithMultiManifestIds(
                artifactNameAndManifestId,
                targetDirectory,
                proxyUri: null,
                minimatchPatterns: minimatchFilters);
            return buildDropManager.DownloadAsync(options, cancellationToken);
        }
    } 

    internal class PipelineArtifactDownloadParameters
    {
        /// <remarks>
        /// Options on how to retrieve the build using the following parameters.
        /// </remarks>
        public BuildArtifactRetrievalOptions ProjectRetrievalOptions { get; set; }
        /// <remarks>
        /// Either project ID or project name need to be supplied.
        /// </remarks>
        public Guid ProjectId { get; set; }
        /// <remarks>
        /// Either project ID or project name need to be supplied.
        /// </remarks>
        public string ProjectName { get; set; }
        public int BuildId { get; set; }
        public string ArtifactName { get; set; }
        public string TargetDirectory { get; set; }
        public string[] MinimatchFilters { get; set; }
    }

    internal enum BuildArtifactRetrievalOptions
    {
        RetrieveByProjectId,
        RetrieveByProjectName
    }
}