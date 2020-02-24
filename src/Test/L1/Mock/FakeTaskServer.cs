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
            String baseDirectory = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            String taskZipsPath = Path.Join(baseDirectory, "TaskZips");

            foreach (String zip in Directory.GetFiles(taskZipsPath))
            {
                String zipName = Path.GetFileNameWithoutExtension(zip);
                if (zipName.Equals(taskId.ToString()))
                {
                    int triesRemaining = 10;
                    while (triesRemaining > 0)
                    {
                        // Basic lock - if 2 processes try to create a filestream from the same file, one will receive an IO exception.
                        // This just spins until it succeeds
                        try
                        {
                            return Task.FromResult<Stream>(new FileStream(zip, FileMode.Open));
                        }
                        catch (System.IO.IOException)
                        {
                            Thread.Sleep(1000);
                            triesRemaining--;
                        }
                    }
                }
            }

            return Task.FromResult<Stream>(null);
        }

        public Task<bool> TaskDefinitionEndpointExist()
        {
            return Task.FromResult(true);
        }
    }
}