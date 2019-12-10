// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Services.Agent.Util;
using Pipelines = Microsoft.TeamFoundation.DistributedTask.Pipelines;

namespace Agent.Sdk
{
    public class ContainerInfo : ExecutionTargetInfo
    {
        private IDictionary<string, string> _userMountVolumes;
        private List<MountVolume> _mountVolumes;
        private IDictionary<string, string> _userPortMappings;
        private List<PortMapping> _portMappings;
        private Dictionary<string, string> _environmentVariables;
        private Dictionary<string, string> _pathMappings;
        private PlatformUtil.OS _imageOS;

        public PlatformUtil.OS ExecutionOS => _imageOS;

        public ContainerInfo()
        {
            this.IsJobContainer = true;
            if (PlatformUtil.RunningOnWindows)
            {
                _pathMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                _pathMappings = new Dictionary<string, string>();
            }
        }

        public ContainerInfo(Pipelines.ContainerResource container, Boolean isJobContainer = true)
        {
            this.ContainerName = container.Alias;

            string containerImage = container.Properties.Get<string>("image");
            ArgUtil.NotNullOrEmpty(containerImage, nameof(containerImage));

            this.ContainerImage = containerImage;
            this.ContainerDisplayName = $"{container.Alias}_{Pipelines.Validation.NameValidation.Sanitize(containerImage)}_{Guid.NewGuid().ToString("N").Substring(0, 6)}";
            this.ContainerRegistryEndpoint = container.Endpoint?.Id ?? Guid.Empty;
            this.ContainerCreateOptions = container.Properties.Get<string>("options");
            this.SkipContainerImagePull = container.Properties.Get<bool>("localimage");
            _environmentVariables = container.Environment != null ? new Dictionary<string, string>(container.Environment) : new Dictionary<string, string>();
            this.ContainerCommand = container.Properties.Get<string>("command", defaultValue: "");
            this.IsJobContainer = isJobContainer;
            this._imageOS = PlatformUtil.HostOS;
           _pathMappings = new Dictionary<string, string>( PlatformUtil.RunningOnWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

            if (container.Ports?.Count > 0)
            {
                foreach (var port in container.Ports)
                {
                    UserPortMappings[port] = port;
                }
            }
            if (container.Volumes?.Count > 0)
            {
                foreach (var volume in container.Volumes)
                {
                    UserMountVolumes[volume] = volume;
                }
            }
        }

        public string ContainerId { get; set; }
        public string ContainerDisplayName { get; private set; }
        public string ContainerNetwork { get; set; }
        public string ContainerNetworkAlias { get; set; }
        public string ContainerImage { get; set; }
        public string ContainerName { get; set; }
        public string ContainerCommand { get; set; }
        public string CustomNodePath { get; set; }
        public Guid ContainerRegistryEndpoint { get; private set; }
        public string ContainerCreateOptions { get; set; }
        public bool SkipContainerImagePull { get; private set; }
        public string CurrentUserName { get; set; }
        public string CurrentUserId { get; set; }
        public bool IsJobContainer { get; set; }
        public PlatformUtil.OS ImageOS {
            get
            {
                return _imageOS;
            }
            set
            {
                var previousImageOS = _imageOS;
                _imageOS = value;
                if (_pathMappings != null)
                {
                    var newMappings = new Dictionary<string, string>( _pathMappings.Comparer);
                    foreach (var mapping in _pathMappings)
                    {
                        newMappings[mapping.Key] = TranslateContainerPathForImageOS(previousImageOS, mapping.Value);
                    }
                    _pathMappings = newMappings;
                }
                if (_environmentVariables != null)
                {
                    var newEnvVars = new Dictionary<string, string>(_environmentVariables.Comparer);
                    foreach (var env in _environmentVariables)
                    {
                        newEnvVars[env.Key] = TranslateContainerPathForImageOS(previousImageOS, env.Value);
                    }
                    _environmentVariables = newEnvVars;
                }
            }
        }

        public Dictionary<string, string> ContainerEnvironmentVariables
        {
            get
            {
                if (_environmentVariables == null)
                {
                    _environmentVariables = new Dictionary<string, string>();
                }

                return _environmentVariables;
            }
        }

        public IDictionary<string, string> UserMountVolumes
        {
            get
            {
                if (_userMountVolumes == null)
                {
                    _userMountVolumes = new Dictionary<string, string>();
                }
                return _userMountVolumes;
            }
        }

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

        public IDictionary<string, string> UserPortMappings
        {
            get
            {
                if (_userPortMappings == null)
                {
                    _userPortMappings = new Dictionary<string, string>();
                }

                return _userPortMappings;
            }
        }

        public List<PortMapping> PortMappings
        {
            get
            {
                if (_portMappings == null)
                {
                    _portMappings = new List<PortMapping>();
                }

                return _portMappings;
            }
        }

        public Dictionary<string, string> PathMappings
        {
            get
            {
                if (_pathMappings == null)
                {
                    _pathMappings = new Dictionary<string, string>();
                }

                return _pathMappings;
            }
        }

        public string TranslateToContainerPath(string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                var comparison = PlatformUtil.RunningOnWindows
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
                foreach (var mapping in _pathMappings)
                {
                    if (string.Equals(path, mapping.Key, comparison))
                    {
                        return mapping.Value;
                    }

                    if (path.StartsWith(mapping.Key + Path.DirectorySeparatorChar, comparison) ||
                        path.StartsWith(mapping.Key + Path.AltDirectorySeparatorChar, comparison))
                    {
                        return mapping.Value + path.Remove(0, mapping.Key.Length);
                    }
                }
            }

            return path;
        }

        public string TranslateToHostPath(string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                var comparison = PlatformUtil.RunningOnWindows
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
                foreach (var mapping in _pathMappings)
                {
                    string retval = null;

                    if (string.Equals(path, mapping.Value, comparison))
                    {
                        retval = mapping.Key;
                    }
                    else if (path.StartsWith(mapping.Value + Path.DirectorySeparatorChar, comparison) ||
                             path.StartsWith(mapping.Value + Path.AltDirectorySeparatorChar, comparison))
                    {
                        retval = mapping.Key + path.Remove(0, mapping.Value.Length);
                    }

                    if (retval != null)
                    {
                        if (PlatformUtil.RunningOnWindows)
                        {
                            retval = retval.Replace("/", "\\");
                        }
                        else
                        {
                            retval = retval.Replace("\\","/");
                        }
                        return retval;
                    }
                }
            }

            return path;
        }

        public string TranslateContainerPathForImageOS(PlatformUtil.OS runningOs, string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                if (runningOs == PlatformUtil.OS.Windows && ImageOS == PlatformUtil.OS.Linux)
                {
                    return path.Replace("C:\\","/").Replace("\\", "/");
                }
            }
            return path;
        }

        public void AddPortMappings(List<PortMapping> portMappings)
        {
            foreach (var port in portMappings)
            {
                PortMappings.Add(port);
            }
        }

        public void AddPathMappings(Dictionary<string, string> pathMappings)
        {
            foreach (var path in pathMappings)
            {
                PathMappings.Add(path.Key, path.Value);
            }
        }
    }

    public class MountVolume
    {
        public MountVolume()
        {

        }

        public MountVolume(string sourceVolumePath, string targetVolumePath, bool readOnly = false)
        {
            this.SourceVolumePath = sourceVolumePath;
            this.TargetVolumePath = targetVolumePath;
            this.ReadOnly = readOnly;
        }

        public MountVolume(string fromString)
        {
            ParseVolumeString(fromString);
        }

        private static Regex autoEscapeWindowsDriveRegex = new Regex(@"(^|:)([a-zA-Z]):(\\|/)", RegexOptions.Compiled);
        private string AutoEscapeWindowsDriveInPath(string path)
        {

            return autoEscapeWindowsDriveRegex.Replace(path, @"$1$2\:$3");
        }

        private void ParseVolumeString(string volume)
        {
            ReadOnly = false;
            SourceVolumePath = null;

            string readonlyToken = ":ro";
            if (volume.ToLower().EndsWith(readonlyToken))
            {
                ReadOnly = true;
                volume = volume.Remove(volume.Length-readonlyToken.Length);
            }
            // for completeness, in case someone explicitly added :rw in the volume mapping, we should strip it as well
            string readWriteToken = ":rw";
            if (volume.ToLower().EndsWith(readWriteToken))
            {
                ReadOnly = false;
                volume = volume.Remove(volume.Length-readWriteToken.Length);
            }

            if (volume.StartsWith(":"))
            {
                volume = volume.Substring(1);
            }

            var volumes = new List<string>();
            // split by colon, but honor escaping of colons
            var volumeSplit = AutoEscapeWindowsDriveInPath(volume).Split(':');
            var appendNextIteration = false;
            foreach (var fragment in volumeSplit)
            {
                if (appendNextIteration)
                {
                    var orig = volumes[volumes.Count - 1];
                    orig = orig.Remove(orig.Length - 1); // remove the trailing backslash
                    volumes[volumes.Count - 1] = orig + ":" + fragment;
                    appendNextIteration = false;
                }
                else
                {
                    volumes.Add(fragment);
                }
                // if this fragment ends with backslash, then the : was escaped
                if (fragment.EndsWith(@"\"))
                {
                    appendNextIteration = true;
                }
            }

            if (volumes.Count >= 2)
            {
                // source:target
                SourceVolumePath = volumes[0];
                TargetVolumePath = volumes[1];
                // if volumes.Count > 2 here, we should log something that says we ignored options passed in.
                // for now, do nothing in order to remain backwards compatable.
            }
            else
            {
                // target - or, default to passing straight through
                TargetVolumePath = volume;
            }
        }

        public string SourceVolumePath { get; set; }
        public string TargetVolumePath { get; set; }
        public bool ReadOnly { get; set; }
    }

    public class PortMapping
    {

        public PortMapping()
        {

        }

        public PortMapping(string hostPort, string containerPort, string protocol)
        {
            this.HostPort = hostPort;
            this.ContainerPort = containerPort;
            this.Protocol = protocol;
        }

        public string HostPort { get; set; }
        public string ContainerPort { get; set; }
        public string Protocol { get; set; }
    }

    public class DockerVersion
    {
        public DockerVersion()
        {

        }

        public DockerVersion(Version serverVersion, Version clientVersion)
        {
            this.ServerVersion = serverVersion;
            this.ClientVersion = clientVersion;
        }

        public Version ServerVersion { get; set; }
        public Version ClientVersion { get; set; }
    }
}