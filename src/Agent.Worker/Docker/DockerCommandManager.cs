using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.IO;

namespace Microsoft.VisualStudio.Services.Agent.Worker.Build
{
    public interface IDockerCommandManager : IAgentService
    {
        Task<string> DockerCreate(IExecutionContext context, string image);
    }

    public class DockerCommandManager : AgentService, IDockerCommandManager
    {
        public async Task<string> DockerCreate(IExecutionContext context, string image)
        {
            string dockerArgs = $"create {image}";
            List<string> output = new List<string>();
            object outputLock = new object();
            var processInvoker = HostContext.CreateService<IProcessInvoker>();
            processInvoker.OutputDataReceived += delegate (object sender, ProcessDataReceivedEventArgs message)
            {
                lock (outputLock)
                {
                    if (!string.IsNullOrEmpty(message.Data))
                    {
                        output.Add(message.Data);
                    }
                }
            };

            processInvoker.ErrorDataReceived += delegate (object sender, ProcessDataReceivedEventArgs message)
            {
                lock (outputLock)
                {
                    if (!string.IsNullOrEmpty(message.Data))
                    {
                        output.Add(message.Data);
                    }
                }
            };

            await processInvoker.ExecuteAsync(
                workingDirectory: HostContext.GetDirectory(WellKnownDirectory.Work),
                fileName: "docker",
                arguments: dockerArgs,
                environment: null,
                requireExitCodeZero: true,
                outputEncoding: null,
                cancellationToken: default(CancellationToken));

            return output.FirstOrDefault();;
        }
    }
}