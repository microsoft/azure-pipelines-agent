using System;
using System.Collections.Generic;

namespace Microsoft.VisualStudio.Services.Agent.Worker.Docker
{
    public class DockerInfo
    {
        public string ContainerId { get; set; }
        public DirectoryMount SharedDirectory { get; set; }
    }

    public class DirectoryMount
    {
        public DirectoryMount(string sourceDir, string containerDir, Permission permission)
        {
            SourceDirectory = sourceDir;
            ContainerDirectory = containerDir;
            DirectoryPermission = permission;
        }

        public string SourceDirectory { get; }
        public string ContainerDirectory { get; }
        public Permission DirectoryPermission { get; }

    }

    [Flags]
    public enum Permission
    {
        None = 0,
        Read = 1,
        Write = 2,
        Execute = 4,
        All = Read | Write | Execute,
    }
}