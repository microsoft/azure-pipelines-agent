using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.Services.Agent.Util;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;

namespace Microsoft.VisualStudio.Services.Agent.Worker.Container
{
    public class ContainerInfo
    {
        public ContainerInfo(Pipelines.ContainerReference container)
        {
            this.ContainerName = container.Name;

            container.Data.TryGetValue("image", out string containerImage);
            ArgUtil.NotNullOrEmpty(containerImage, nameof(containerImage));
            this.ContainerImage = containerImage;

            this.ContainerDisplayName = $"{container.Name}_{Pipelines.Validation.NameValidation.Sanitize(containerImage)}";

            container.Data.TryGetValue("registry", out string containerRegistry);
            this.ContainerRegistryEndpoint = containerRegistry;

            container.Data.TryGetValue("options", out string containerCreateOptions);
            this.ContainerCreateOptions = containerCreateOptions;

            container.Data.TryGetValue("localimage", out string localImage);
            this.SkipContainerImagePull = StringUtil.ConvertToBoolean(localImage);
        }

        private List<MountVolume> _mountVolumes;

        public string ContainerId { get; set; }
        public string ContainerName { get; set; }
        public string ContainerDisplayName { get; set; }
        public string ContainerImage { get; set; }
        public string ContainerRegistryEndpoint { get; set; }
        public string ContainerCreateOptions { get; set; }
        public bool SkipContainerImagePull { get; set; }
        public bool ContainerCreateStepAssigned { get; set; }
        public string CurrentUserName { get; set; }
        public string CurrentUserId { get; set; }

        public List<MountVolume> MountVolumes
        {
            get
            {
                if (_mountVolumes == null)
                {
                    _mountVolumes = new List<MountVolume>();
                }

                return _mountVolumes;
            }
        }
    }

    public class MountVolume
    {
        public MountVolume(string volumePath, bool readOnly = false)
        {
            this.VolumePath = volumePath;
            this.ReadOnly = readOnly;
        }

        public string VolumePath { get; set; }
        public bool ReadOnly { get; set; }
    }

    public class DockerVersion
    {
        public DockerVersion(Version serverVersion, Version clientVersion)
        {
            this.ServerVersion = serverVersion;
            this.ClientVersion = clientVersion;
        }

        public Version ServerVersion { get; set; }
        public Version ClientVersion { get; set; }
    }
}