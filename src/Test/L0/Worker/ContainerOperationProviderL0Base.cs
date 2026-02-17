// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.Container;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Moq;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Services.Agent;
using Microsoft.TeamFoundation.Framework.Common;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker
{
    public abstract class ContainerOperationProviderL0Base
    {
        protected const string NodePathFromLabel = "/usr/bin/node";
        protected const string NodePathFromLabelEmpty = "";
        protected const string DefaultNodeCommand = "node";
        protected const string NodeFromAgentExternal = "externals/node";

        protected Mock<IDockerCommandManager> CreateDockerManagerMock(string inspectResult)
        {
            var dockerManager = new Mock<IDockerCommandManager>();
            dockerManager.Setup(x => x.DockerVersion(It.IsAny<IExecutionContext>()))
                .ReturnsAsync(new DockerVersion(new Version("1.35"), new Version("1.35")));
            dockerManager.Setup(x => x.DockerPS(It.IsAny<IExecutionContext>(), It.IsAny<string>()))
                .ReturnsAsync(new List<string> { "container123 Up 5 seconds" });
            dockerManager.Setup(x => x.DockerNetworkCreate(It.IsAny<IExecutionContext>(), It.IsAny<string>()))
                .ReturnsAsync(0);
            dockerManager.Setup(x => x.DockerPull(It.IsAny<IExecutionContext>(), It.IsAny<string>()))
                .ReturnsAsync(0);
            dockerManager.Setup(x => x.DockerCreate(It.IsAny<IExecutionContext>(), It.IsAny<ContainerInfo>()))
                .ReturnsAsync("container123");
            dockerManager.Setup(x => x.DockerStart(It.IsAny<IExecutionContext>(), It.IsAny<string>()))
                .ReturnsAsync(0);
            dockerManager.Setup(x => x.DockerInspect(It.IsAny<IExecutionContext>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(inspectResult);
            dockerManager.Setup(x => x.DockerExec(It.IsAny<IExecutionContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<string>>()))
                .ReturnsAsync((IExecutionContext context, string containerId, string options, string command, List<string> output) =>
                {
                    if (command.Contains("node -v"))
                    {
                        output.Add("v16.20.2");
                    }
                    return 0;
                });
            return dockerManager;
        }

        protected Mock<IExecutionContext> CreateExecutionContextMock(TestHostContext hc)
        {
            var executionContext = new Mock<IExecutionContext>();
            var variables = new Variables(hc, new Dictionary<string, VariableValue>(), out var warnings);
            executionContext.Setup(x => x.Variables).Returns(variables);
            executionContext.Setup(x => x.CancellationToken).Returns(CancellationToken.None);
            executionContext.Setup(x => x.GetVariableValueOrDefault(It.IsAny<string>())).Returns(string.Empty);
            executionContext.Setup(x => x.Containers).Returns(new List<ContainerInfo>());
            executionContext.Setup(x => x.GetScopedEnvironment()).Returns(new SystemEnvironment());
            return executionContext;
        }

        protected sealed class FakeProcessInvoker : IProcessInvoker
        {
            public event EventHandler<ProcessDataReceivedEventArgs> OutputDataReceived;
#pragma warning disable CS0067
            public event EventHandler<ProcessDataReceivedEventArgs> ErrorDataReceived;
#pragma warning restore CS0067
            public TimeSpan SigintTimeout { get; set; }
            public TimeSpan SigtermTimeout { get; set; }
            public bool TryUseGracefulShutdown { get; set; }

            public void Initialize(IHostContext hostContext) { }

            public Task<int> ExecuteAsync(string workingDirectory, string fileName, string arguments, IDictionary<string, string> environment, CancellationToken cancellationToken)
                => ExecuteAsync(workingDirectory, fileName, arguments, environment, false, null, false, cancellationToken);

            public Task<int> ExecuteAsync(string workingDirectory, string fileName, string arguments, IDictionary<string, string> environment, bool requireExitCodeZero, CancellationToken cancellationToken)
                => ExecuteAsync(workingDirectory, fileName, arguments, environment, requireExitCodeZero, null, false, cancellationToken);

            public Task<int> ExecuteAsync(string workingDirectory, string fileName, string arguments, IDictionary<string, string> environment, bool requireExitCodeZero, Encoding outputEncoding, CancellationToken cancellationToken)
                => ExecuteAsync(workingDirectory, fileName, arguments, environment, requireExitCodeZero, outputEncoding, false, cancellationToken);

            public Task<int> ExecuteAsync(string workingDirectory, string fileName, string arguments, IDictionary<string, string> environment, bool requireExitCodeZero, Encoding outputEncoding, bool killProcessOnCancel, CancellationToken cancellationToken)
                => ExecuteAsync(workingDirectory, fileName, arguments, environment, requireExitCodeZero, outputEncoding, killProcessOnCancel, null, cancellationToken);

            public Task<int> ExecuteAsync(string workingDirectory, string fileName, string arguments, IDictionary<string, string> environment, bool requireExitCodeZero, Encoding outputEncoding, bool killProcessOnCancel, InputQueue<string> redirectStandardIn, CancellationToken cancellationToken)
                => ExecuteAsync(workingDirectory, fileName, arguments, environment, requireExitCodeZero, outputEncoding, killProcessOnCancel, redirectStandardIn, false, false, cancellationToken);

            public Task<int> ExecuteAsync(string workingDirectory, string fileName, string arguments, IDictionary<string, string> environment, bool requireExitCodeZero, Encoding outputEncoding, bool killProcessOnCancel, InputQueue<string> redirectStandardIn, bool inheritConsoleHandler, bool continueAfterCancelProcessTreeKillAttempt, CancellationToken cancellationToken)
                => ExecuteAsync(workingDirectory, fileName, arguments, environment, requireExitCodeZero, outputEncoding, killProcessOnCancel, redirectStandardIn, inheritConsoleHandler, false, continueAfterCancelProcessTreeKillAttempt, cancellationToken);

            public Task<int> ExecuteAsync(string workingDirectory, string fileName, string arguments, IDictionary<string, string> environment, bool requireExitCodeZero, Encoding outputEncoding, bool killProcessOnCancel, InputQueue<string> redirectStandardIn, bool inheritConsoleHandler, bool keepStandardInOpen, bool continueAfterCancelProcessTreeKillAttempt, CancellationToken cancellationToken)
                => ExecuteAsync(workingDirectory, fileName, arguments, environment, requireExitCodeZero, outputEncoding, killProcessOnCancel, redirectStandardIn, inheritConsoleHandler, keepStandardInOpen, false, continueAfterCancelProcessTreeKillAttempt, cancellationToken);

            public Task<int> ExecuteAsync(string workingDirectory, string fileName, string arguments, IDictionary<string, string> environment, bool requireExitCodeZero, Encoding outputEncoding, bool killProcessOnCancel, InputQueue<string> redirectStandardIn, bool inheritConsoleHandler, bool keepStandardInOpen, bool highPriorityProcess, bool continueAfterCancelProcessTreeKillAttempt, CancellationToken cancellationToken)
            {
                if (fileName == "whoami")
                    OutputDataReceived?.Invoke(this, new ProcessDataReceivedEventArgs("testuser"));
                else if (fileName == "id" && arguments.StartsWith("-u"))
                    OutputDataReceived?.Invoke(this, new ProcessDataReceivedEventArgs("1000"));
                else if (fileName == "id" && arguments.StartsWith("-gn"))
                    OutputDataReceived?.Invoke(this, new ProcessDataReceivedEventArgs("testgroup"));
                else if (fileName == "id" && arguments.StartsWith("-g"))
                    OutputDataReceived?.Invoke(this, new ProcessDataReceivedEventArgs("1000"));
                else if (fileName == "node" && arguments.Contains("-v"))
                    OutputDataReceived?.Invoke(this, new ProcessDataReceivedEventArgs("v16.20.2"));

                return Task.FromResult(0);
            }

            public void Dispose() { }
        }

        protected void SetupProcessInvokerMock(TestHostContext hc)
        {
#pragma warning disable CA2000
            var processInvoker = new FakeProcessInvoker();
#pragma warning restore CA2000
            // Enqueue enough instances for all ExecuteCommandAsync calls in container operations
            // Each test may call: whoami, id -u, id -g, id -gn, stat, and potentially other commands
            // Enqueue 10 instances to ensure we don't run out during test execution
            for (int i = 0; i < 10; i++)
            {
                hc.EnqueueInstance<IProcessInvoker>(processInvoker);
            }
        }
    }
}
