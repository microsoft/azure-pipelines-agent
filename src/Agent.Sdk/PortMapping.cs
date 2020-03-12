// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Agent.Sdk
{
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
}