using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.FileContainer;
using Microsoft.VisualStudio.Services.FileContainer.Client;
using Microsoft.VisualStudio.Services.WebApi;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Agent.Plugins.PipelineArtifact
{
    class FileContainerServer
    {
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
    }
}
