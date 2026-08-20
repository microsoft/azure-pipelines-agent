// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using Microsoft.TeamFoundation.DistributedTask.Pipelines;
using Microsoft.TeamFoundation.Framework.Common;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent;
using Microsoft.VisualStudio.Services.Agent.Tests;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.Handlers;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Test.L0.Worker.Handlers;

[Collection("Worker proxy environment tests")]
public sealed class ProcessHandlerJobEnvironmentL0
{
    private const string LegacyStartMarker = "##ENV_DELIMITER_d8c0672b##";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Level", "L0")]
    [Trait("Category", "Worker.Handlers")]
    [Trait("SkipOn", "linux")]
    [Trait("SkipOn", "darwin")]
    public async Task FeatureEnabledCapturesRealCmdEnvironment(bool useV2)
    {
        const string captured = "AZP_PH_REAL_CMD_CAPTURE";
        string originalCaptured = Environment.GetEnvironmentVariable(captured);

        try
        {
            Environment.SetEnvironmentVariable(captured, "worker");
            using var hostContext = new TestHostContext(this);
            hostContext.SetSingleton<IWorkerCommandManager>(new WorkerCommandManager());
            hostContext.SetSingleton<IExtensionManager>(new ExtensionManager());
            using var processInvoker = new ProcessInvokerWrapper();
            hostContext.EnqueueInstance<IProcessInvoker>(processInvoker);
            string target = Path.Combine(
                hostContext.GetDirectory(WellKnownDirectory.Temp),
                "process-handler-real-capture.cmd");
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.WriteAllText(target, $"@echo off{Environment.NewLine}set {captured}=captured");

            try
            {
                var state = new TaskEnvironmentState();
                var environment = new TaskEnvironment();
                environment.Reset(state.GetSnapshot());
                var runtimeVariables = new Variables(
                    hostContext,
                    new Dictionary<string, VariableValue>(),
                    out _);
                var executionContext = new Mock<IExecutionContext>();
                executionContext.SetupGet(context => context.PrependPath).Returns(new List<string>());
                executionContext.SetupGet(context => context.Variables).Returns(runtimeVariables);
                executionContext.SetupGet(context => context.TaskEnvironmentState).Returns(state);
                executionContext.SetupGet(context => context.CancellationToken).Returns(CancellationToken.None);
                executionContext.Setup(context => context.GetScopedEnvironment()).Returns(new LocalEnvironment());
                executionContext
                    .Setup(context => context.GetVariableValueOrDefault("DistributedTask.Agent.UseJobScopedTaskEnvironment"))
                    .Returns("true");

                IProcessHandler handler = useV2 ? new ProcessHandlerV2() : new ProcessHandler();
                handler.Initialize(hostContext);
                handler.Data = new ProcessHandlerData
                {
                    Target = target,
                    ArgumentFormat = string.Empty,
                    DisableInlineExecution = false.ToString(),
                    ModifyEnvironment = true.ToString(),
                };
                handler.Inputs = new Dictionary<string, string>();
                handler.TaskDirectory = hostContext.GetDirectory(WellKnownDirectory.Temp);
                handler.Environment = environment;
                handler.RuntimeVariables = runtimeVariables;
                handler.ExecutionContext = executionContext.Object;

                await handler.RunAsync();

                Assert.Equal("captured", state.GetSnapshot().Values[captured]);
                Assert.Equal("worker", Environment.GetEnvironmentVariable(captured));
            }
            finally
            {
                File.Delete(target);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(captured, originalCaptured);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Level", "L0")]
    [Trait("Category", "Worker.Handlers")]
    [Trait("SkipOn", "linux")]
    [Trait("SkipOn", "darwin")]
    public async Task ExactDeltaFlowsFromProcessHandlerToPluginHostWithoutChangingWorker(bool useV2)
    {
        const string proxy = "HTTP_PROXY";
        const string unrelated = "AZP_PH_END_TO_END_UNRELATED";
        const string secret = "runtime.secret";
        const string proxyValue = "http://public-runtime";
        string originalProxy = Environment.GetEnvironmentVariable(proxy);

        try
        {
            Environment.SetEnvironmentVariable(proxy, proxyValue);
            using var test = new ProcessHandlerTest(
                useV2,
                useJobScopedTaskEnvironment: true,
                modifyEnvironment: true,
                runtimeValues: new Dictionary<string, VariableValue>
                {
                    [proxy] = proxyValue,
                    [secret] = new VariableValue("secret", isSecret: true),
                });
            var filesBeforeCapture = new HashSet<string>(
                Directory.GetFiles(test.TempDirectory),
                StringComparer.OrdinalIgnoreCase);
            test.EnqueueAttempt((invoker, environment, arguments) =>
            {
                Assert.Equal(proxyValue, environment[proxy]);
                Assert.False(environment.ContainsKey("RUNTIME_SECRET"));
                var finalEnvironment =
                    ProcessHandlerEnvironmentCapture.CreateInitialEnvironment(environment);
                finalEnvironment[unrelated] = "changed";
                RaiseCapturedEnvironment(
                    invoker,
                    arguments,
                    finalEnvironment.Select(pair => $"{pair.Key}={pair.Value}").ToArray());
            });

            await test.Handler.RunAsync();

            TaskEnvironmentSnapshot snapshot = test.State.GetSnapshot();
            KeyValuePair<string, string> delta = Assert.Single(snapshot.Values);
            Assert.Equal(unrelated, delta.Key, ignoreCase: true);
            Assert.Equal("changed", delta.Value);
            Assert.Empty(snapshot.Removed);
            Assert.Equal(proxyValue, Environment.GetEnvironmentVariable(proxy));
            Assert.True(filesBeforeCapture.SetEquals(Directory.GetFiles(test.TempDirectory)));
            test.ExecutionContext.Verify(
                context => context.Write(
                    It.IsAny<string>(),
                    It.Is<string>(line => line != null && line.Contains($"{unrelated}=changed")),
                    It.IsAny<bool>()),
                Times.Never);

            test.RuntimeVariables.Set(proxy, string.Empty);
            Dictionary<string, string> launchedEnvironment = null;
            var pluginManager = new Mock<IAgentPluginManager>();
            pluginManager
                .Setup(manager => manager.GetTaskPlugins(It.IsAny<Guid>()))
                .Returns(new List<string> { "Test.Plugin, Test" });
            pluginManager
                .Setup(manager => manager.RunPluginTaskAsync(
                    It.IsAny<IExecutionContext>(),
                    "Test.Plugin, Test",
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<Variables>(),
                    It.IsAny<EventHandler<ProcessDataReceivedEventArgs>>()))
                .Callback<IExecutionContext, string, Dictionary<string, string>, Dictionary<string, string>, Variables, EventHandler<ProcessDataReceivedEventArgs>>(
                    (_, _, _, environment, _, _) => launchedEnvironment = environment)
                .Returns(Task.CompletedTask);
            test.HostContext.SetSingleton<IAgentPluginManager>(pluginManager.Object);
            var pluginEnvironment = new TaskEnvironment();
            pluginEnvironment.Reset(snapshot);
            var pluginHandler = new AgentPluginHandler
            {
                Data = new AgentPluginHandlerData { Target = "Test.Plugin, Test" },
                Environment = pluginEnvironment,
                ExecutionContext = test.ExecutionContext.Object,
                Inputs = new Dictionary<string, string>(),
                RuntimeVariables = test.RuntimeVariables,
                StepHost = new Mock<IDefaultStepHost>().Object,
                Task = new TaskStepDefinitionReference
                {
                    Id = Guid.NewGuid(),
                    Name = "plugin test",
                    Version = "1.0.0",
                },
            };
            pluginHandler.Initialize(test.HostContext);

            await pluginHandler.RunAsync();

            Assert.Equal("changed", launchedEnvironment[unrelated]);
            Assert.Equal(string.Empty, launchedEnvironment[proxy]);
            Assert.False(launchedEnvironment.ContainsKey("RUNTIME_SECRET"));
            Assert.False(launchedEnvironment.ContainsKey("SECRET_RUNTIME_SECRET"));
            Assert.False(launchedEnvironment.ContainsKey("VSTS_PUBLIC_VARIABLES"));
            Assert.False(launchedEnvironment.ContainsKey("VSTS_SECRET_VARIABLES"));
            Assert.False(test.State.GetSnapshot().Values.ContainsKey(proxy));
            Assert.Equal(proxyValue, Environment.GetEnvironmentVariable(proxy));
        }
        finally
        {
            Environment.SetEnvironmentVariable(proxy, originalProxy);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Level", "L0")]
    [Trait("Category", "Worker.Handlers")]
    [Trait("SkipOn", "linux")]
    [Trait("SkipOn", "darwin")]
    public async Task FeatureEnabledCommitsOnlyDeltaWithoutChangingWorker(bool useV2)
    {
        const string captured = "AZP_PH_CAPTURED";
        const string workerUnchanged = "AZP_PH_WORKER_UNCHANGED";
        const string explicitUnchanged = "AZP_PH_EXPLICIT_UNCHANGED";
        const string unrelated = "AZP_PH_UNRELATED";
        const string proxy = "HTTP_PROXY";
        string originalCaptured = Environment.GetEnvironmentVariable(captured);
        string originalWorkerUnchanged = Environment.GetEnvironmentVariable(workerUnchanged);
        string originalProxy = Environment.GetEnvironmentVariable(proxy);

        try
        {
            Environment.SetEnvironmentVariable(captured, "worker");
            Environment.SetEnvironmentVariable(workerUnchanged, "worker");
            Environment.SetEnvironmentVariable(proxy, "worker-proxy");
            using var test = new ProcessHandlerTest(
                useV2,
                useJobScopedTaskEnvironment: true,
                modifyEnvironment: true,
                explicitEnvironment: new Dictionary<string, string>
                {
                    [explicitUnchanged] = "explicit",
                },
                runtimeValues: new Dictionary<string, VariableValue>
                {
                    [proxy] = string.Empty,
                });
            test.State.Set(unrelated, "preserved");
            test.EnqueueAttempt((invoker, environment, arguments) =>
            {
                Assert.Equal(string.Empty, environment[proxy]);
                RaiseCapturedEnvironment(
                    invoker,
                    arguments,
                    $"{captured}=job=value",
                    $"{workerUnchanged}=worker",
                    $"{explicitUnchanged}=explicit",
                    $"{unrelated}=preserved",
                    $"{proxy}=");
            });

            await test.Handler.RunAsync();

            TaskEnvironmentSnapshot snapshot = test.State.GetSnapshot();
            Assert.Equal("job=value", snapshot.Values[captured]);
            Assert.Equal("preserved", snapshot.Values[unrelated]);
            Assert.False(snapshot.Values.ContainsKey(workerUnchanged));
            Assert.False(snapshot.Values.ContainsKey(explicitUnchanged));
            Assert.False(snapshot.Values.ContainsKey(proxy));
            Assert.DoesNotContain(workerUnchanged, snapshot.Removed);
            Assert.DoesNotContain(explicitUnchanged, snapshot.Removed);
            Assert.DoesNotContain(proxy, snapshot.Removed);
            Assert.Equal("worker", Environment.GetEnvironmentVariable(captured));
            Assert.Equal("worker", Environment.GetEnvironmentVariable(workerUnchanged));
            Assert.Equal("worker-proxy", Environment.GetEnvironmentVariable(proxy));
        }
        finally
        {
            Environment.SetEnvironmentVariable(captured, originalCaptured);
            Environment.SetEnvironmentVariable(workerUnchanged, originalWorkerUnchanged);
            Environment.SetEnvironmentVariable(proxy, originalProxy);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Level", "L0")]
    [Trait("Category", "Worker.Handlers")]
    [Trait("SkipOn", "linux")]
    [Trait("SkipOn", "darwin")]
    public async Task FeatureEnabledStoresExplicitEmptyInsteadOfTombstone(bool useV2)
    {
        const string changed = "AZP_PH_CHANGED_TO_EMPTY";
        string original = Environment.GetEnvironmentVariable(changed);

        try
        {
            Environment.SetEnvironmentVariable(changed, "worker");
            using var test = new ProcessHandlerTest(
                useV2,
                useJobScopedTaskEnvironment: true,
                modifyEnvironment: true);
            test.EnqueueAttempt((invoker, _, arguments) =>
            {
                RaiseCapturedEnvironment(invoker, arguments, $"{changed}=");
            });

            await test.Handler.RunAsync();

            TaskEnvironmentSnapshot snapshot = test.State.GetSnapshot();
            Assert.Equal(string.Empty, snapshot.Values[changed]);
            Assert.DoesNotContain(changed, snapshot.Removed);
            Assert.Equal("worker", Environment.GetEnvironmentVariable(changed));
        }
        finally
        {
            Environment.SetEnvironmentVariable(changed, original);
        }
    }

    [Fact]
    [Trait("Level", "L0")]
    [Trait("Category", "Worker.Handlers")]
    [Trait("SkipOn", "linux")]
    [Trait("SkipOn", "darwin")]
    public void InitialEnvironmentMatchesProcessInvokerMerge()
    {
        const string removed = "AZP_PH_INITIAL_REMOVED";
        string original = Environment.GetEnvironmentVariable(removed);

        try
        {
            Environment.SetEnvironmentVariable(removed, "worker");
            var environment = new TaskEnvironment
            {
                ["AZP_PH_INITIAL_OVERLAY"] = "overlay",
                ["=AZP_PH_PSEUDO"] = "ignored",
            };
            environment.Remove(removed);

            Dictionary<string, string> initial =
                ProcessHandlerEnvironmentCapture.CreateInitialEnvironment(environment);

            Assert.False(initial.ContainsKey(removed));
            Assert.Equal("overlay", initial["AZP_PH_INITIAL_OVERLAY"]);
            Assert.Equal("True", initial[Constants.TFBuild]);
            Assert.False(initial.ContainsKey("=AZP_PH_PSEUDO"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(removed, original);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Level", "L0")]
    [Trait("Category", "Worker.Handlers")]
    [Trait("SkipOn", "linux")]
    [Trait("SkipOn", "darwin")]
    public async Task FeatureEnabledExcludesHandlerOwnedVariables(bool useV2)
    {
        const string secureArguments = "secure argument value";
        using var test = new ProcessHandlerTest(
            useV2,
            useJobScopedTaskEnvironment: true,
            modifyEnvironment: true,
            runtimeValues: new Dictionary<string, VariableValue>
            {
                [Constants.Variables.Agent.JobStatus] = "Failed",
            },
            disableInlineExecution: true,
            arguments: secureArguments,
            secureArguments: true);
        test.Handler.Environment[Constants.CommandCorrelationIdEnvVar] = "internal";
        string secureName = null;
        test.EnqueueAttempt((invoker, environment, arguments) =>
        {
            secureName = environment.Keys.Single(
                name => name.StartsWith("AGENT_PH_ARGS_", StringComparison.OrdinalIgnoreCase));
            RaiseCapturedEnvironment(
                invoker,
                arguments,
                "AGENT_PH_ARGS_TEST=secret");
        });

        await test.Handler.RunAsync();

        TaskEnvironmentSnapshot snapshot = test.State.GetSnapshot();
        Assert.NotNull(secureName);
        Assert.False(snapshot.Values.ContainsKey(Constants.TFBuild));
        Assert.False(snapshot.Values.ContainsKey(Constants.Variables.Agent.JobStatus));
        Assert.False(snapshot.Values.ContainsKey("AGENT_JOBSTATUS"));
        Assert.False(snapshot.Values.ContainsKey(Constants.CommandCorrelationIdEnvVar));
        Assert.False(snapshot.Values.ContainsKey("AGENT_PH_ARGS_TEST"));
        Assert.False(snapshot.Values.ContainsKey(secureName));
        Assert.DoesNotContain(Constants.TFBuild, snapshot.Removed);
        Assert.DoesNotContain(Constants.Variables.Agent.JobStatus, snapshot.Removed);
        Assert.DoesNotContain("AGENT_JOBSTATUS", snapshot.Removed);
        Assert.DoesNotContain(Constants.CommandCorrelationIdEnvVar, snapshot.Removed);
        Assert.DoesNotContain("AGENT_PH_ARGS_TEST", snapshot.Removed);
        Assert.DoesNotContain(secureName, snapshot.Removed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Level", "L0")]
    [Trait("Category", "Worker.Handlers")]
    [Trait("SkipOn", "linux")]
    [Trait("SkipOn", "darwin")]
    public async Task FeatureEnabledRecordsRemovalThenGenuineReAdd(bool useV2)
    {
        const string workerName = "AZP_PH_WORKER_REMOVED";
        const string stateName = "AZP_PH_STATE_REMOVED";
        string originalWorker = Environment.GetEnvironmentVariable(workerName);

        try
        {
            Environment.SetEnvironmentVariable(workerName, "worker");
            using var test = new ProcessHandlerTest(
                useV2,
                useJobScopedTaskEnvironment: true,
                modifyEnvironment: true);
            test.State.Set(stateName, "state");
            test.EnqueueAttempt((invoker, environment, arguments) =>
            {
                Assert.Equal("state", environment[stateName]);
                RaiseCapturedEnvironment(invoker, arguments);
            });

            await test.Handler.RunAsync();

            TaskEnvironmentSnapshot removed = test.State.GetSnapshot();
            Assert.Contains(workerName, removed.Removed);
            Assert.Contains(stateName, removed.Removed);
            Assert.False(removed.Values.ContainsKey(stateName));

            test.EnqueueAttempt((invoker, environment, arguments) =>
            {
                Assert.False(environment.ContainsKey(workerName));
                Assert.False(environment.ContainsKey(stateName));
                RaiseCapturedEnvironment(invoker, arguments, $"{stateName}=restored");
            });

            await test.Handler.RunAsync();

            TaskEnvironmentSnapshot restored = test.State.GetSnapshot();
            Assert.Equal("restored", restored.Values[stateName]);
            Assert.DoesNotContain(stateName, restored.Removed);
            Assert.Equal("worker", Environment.GetEnvironmentVariable(workerName));
        }
        finally
        {
            Environment.SetEnvironmentVariable(workerName, originalWorker);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Level", "L0")]
    [Trait("Category", "Worker.Handlers")]
    [Trait("SkipOn", "linux")]
    [Trait("SkipOn", "darwin")]
    public async Task UnchangedExplicitAndRuntimeOverlaysPreserveUnderlyingTombstones(bool useV2)
    {
        const string explicitName = "AZP_PH_EXPLICIT_RESTORE";
        const string runtimeName = "AZP_PH_RUNTIME_RESTORE";
        using var test = new ProcessHandlerTest(
            useV2,
            useJobScopedTaskEnvironment: true,
            modifyEnvironment: true,
            explicitEnvironment: new Dictionary<string, string>
            {
                [explicitName] = "explicit",
            },
            runtimeValues: new Dictionary<string, VariableValue>
            {
                [runtimeName] = "runtime",
            });
        test.State.Remove(explicitName);
        test.State.Remove(runtimeName);
        test.EnqueueAttempt((invoker, environment, arguments) =>
        {
            Assert.Equal("explicit", environment[explicitName]);
            Assert.Equal("runtime", environment[runtimeName]);
            RaiseCapturedEnvironment(
                invoker,
                arguments,
                $"{explicitName}=explicit",
                $"{runtimeName}=runtime");
        });

        await test.Handler.RunAsync();

        TaskEnvironmentSnapshot snapshot = test.State.GetSnapshot();
        Assert.Contains(explicitName, snapshot.Removed);
        Assert.Contains(runtimeName, snapshot.Removed);
        Assert.False(snapshot.Values.ContainsKey(explicitName));
        Assert.False(snapshot.Values.ContainsKey(runtimeName));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Level", "L0")]
    [Trait("Category", "Worker.Handlers")]
    [Trait("SkipOn", "linux")]
    [Trait("SkipOn", "darwin")]
    public async Task FeatureDisabledRetainsWorkerSecureArgumentTransport(bool useV2)
    {
        string arguments = $"legacy-secure-{Guid.NewGuid():N}";
        string secureName = null;

        try
        {
            using var test = new ProcessHandlerTest(
                useV2,
                useJobScopedTaskEnvironment: false,
                modifyEnvironment: false,
                disableInlineExecution: true,
                arguments: arguments,
                secureArguments: true);
            test.EnqueueAttempt((_, environment, _) =>
            {
                secureName = Environment.GetEnvironmentVariables()
                    .Keys
                    .Cast<string>()
                    .Single(name =>
                        name.StartsWith("AGENT_PH_ARGS_", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(Environment.GetEnvironmentVariable(name), arguments, StringComparison.Ordinal));
                Assert.False(environment.ContainsKey(secureName));
            });

            await test.Handler.RunAsync();

            Assert.NotNull(secureName);
            Assert.Equal(arguments, Environment.GetEnvironmentVariable(secureName));
            Assert.False(test.State.GetSnapshot().Values.ContainsKey(secureName));
        }
        finally
        {
            if (secureName != null)
            {
                Environment.SetEnvironmentVariable(secureName, null);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Level", "L0")]
    [Trait("Category", "Worker.Handlers")]
    [Trait("SkipOn", "linux")]
    [Trait("SkipOn", "darwin")]
    public async Task FeatureEnabledKeepsSecureArgumentsOutOfWorkerAndJobState(bool useV2)
    {
        const string arguments = "secure argument value";
        using var test = new ProcessHandlerTest(
            useV2,
            useJobScopedTaskEnvironment: true,
            modifyEnvironment: true,
            disableInlineExecution: true,
            arguments: arguments,
            secureArguments: true);
        string secureName = null;
        test.EnqueueAttempt((invoker, environment, commandLine) =>
        {
            KeyValuePair<string, string> secureArgument = environment.Single(
                pair => pair.Key.StartsWith("AGENT_PH_ARGS_", StringComparison.OrdinalIgnoreCase));
            secureName = secureArgument.Key;
            Assert.Equal(arguments, secureArgument.Value);
            Assert.Null(Environment.GetEnvironmentVariable(secureName));
            RaiseCapturedEnvironment(invoker, commandLine, $"{secureName}={arguments}");
        });

        await test.Handler.RunAsync();

        Assert.NotNull(secureName);
        Assert.Null(Environment.GetEnvironmentVariable(secureName));
        Assert.False(test.State.GetSnapshot().Values.ContainsKey(secureName));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Level", "L0")]
    [Trait("Category", "Worker.Handlers")]
    [Trait("SkipOn", "linux")]
    [Trait("SkipOn", "darwin")]
    public async Task FeatureEnabledDoesNotCommitNonzeroOrIncompleteAttempts(bool useV2)
    {
        using var test = new ProcessHandlerTest(
            useV2,
            useJobScopedTaskEnvironment: true,
            modifyEnvironment: true);
        test.State.Set("PRIOR", "unchanged");
        test.EnqueueAttempt((invoker, _, arguments) =>
        {
            RaiseCapturedEnvironment(invoker, arguments, "NONZERO=not-committed");
        }, exitCode: 1);

        await Assert.ThrowsAsync<Exception>(() => test.Handler.RunAsync());
        AssertStateUnchanged(test.State, "NONZERO");

        test.EnqueueAttempt((invoker, _, arguments) =>
        {
            RaiseOutput(invoker, GetCaptureMarkers(arguments).Start);
            RaiseOutput(invoker, "INCOMPLETE=not-committed");
        });

        await test.Handler.RunAsync();
        AssertStateUnchanged(test.State, "INCOMPLETE");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Level", "L0")]
    [Trait("Category", "Worker.Handlers")]
    [Trait("SkipOn", "linux")]
    [Trait("SkipOn", "darwin")]
    public async Task FeatureEnabledDoesNotCommitFailingStandardErrorOrCancellation(bool useV2)
    {
        using var test = new ProcessHandlerTest(
            useV2,
            useJobScopedTaskEnvironment: true,
            modifyEnvironment: true);
        test.State.Set("PRIOR", "unchanged");
        test.EnqueueAttempt((invoker, _, arguments) =>
        {
            RaiseCapturedEnvironment(invoker, arguments, "STDERR_FAILURE=not-committed");
            RaiseError(invoker, "failure");
        });

        await Assert.ThrowsAsync<Exception>(() => test.Handler.RunAsync());
        AssertStateUnchanged(test.State, "STDERR_FAILURE");

        test.EnqueueAttempt((invoker, _, arguments) =>
        {
            RaiseOutput(invoker, GetCaptureMarkers(arguments).Start);
            RaiseOutput(invoker, "CANCELED=not-committed");
        }, cancel: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => test.Handler.RunAsync());
        AssertStateUnchanged(test.State, "CANCELED");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Level", "L0")]
    [Trait("Category", "Worker.Handlers")]
    [Trait("SkipOn", "linux")]
    [Trait("SkipOn", "darwin")]
    public async Task FeatureEnabledDoesNotCommitFailedTaskResult(bool useV2)
    {
        using var test = new ProcessHandlerTest(
            useV2,
            useJobScopedTaskEnvironment: true,
            modifyEnvironment: true);
        test.State.Set("PRIOR", "unchanged");
        test.ExecutionContext.SetupGet(context => context.Result).Returns(TaskResult.Failed);
        test.EnqueueAttempt((invoker, _, arguments) =>
        {
            RaiseCapturedEnvironment(invoker, arguments, "FAILED_RESULT=not-committed");
        });

        await test.Handler.RunAsync();

        AssertStateUnchanged(test.State, "FAILED_RESULT");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Level", "L0")]
    [Trait("Category", "Worker.Handlers")]
    [Trait("SkipOn", "linux")]
    [Trait("SkipOn", "darwin")]
    public async Task FeatureEnabledTreatsStandardErrorAndPostCompletionAsOrdinaryOutput(bool useV2)
    {
        using var test = new ProcessHandlerTest(
            useV2,
            useJobScopedTaskEnvironment: true,
            modifyEnvironment: true,
            failOnStandardError: false);
        test.EnqueueAttempt((invoker, _, arguments) =>
        {
            (string start, string completion) = GetCaptureMarkers(arguments);
            RaiseOutput(invoker, start);
            RaiseOutput(invoker, "CAPTURED=good");
            RaiseError(invoker, "STDERR_SPOOF=bad");
            RaiseOutput(invoker, completion);
            RaiseOutput(invoker, "AFTER_COMPLETION=ordinary");
        });

        await test.Handler.RunAsync();

        TaskEnvironmentSnapshot snapshot = test.State.GetSnapshot();
        Assert.Equal("good", snapshot.Values["CAPTURED"]);
        Assert.False(snapshot.Values.ContainsKey("STDERR_SPOOF"));
        Assert.False(snapshot.Values.ContainsKey("AFTER_COMPLETION"));
        test.ExecutionContext.Verify(
            context => context.Write(It.IsAny<string>(), "STDERR_SPOOF=bad", It.IsAny<bool>()),
            Times.Once);
        test.ExecutionContext.Verify(
            context => context.Write(It.IsAny<string>(), "AFTER_COMPLETION=ordinary", It.IsAny<bool>()),
            Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Level", "L0")]
    [Trait("Category", "Worker.Handlers")]
    [Trait("SkipOn", "linux")]
    [Trait("SkipOn", "darwin")]
    public async Task FeatureEnabledRequiresExactPerAttemptBoundaries(bool useV2)
    {
        using var test = new ProcessHandlerTest(
            useV2,
            useJobScopedTaskEnvironment: true,
            modifyEnvironment: true);
        test.EnqueueAttempt((invoker, _, arguments) =>
        {
            (string start, string completion) = GetCaptureMarkers(arguments);
            RaiseOutput(invoker, $"{start}_SPOOF");
            RaiseOutput(invoker, start);
            RaiseOutput(invoker, $"{ProcessHandlerEnvironmentCapture.CompletionMarkerPrefix}SPOOF=value");
            RaiseOutput(invoker, "CAPTURED=good");
            RaiseOutput(invoker, completion);
            RaiseOutput(invoker, $"{completion}_SPOOF");
        });

        await test.Handler.RunAsync();

        Assert.Equal("good", test.State.GetSnapshot().Values["CAPTURED"]);
        test.ExecutionContext.Verify(
            context => context.Write(It.IsAny<string>(), It.Is<string>(line => line.EndsWith("_SPOOF")), It.IsAny<bool>()),
            Times.Exactly(2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Level", "L0")]
    [Trait("Category", "Worker.Handlers")]
    [Trait("SkipOn", "linux")]
    [Trait("SkipOn", "darwin")]
    public async Task FeatureEnabledDoesNotCommitWhenCancellationWasRequested(bool useV2)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var test = new ProcessHandlerTest(
            useV2,
            useJobScopedTaskEnvironment: true,
            modifyEnvironment: true,
            cancellationToken: cancellation.Token);
        test.State.Set("PRIOR", "unchanged");
        test.EnqueueAttempt((invoker, _, arguments) =>
        {
            RaiseCapturedEnvironment(invoker, arguments, "CANCELED=not-committed");
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => test.Handler.RunAsync());

        AssertStateUnchanged(test.State, "CANCELED");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Level", "L0")]
    [Trait("Category", "Worker.Handlers")]
    [Trait("SkipOn", "linux")]
    [Trait("SkipOn", "darwin")]
    public async Task RetryResetsEnvironmentCaptureAndErrorState(bool useV2)
    {
        using var test = new ProcessHandlerTest(
            useV2,
            useJobScopedTaskEnvironment: true,
            modifyEnvironment: true,
            explicitEnvironment: new Dictionary<string, string>
            {
                ["EXPLICIT"] = "task",
            });
        test.State.Set("JOB", "first");
        test.EnqueueAttempt((invoker, _, arguments) =>
        {
            RaiseOutput(invoker, GetCaptureMarkers(arguments).Start);
            RaiseOutput(invoker, "STALE_CAPTURE=first");
            RaiseError(invoker, "first attempt error");
        });

        await Assert.ThrowsAsync<Exception>(() => test.Handler.RunAsync());
        test.State.Set("JOB", "second");

        test.EnqueueAttempt((invoker, environment, arguments) =>
        {
            Assert.Equal("second", environment["JOB"]);
            Assert.Equal("task", environment["EXPLICIT"]);
            Assert.False(environment.ContainsKey("STALE_CAPTURE"));
            RaiseCapturedEnvironment(invoker, arguments, "FRESH_CAPTURE=second");
        });

        await test.Handler.RunAsync();

        TaskEnvironmentSnapshot snapshot = test.State.GetSnapshot();
        Assert.Equal("second", snapshot.Values["FRESH_CAPTURE"]);
        Assert.False(snapshot.Values.ContainsKey("STALE_CAPTURE"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Level", "L0")]
    [Trait("Category", "Worker.Handlers")]
    [Trait("SkipOn", "linux")]
    [Trait("SkipOn", "darwin")]
    public async Task AttemptResetPreservesCommandCorrelationIdWithoutPersistingIt(bool useV2)
    {
        using var test = new ProcessHandlerTest(
            useV2,
            useJobScopedTaskEnvironment: true,
            modifyEnvironment: true);
        test.Handler.Environment[Constants.CommandCorrelationIdEnvVar] = "correlation";
        test.EnqueueAttempt((invoker, environment, arguments) =>
        {
            Assert.Equal("correlation", environment[Constants.CommandCorrelationIdEnvVar]);
            RaiseCapturedEnvironment(
                invoker,
                arguments,
                $"{Constants.CommandCorrelationIdEnvVar}=correlation");
        });

        await test.Handler.RunAsync();

        Assert.False(test.State.GetSnapshot().Values.ContainsKey(Constants.CommandCorrelationIdEnvVar));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Level", "L0")]
    [Trait("Category", "Worker.Handlers")]
    [Trait("SkipOn", "linux")]
    [Trait("SkipOn", "darwin")]
    public async Task FeatureDisabledRetainsLegacyCommandAndWorkerMutation(bool useV2)
    {
        const string changed = "AZP_PH_LEGACY_CHANGED";
        const string proxy = "HTTP_PROXY";
        string originalChanged = Environment.GetEnvironmentVariable(changed);
        string originalProxy = Environment.GetEnvironmentVariable(proxy);

        try
        {
            Environment.SetEnvironmentVariable(changed, null);
            Environment.SetEnvironmentVariable(proxy, "worker-proxy");
            using var test = new ProcessHandlerTest(
                useV2,
                useJobScopedTaskEnvironment: false,
                modifyEnvironment: true);
            test.EnqueueAttempt((invoker, _, arguments) =>
            {
                Assert.Contains($"&& echo {LegacyStartMarker} && set \"", arguments, StringComparison.Ordinal);
                Assert.DoesNotContain(ProcessHandlerEnvironmentCapture.CompletionMarkerPrefix, arguments, StringComparison.Ordinal);
                RaiseOutput(invoker, LegacyStartMarker);
                RaiseOutput(invoker, $"{changed}=legacy");
                RaiseOutput(invoker, $"{proxy}=task-proxy");
            });

            await test.Handler.RunAsync();

            Assert.Equal("legacy", Environment.GetEnvironmentVariable(changed));
            Assert.Equal("worker-proxy", Environment.GetEnvironmentVariable(proxy));
            Assert.Empty(test.State.GetSnapshot().Values);
        }
        finally
        {
            Environment.SetEnvironmentVariable(changed, originalChanged);
            Environment.SetEnvironmentVariable(proxy, originalProxy);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Level", "L0")]
    [Trait("Category", "Worker.Handlers")]
    [Trait("SkipOn", "linux")]
    [Trait("SkipOn", "darwin")]
    public async Task ModifyEnvironmentFalseDoesNotCapture(bool useV2)
    {
        using var test = new ProcessHandlerTest(
            useV2,
            useJobScopedTaskEnvironment: true,
            modifyEnvironment: false);
        test.EnqueueAttempt((invoker, _, arguments) =>
        {
            Assert.DoesNotContain(ProcessHandlerEnvironmentCapture.StartMarkerPrefix, arguments, StringComparison.Ordinal);
            Assert.DoesNotContain(ProcessHandlerEnvironmentCapture.CompletionMarkerPrefix, arguments, StringComparison.Ordinal);
            RaiseOutput(invoker, "NOT_CAPTURED=value");
        });

        await test.Handler.RunAsync();

        Assert.False(test.State.GetSnapshot().Values.ContainsKey("NOT_CAPTURED"));
    }

    private static void AssertStateUnchanged(TaskEnvironmentState state, string absentName)
    {
        TaskEnvironmentSnapshot snapshot = state.GetSnapshot();
        Assert.Equal("unchanged", snapshot.Values["PRIOR"]);
        Assert.False(snapshot.Values.ContainsKey(absentName));
    }

    private static void RaiseOutput(Mock<IProcessInvoker> invoker, string line)
    {
        invoker.Raise(
            processInvoker => processInvoker.OutputDataReceived += null,
            new ProcessDataReceivedEventArgs(line));
    }

    private static void RaiseError(Mock<IProcessInvoker> invoker, string line)
    {
        invoker.Raise(
            processInvoker => processInvoker.ErrorDataReceived += null,
            new ProcessDataReceivedEventArgs(line));
    }

    private static void RaiseCapturedEnvironment(
        Mock<IProcessInvoker> invoker,
        string arguments,
        params string[] environmentLines)
    {
        (string start, string completion) = GetCaptureMarkers(arguments);
        RaiseOutput(invoker, start);
        foreach (string line in environmentLines)
        {
            RaiseOutput(invoker, line);
        }

        RaiseOutput(invoker, completion);
    }

    private static (string Start, string Completion) GetCaptureMarkers(string arguments)
    {
        string commandText = arguments;
        if (!commandText.Contains(ProcessHandlerEnvironmentCapture.StartMarkerPrefix, StringComparison.Ordinal))
        {
            int firstQuote = commandText.IndexOf('"');
            int lastQuote = commandText.LastIndexOf('"');
            Assert.True(firstQuote >= 0 && lastQuote > firstQuote, "Generated script path was not found.");
            string scriptPath = commandText.Substring(firstQuote + 1, lastQuote - firstQuote - 1);
            commandText = File.ReadAllText(scriptPath);
        }

        string start = ExtractMarker(commandText, ProcessHandlerEnvironmentCapture.StartMarkerPrefix);
        string completion = ExtractMarker(commandText, ProcessHandlerEnvironmentCapture.CompletionMarkerPrefix);
        return (start, completion);
    }

    private static string ExtractMarker(string arguments, string prefix)
    {
        int start = arguments.IndexOf(prefix, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Marker prefix '{prefix}' was not found.");
        int end = arguments.IndexOf("##", start + prefix.Length, StringComparison.Ordinal);
        Assert.True(end >= 0, $"Marker terminator for '{prefix}' was not found.");
        return arguments.Substring(start, end + 2 - start);
    }

    private sealed class ProcessHandlerTest : IDisposable
    {
        private readonly TestHostContext _hostContext;
        private readonly string _target;

        public ProcessHandlerTest(
            bool useV2,
            bool useJobScopedTaskEnvironment,
            bool modifyEnvironment,
            Dictionary<string, string> explicitEnvironment = null,
            Dictionary<string, VariableValue> runtimeValues = null,
            bool failOnStandardError = true,
            bool disableInlineExecution = false,
            string arguments = "",
            bool secureArguments = false,
            CancellationToken cancellationToken = default)
        {
            _hostContext = new TestHostContext(this);
            _hostContext.SetSingleton<IWorkerCommandManager>(new Mock<IWorkerCommandManager>().Object);
            State = new TaskEnvironmentState();
            RuntimeVariables = new Variables(
                _hostContext,
                runtimeValues ?? new Dictionary<string, VariableValue>(),
                out _);
            ExecutionContext = new Mock<IExecutionContext>();
            ExecutionContext.SetupGet(context => context.PrependPath).Returns(new List<string>());
            ExecutionContext.SetupGet(context => context.Variables).Returns(RuntimeVariables);
            ExecutionContext.SetupGet(context => context.TaskEnvironmentState).Returns(State);
            ExecutionContext.SetupGet(context => context.CancellationToken).Returns(cancellationToken);
            ExecutionContext.Setup(context => context.GetScopedEnvironment()).Returns(new LocalEnvironment());
            ExecutionContext
                .Setup(context => context.GetVariableValueOrDefault("DistributedTask.Agent.UseJobScopedTaskEnvironment"))
                .Returns(useJobScopedTaskEnvironment.ToString());
            ExecutionContext
                .Setup(context => context.GetVariableValueOrDefault("AZP_75787_ENABLE_NEW_LOGIC"))
                .Returns(secureArguments.ToString());
            ExecutionContext
                .Setup(context => context.GetVariableValueOrDefault("AZP_75787_ENABLE_NEW_PH_LOGIC"))
                .Returns("false");
            ExecutionContext
                .Setup(context => context.GetVariableValueOrDefault(Constants.Variables.Agent.TempDirectory))
                .Returns(_hostContext.GetDirectory(WellKnownDirectory.Temp));

            var environment = new TaskEnvironment(explicitEnvironment);
            environment.Reset(State.GetSnapshot());
            _target = Path.Combine(
                _hostContext.GetDirectory(WellKnownDirectory.Temp),
                "process-handler-job-environment.cmd");
            Directory.CreateDirectory(Path.GetDirectoryName(_target));
            File.WriteAllText(_target, "@echo off");

            Handler = useV2 ? new ProcessHandlerV2() : new ProcessHandler();
            Handler.Initialize(_hostContext);
            Handler.Data = new ProcessHandlerData
            {
                Target = _target,
                ArgumentFormat = arguments,
                DisableInlineExecution = disableInlineExecution.ToString(),
                ModifyEnvironment = modifyEnvironment.ToString(),
            };
            Handler.Inputs = new Dictionary<string, string>
            {
                ["failOnStandardError"] = failOnStandardError.ToString(),
            };
            Handler.TaskDirectory = _hostContext.GetDirectory(WellKnownDirectory.Temp);
            Handler.Environment = environment;
            Handler.RuntimeVariables = RuntimeVariables;
            Handler.ExecutionContext = ExecutionContext.Object;
        }

        public Mock<IExecutionContext> ExecutionContext { get; }

        public IProcessHandler Handler { get; }

        public TestHostContext HostContext => _hostContext;

        public Variables RuntimeVariables { get; }

        public TaskEnvironmentState State { get; }

        public string TempDirectory => _hostContext.GetDirectory(WellKnownDirectory.Temp);

        public void EnqueueAttempt(
            Action<Mock<IProcessInvoker>, IDictionary<string, string>, string> emit,
            int exitCode = 0,
            bool cancel = false)
        {
            var invoker = new Mock<IProcessInvoker>();
            invoker
                .Setup(processInvoker => processInvoker.ExecuteAsync(
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
                .Returns<string, string, string, IDictionary<string, string>, bool, Encoding, bool, InputQueue<string>, bool, bool, CancellationToken>(
                    (_, _, arguments, environment, _, _, _, _, _, _, _) =>
                    {
                        emit(invoker, environment, arguments);
                        return cancel
                            ? Task.FromCanceled<int>(new CancellationToken(canceled: true))
                            : Task.FromResult(exitCode);
                    });
            _hostContext.EnqueueInstance<IProcessInvoker>(invoker.Object);
        }

        public void Dispose()
        {
            if (File.Exists(_target))
            {
                File.Delete(_target);
            }

            _hostContext.Dispose();
        }
    }
}
