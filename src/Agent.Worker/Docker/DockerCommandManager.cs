using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Microsoft.VisualStudio.Services.Agent.Worker.Docker
{
    [ServiceLocator(Default = typeof(DockerCommandManager))]
    public interface IDockerCommandManager : IAgentService
    {
        Task<int> DockerPull(IExecutionContext context, string image);
        Task<string> DockerCreate(IExecutionContext context, string image, DirectoryMount sharedDirectory);
        Task<string> DockerStart(IExecutionContext context, string containerId);
        Task<int> DockerStop(IExecutionContext context, string containerId);
        Task<int> DockerRM(IExecutionContext context, string containerId);
        Task<int> DockerExec(IExecutionContext context, string command, string args);
    }

    public class DockerCommandManager : AgentService, IDockerCommandManager
    {
        private string _dockerPath;

        public override void Initialize(IHostContext hostContext)
        {
            base.Initialize(hostContext);

            var whichUtil = HostContext.GetService<IWhichUtil>();
            _dockerPath = whichUtil.Which("docker", true);
        }

        public async Task<int> DockerPull(IExecutionContext context, string image)
        {
            string dockerArgs = $"pull {image}";
            context.Command($"{_dockerPath} {dockerArgs}");
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
                fileName: _dockerPath,
                arguments: dockerArgs,
                environment: null,
                requireExitCodeZero: false,
                outputEncoding: null,
                cancellationToken: default(CancellationToken));
        }

        public async Task<string> DockerCreate(IExecutionContext context, string image, DirectoryMount sharedDirectory)
        {
            string dockerArgs = $"create -v {sharedDirectory.SourceDirectory}:{sharedDirectory.ContainerDirectory} {image}";
            context.Command($"{_dockerPath} {dockerArgs}");
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
                fileName: _dockerPath,
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
            context.Command($"{_dockerPath} {dockerArgs}");
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
                fileName: _dockerPath,
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
            context.Command($"{_dockerPath} {dockerArgs}");
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
                fileName: _dockerPath,
                arguments: dockerArgs,
                environment: null,
                requireExitCodeZero: false,
                outputEncoding: null,
                cancellationToken: default(CancellationToken));
        }

        public async Task<int> DockerStop(IExecutionContext context, string containerId)
        {
            string dockerArgs = $"stop {containerId}";
            context.Command($"{_dockerPath} {dockerArgs}");
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
                fileName: _dockerPath,
                arguments: dockerArgs,
                environment: null,
                requireExitCodeZero: false,
                outputEncoding: null,
                cancellationToken: default(CancellationToken));
        }

        public async Task<int> DockerRM(IExecutionContext context, string containerId)
        {
            string dockerArgs = $"rm {containerId}";
            context.Command($"{_dockerPath} {dockerArgs}");
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
                fileName: _dockerPath,
                arguments: dockerArgs,
                environment: null,
                requireExitCodeZero: false,
                outputEncoding: null,
                cancellationToken: default(CancellationToken));
        }
    }
}