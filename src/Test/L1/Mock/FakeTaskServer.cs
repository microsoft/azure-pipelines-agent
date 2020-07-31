// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using System;
using Microsoft.VisualStudio.Services.WebApi;

namespace Microsoft.VisualStudio.Services.Agent.Tests.L1.Worker
{
    public class FakeTaskServer : AgentService, ITaskServer
    {
        public Task ConnectAsync(VssConnection jobConnection)
        {
            return Task.CompletedTask;
        }

        public Task<Stream> GetTaskContentZipAsync(Guid taskId, TaskVersion taskVersion, CancellationToken token)
        {
            String taskZip = Path.Join(HostContext.GetDirectory(WellKnownDirectory.Externals), "Tasks", taskId.ToString() + ".zip");
            if (File.Exists(taskZip))
            {
                return Task.FromResult<Stream>(new FileStream(taskZip, FileMode.Open, FileAccess.Read, FileShare.Read));
            }
            else
            {
                throw new Exception("A step specified a task which does not exist in the L1 test framework. Any tasks used by L1 tests must be added manually.");
            }
        }

        public Task<bool> TaskDefinitionEndpointExist()
        {
            return Task.FromResult(true);
        }
    }
}