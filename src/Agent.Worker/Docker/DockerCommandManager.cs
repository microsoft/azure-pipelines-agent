using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.VisualStudio.Services.Agent.Worker.Docker
{
    [ServiceLocator(Default = typeof(DockerCommandManager))]
    public interface IDockerCommandManager : IAgentService
    {
        Task<string> DockerCreate(IExecutionContext context, string image);
        Task<string> DockerStart(IExecutionContext context, string containerId);
        Task<int> DockerExec(IExecutionContext context, string command, string args);
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

            return output.FirstOrDefault();
        }

        public async Task<string> DockerStart(IExecutionContext context, string containerId)
        {
            string dockerArgs = $"start {containerId}";
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

            return output.FirstOrDefault();
        }

        public async Task<int> DockerExec(IExecutionContext context, string command, string args)
        {
            string dockerArgs = $"exec {command} {args}";
            var processInvoker = HostContext.CreateService<IProcessInvoker>();
            processInvoker.OutputDataReceived += delegate (object sender, ProcessDataReceivedEventArgs message)
            {
                context.Output(message.Data);
            };

            processInvoker.ErrorDataReceived += delegate (object sender, ProcessDataReceivedEventArgs message)
            {
                context.Output(message.Data);
            };

            return await processInvoker.ExecuteAsync(
                workingDirectory: HostContext.GetDirectory(WellKnownDirectory.Work),
                fileName: "docker",
                arguments: dockerArgs,
                environment: null,
                requireExitCodeZero: false,
                outputEncoding: null,
                cancellationToken: default(CancellationToken));
        }
    }
}