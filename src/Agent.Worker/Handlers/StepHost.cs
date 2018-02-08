using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.DistributedTask.ServiceEndpoints;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker.Container;

namespace Microsoft.VisualStudio.Services.Agent.Worker.Handlers
{
    public interface IStepHost : IAgentService
    {
        event EventHandler<ProcessDataReceivedEventArgs> OutputDataReceived;
        event EventHandler<ProcessDataReceivedEventArgs> ErrorDataReceived;

        Task<int> ExecuteAsync(
            string workingDirectory,
            string fileName,
            string arguments,
            IDictionary<string, string> environment,
            bool requireExitCodeZero,
            Encoding outputEncoding,
            bool killProcessOnCancel,
            CancellationToken cancellationToken);
    }

    [ServiceLocator(Default = typeof(ContainerStepHost))]
    public interface IContainerStepHost : IStepHost
    {
        ContainerInfo Container { get; set; }
    }

    [ServiceLocator(Default = typeof(DefaultStepHost))]
    public interface IDefaultStepHost : IStepHost
    {
    }


    public sealed class DefaultStepHost : AgentService, IDefaultStepHost
    {
        public event EventHandler<ProcessDataReceivedEventArgs> OutputDataReceived;
        public event EventHandler<ProcessDataReceivedEventArgs> ErrorDataReceived;

        public async Task<int> ExecuteAsync(
             string workingDirectory,
             string fileName,
             string arguments,
             IDictionary<string, string> environment,
             bool requireExitCodeZero,
             Encoding outputEncoding,
             bool killProcessOnCancel,
             CancellationToken cancellationToken)
        {
            using (var processInvoker = HostContext.CreateService<IProcessInvoker>())
            {
                processInvoker.OutputDataReceived += OutputDataReceived;
                processInvoker.ErrorDataReceived += ErrorDataReceived;

                return await processInvoker.ExecuteAsync(workingDirectory: workingDirectory,
                                                 fileName: fileName,
                                                 arguments: arguments,
                                                 environment: environment,
                                                 requireExitCodeZero: requireExitCodeZero,
                                                 outputEncoding: outputEncoding,
                                                 killProcessOnCancel: killProcessOnCancel,
                                                 cancellationToken: cancellationToken);
            }
        }
    }

    public sealed class ContainerStepHost : AgentService, IContainerStepHost
    {
        public ContainerInfo Container { get; set; }
        public event EventHandler<ProcessDataReceivedEventArgs> OutputDataReceived;
        public event EventHandler<ProcessDataReceivedEventArgs> ErrorDataReceived;

        public async Task<int> ExecuteAsync(
             string workingDirectory,
             string fileName,
             string arguments,
             IDictionary<string, string> environment,
             bool requireExitCodeZero,
             Encoding outputEncoding,
             bool killProcessOnCancel,
             CancellationToken cancellationToken)
        {
            // make sure container exist.
            ArgUtil.NotNull(Container, nameof(Container));
            ArgUtil.NotNullOrEmpty(Container.ContainerId, nameof(Container.ContainerId));

            var dockerManger = HostContext.GetService<IDockerCommandManager>();
            string containerEnginePath = dockerManger.DockerPath;

            string envOptions = "";
            foreach (var env in environment)
            {
                envOptions += $" -e \"{env.Key}={env.Value.Replace("\"", "\\\"")}\"";
            }

            // we need cd to the workingDir then run the executable with args.
            // bash -c "cd \"workingDirectory\"; \"filePath\" \"arguments\""
            string workingDirectoryEscaped = StringUtil.Format(@"\""{0}\""", workingDirectory.Replace(@"""", @"\\\"""));
            string filePathEscaped = StringUtil.Format(@"\""{0}\""", fileName.Replace(@"""", @"\\\"""));
            string argumentsEscaped = arguments.Replace(@"\", @"\\").Replace(@"""", @"\""");
            string bashCommandLine = $"bash -c \"cd {workingDirectoryEscaped}&{filePathEscaped} {argumentsEscaped}\"";

            string containerExecutionArgs = $"exec -u {Container.CurrentUserId} {envOptions} {Container.ContainerId} {bashCommandLine}"; ;

            using (var processInvoker = HostContext.CreateService<IProcessInvoker>())
            {
                processInvoker.OutputDataReceived += OutputDataReceived;
                processInvoker.ErrorDataReceived += ErrorDataReceived;

                return await processInvoker.ExecuteAsync(workingDirectory: HostContext.GetDirectory(WellKnownDirectory.Work),
                                                 fileName: containerEnginePath,
                                                 arguments: containerExecutionArgs,
                                                 environment: null,
                                                 requireExitCodeZero: requireExitCodeZero,
                                                 outputEncoding: outputEncoding,
                                                 killProcessOnCancel: killProcessOnCancel,
                                                 cancellationToken: cancellationToken);
            }
        }
    }
}