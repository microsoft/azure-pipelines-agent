// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace Agent.Sdk
{
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