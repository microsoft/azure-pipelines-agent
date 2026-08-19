// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using Microsoft.TeamFoundation.Framework.Common;
using Microsoft.TeamFoundation.DistributedTask.Pipelines;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.Handlers;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker.Handlers
{
    public sealed class LegacyPowerShellHandlerL0
    {
        [Theory]
        [InlineData(false, "job", "explicit")]
        [InlineData(true, "", "runtime")]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker.Handlers")]
        [Trait("SkipOn", "linux")]
        [Trait("SkipOn", "darwin")]
        public async Task FeatureGateControlsPublicRuntimeVariableProjection(
            bool useJobScopedTaskEnvironment,
            string expectedProxy,
            string expectedShared)
        {
            using var hostContext = new TestHostContext(this);
            hostContext.SetSingleton<IWorkerCommandManager>(new Mock<IWorkerCommandManager>().Object);
            hostContext.SetSingleton<IVstsAgentWebProxy>(new Mock<IVstsAgentWebProxy>().Object);
            var processInvoker = new Mock<IProcessInvoker>();
            IDictionary<string, string> launchedEnvironment = null;
            processInvoker
                .Setup(x => x.ExecuteAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IDictionary<string, string>>(),
                    It.IsAny<bool>(),
                    It.IsAny<Encoding>(),
                    It.IsAny<bool>(),
                    It.IsAny<InputQueue<string>>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .Callback<string, string, string, IDictionary<string, string>, bool, Encoding, bool, InputQueue<string>, bool, bool, CancellationToken>(
                    (_, _, _, environment, _, _, _, _, _, _, _) => launchedEnvironment = environment)
                .ReturnsAsync(0);
            hostContext.EnqueueInstance<IProcessInvoker>(processInvoker.Object);

            string taskDirectory = hostContext.GetDirectory(WellKnownDirectory.Temp);
            Directory.CreateDirectory(taskDirectory);
            string scriptFile = Path.Combine(taskDirectory, "legacy-handler-test.ps1");
            File.WriteAllText(scriptFile, string.Empty);
            try
            {
                var environment = new TaskEnvironment(new Dictionary<string, string>(VarUtil.EnvironmentVariableKeyComparer)
                {
                    ["SHARED_VALUE"] = "explicit",
                });
                var state = new TaskEnvironmentState();
                state.Set("HTTP_PROXY", "job");
                state.Remove("RESTORED_VALUE");
                environment.Reset(state.GetSnapshot());
                var runtimeVariables = new Variables(
                    hostContext,
                    new Dictionary<string, VariableValue>
                    {
                        ["HTTP_PROXY"] = string.Empty,
                        ["shared.value"] = "runtime",
                        ["restored.value"] = "restored",
                        ["VSTSPSHOSTSCRIPTNAME"] = "spoofed",
                        [Constants.PathVariable] = "runtime-path",
                        ["runtime.secret"] = new VariableValue("secret", isSecret: true),
                    },
                    out _);
                var executionContext = new Mock<IExecutionContext>();
                executionContext.SetupGet(x => x.Variables).Returns(runtimeVariables);
                executionContext.SetupGet(x => x.Endpoints).Returns(new List<ServiceEndpoint>());
                executionContext.SetupGet(x => x.Repositories).Returns(new List<RepositoryResource>());
                executionContext.SetupGet(x => x.PrependPath).Returns(new List<string> { "prepend" });
                executionContext.Setup(x => x.GetScopedEnvironment()).Returns(new LocalEnvironment());
                executionContext
                    .Setup(x => x.GetVariableValueOrDefault("DistributedTask.Agent.UseJobScopedTaskEnvironment"))
                    .Returns(useJobScopedTaskEnvironment.ToString());
                executionContext
                    .Setup(x => x.GetVariableValueOrDefault("DistributedTask.Agent.InstallLegacyTfExe"))
                    .Returns("false");
                executionContext
                    .Setup(x => x.GetVariableValueOrDefault(Constants.Variables.Agent.HomeDirectory))
                    .Returns(hostContext.GetDirectory(WellKnownDirectory.Root));

                var handler = new PowerShellHandler
                {
                    Data = new PowerShellHandlerData
                    {
                        Target = Path.GetFileName(scriptFile),
                    },
                    Environment = environment,
                    ExecutionContext = executionContext.Object,
                    Inputs = new Dictionary<string, string>(),
                    RuntimeVariables = runtimeVariables,
                    TaskDirectory = taskDirectory,
                    Task = new TaskStepDefinitionReference
                    {
                        Name = "legacy PowerShell test",
                        Version = "1.0.0",
                    },
                };
                handler.Initialize(hostContext);

                await handler.RunAsync();

                Assert.Same(environment, launchedEnvironment);
                Assert.Equal(expectedProxy, launchedEnvironment["HTTP_PROXY"]);
                Assert.Equal(expectedShared, launchedEnvironment["SHARED_VALUE"]);
                Assert.Equal(scriptFile, launchedEnvironment["VSTSPSHOSTSCRIPTNAME"]);
                Assert.Equal(
                    PathUtil.PrependPath("prepend", "runtime-path"),
                    launchedEnvironment[Constants.PathVariable]);
                Assert.False(launchedEnvironment.ContainsKey("RUNTIME_SECRET"));
                Assert.False(launchedEnvironment.ContainsKey("SECRET_RUNTIME_SECRET"));
                Assert.False(launchedEnvironment.ContainsKey("VSTS_PUBLIC_VARIABLES"));
                Assert.False(launchedEnvironment.ContainsKey("VSTS_SECRET_VARIABLES"));
                if (useJobScopedTaskEnvironment)
                {
                    Assert.Equal("restored", launchedEnvironment["RESTORED_VALUE"]);
                    Assert.DoesNotContain("RESTORED_VALUE", environment.RemovedEnvironmentVariables);
                }
                else
                {
                    Assert.False(launchedEnvironment.ContainsKey("RESTORED_VALUE"));
                    Assert.Contains("RESTORED_VALUE", environment.RemovedEnvironmentVariables);
                }
            }
            finally
            {
                File.Delete(scriptFile);
            }
        }
    }
}
