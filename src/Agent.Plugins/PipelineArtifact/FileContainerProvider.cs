using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.FileContainer;
using Microsoft.VisualStudio.Services.FileContainer.Client;
using Microsoft.VisualStudio.Services.WebApi;
using Minimatch;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Agent.Plugins.PipelineArtifact
{
    class FileContainerProvider : IArtifactProvider
    {
        private FileContainerHttpClient containerClient;
        // https://github.com/Microsoft/azure-pipelines-task-lib/blob/master/node/docs/findingfiles.md#matchoptions
        private static readonly Options minimatchOptions = new Options
        {
            Dot = true,
            NoBrace = true,
            NoCase = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? true : false
        };

        public FileContainerProvider(VssConnection connection)
        {
            containerClient = connection.GetClient<FileContainerHttpClient>();
        }
        public async Task DownloadSingleArtifactAsync(PipelineArtifactDownloadParameters downloadParameters, BuildArtifact buildArtifact, CancellationToken cancellationToken)
        {
            await this.DownloadFileContainerAsync(downloadParameters.ProjectId, buildArtifact, downloadParameters.TargetDirectory, downloadParameters.MinimatchFilters, cancellationToken);
        }

        public async Task DownloadMultipleArtifactsAsync(PipelineArtifactDownloadParameters downloadParameters, IEnumerable<BuildArtifact> buildArtifacts, CancellationToken cancellationToken)
        {
            await this.DownloadFileContainersAsync(downloadParameters.ProjectId, buildArtifacts, downloadParameters.TargetDirectory, downloadParameters.MinimatchFilters, cancellationToken);
        }

        public async Task DownloadFileContainersAsync(Guid projectId, IEnumerable<BuildArtifact> buildArtifacts, string targetDirectory, string[] minimatchFilters, CancellationToken cancellationToken)
        {
            if (buildArtifacts.Count() == 0)
            {
                return;
            }
                foreach (var buildArtifact in buildArtifacts)
                {
                    var specificPath = Path.Combine(targetDirectory, buildArtifact.Name);
                    await DownloadFileContainerAsync(projectId, buildArtifact, specificPath, minimatchFilters, cancellationToken);
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

        private async Task DownloadFileContainerAsync(Guid projectId, BuildArtifact artifact, string rootPath, IEnumerable<string> minimatchPatterns, CancellationToken cancellationToken)
        {
            var containerIdAndRoot = ParseContainerId(artifact.Resource.Data);

            var items = await containerClient.QueryContainerItemsAsync(
                containerIdAndRoot.Item1,
                projectId,
                containerIdAndRoot.Item2
                );

            IEnumerable<Func<string, bool>> minimatcherFuncs = this.GetMinimatchFuncs(minimatchPatterns);
            if(minimatcherFuncs !=null && minimatcherFuncs.Count() !=0)
            {
                items = this.GetFilteredItems(items, minimatcherFuncs);
            }

            // Group items by type because we want to create the folder structure first.
            var groupedItems = from i in items
                               group i by i.ItemType into g
                               select g;

            // Now create the folders.
            var folderItems = groupedItems.SingleOrDefault(i => i.Key == ContainerItemType.Folder);
            if (folderItems != null)
            {
                Parallel.ForEach(folderItems, (folder) =>
                {
                    var targetPath = ResolveTargetPath(rootPath, folder);
                    Directory.CreateDirectory(targetPath);
                });
            }

            // Then download the files.
            var fileItems = groupedItems.SingleOrDefault(i => i.Key == ContainerItemType.File);
            if (fileItems != null)
            {
                Parallel.ForEach(fileItems, (file) =>
                {
                    var targetPath = ResolveTargetPath(rootPath, file);
                    DownloadFileFromContainerAsync(containerIdAndRoot, projectId, containerClient, file, targetPath, cancellationToken).Wait();
                });
            }
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
            var targetPath = Path.Combine(rootPath, itemPathWithoutDirectoryPrefix);
            return targetPath;
        }

        private IEnumerable<Func<string, bool>> GetMinimatchFuncs(IEnumerable<string> minimatchPatterns)
        {
            IEnumerable<Func<string, bool>> minimatcherFuncs;
            if (minimatchPatterns != null && minimatchPatterns.Count() != 0)
            {
                string minimatchPatternMsg = $"Minimatch patterns: [{ string.Join(",", minimatchPatterns) }]";
                minimatcherFuncs = minimatchPatterns
                    .Where(pattern => !string.IsNullOrEmpty(pattern)) // get rid of empty strings to avoid filtering out whole item list.
                    .Select(pattern => Minimatcher.CreateFilter(pattern, minimatchOptions));
            }
            else
            {
                minimatcherFuncs = null;
            }

            return minimatcherFuncs;
        }

        private List<FileContainerItem> GetFilteredItems(List<FileContainerItem> items, IEnumerable<Func<string, bool>> minimatchFuncs)
        {
            List<FileContainerItem> filteredItems = new List<FileContainerItem>();
            foreach(FileContainerItem item in items)
            {
                int index = item.Path.IndexOf('/');
                // trim the leading slash from the item paths
                if (minimatchFuncs.Any(match => match(item.Path.Substring(index+1))))
                {
                    filteredItems.Add(item);
                }
            }
            return filteredItems;
        }
    }
}
