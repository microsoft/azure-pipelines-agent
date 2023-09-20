// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Xunit;
using Microsoft.VisualStudio.Services.Agent.Tests;
using Microsoft.VisualStudio.Services.Agent.Worker.Handlers;
using Microsoft.VisualStudio.Services.Agent;
using Microsoft.VisualStudio.Services.Agent.Worker;
using System.IO;
using Moq;
using System.Collections.Generic;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Agent.Sdk;
using System.Diagnostics;
using System;
using System.Linq;

namespace Test.L0.Worker.Handlers;

public class ProcessHandlerL0
{
    [Fact]
    [Trait("Level", "L0")]
    [Trait("Category", "Worker.Handlers")]
    [Trait("SkipOn", "linux")]
    [Trait("SkipOn", "freebsd")]
    [Trait("SkipOn", "darwin")]
    public async void ProcessHandlerV2_BasicExecution()
    {
        using var hostContext = CreateTestHostContext();

        using var processInvoker = new ProcessInvokerWrapper();
        hostContext.EnqueueInstance<IProcessInvoker>(processInvoker);

        using var targetScript = new TestScript(
            testTemp: hostContext.GetDirectory(WellKnownDirectory.Temp),
            scriptName: "hello.cmd"
        );
        targetScript.WriteContent(@"
@echo off
echo hello");

        var executionContext = CreateMockExecutionContext(hostContext);
        // Disable new logic for args protection.
        executionContext.Setup(x => x.GetVariableValueOrDefault("AZP_75787_ENABLE_NEW_LOGIC")).Returns("false");

        var handler = new ProcessHandlerV2();
        handler.Initialize(hostContext);
        hostContext.EnqueueInstance<IProcessHandlerV2>(handler);

        handler.Data = new ProcessHandlerData()
        {
            Target = targetScript.ScriptPath,
            ArgumentFormat = "",
            DisableInlineExecution = false.ToString()
        };
        handler.Inputs = new();
        handler.TaskDirectory = "";
        handler.Environment = new();
        handler.RuntimeVariables = new(hostContext, new Dictionary<string, VariableValue>(), out _);
        handler.ExecutionContext = executionContext.Object;

        await handler.RunAsync();

        executionContext.Verify(x => x.Write(It.IsAny<string>(), "hello", It.IsAny<bool>()), Times.Once);
        executionContext.Verify(x => x.Write(null, It.IsAny<string>(), It.IsAny<bool>()), Times.Once);
    }

    [Fact]
    [Trait("Level", "L0")]
    [Trait("Category", "Worker.Handlers")]
    [Trait("SkipOn", "linux")]
    [Trait("SkipOn", "freebsd")]
    [Trait("SkipOn", "darwin")]
    public async void ProcessHandlerV2_FileExecution()
    {
        using var hostContext = CreateTestHostContext();

        using var processInvoker = new ProcessInvokerWrapper();
        hostContext.EnqueueInstance<IProcessInvoker>(processInvoker);

        var handler = new ProcessHandlerV2();
        handler.Initialize(hostContext);
        hostContext.EnqueueInstance<IProcessHandlerV2>(handler);

        string temp = hostContext.GetDirectory(WellKnownDirectory.Temp);
        using var targetScript = new TestScript(
            testTemp: temp,
            scriptName: "hello.cmd"
        );
        targetScript.WriteContent(@"
@echo off
echo hello");

        handler.Data = new ProcessHandlerData()
        {
            Target = targetScript.ScriptPath,
            ArgumentFormat = "",
            DisableInlineExecution = true.ToString()
        };
        handler.Inputs = new();
        handler.TaskDirectory = "";
        handler.Environment = new();
        handler.RuntimeVariables = new(hostContext, new Dictionary<string, VariableValue>(), out _);

        var executionContext = CreateMockExecutionContext(hostContext);
        executionContext.Setup(x => x.GetVariableValueOrDefault("AZP_75787_ENABLE_NEW_LOGIC")).Returns("true");
        // Disable new logic for args validation, use a file instead.
        executionContext.Setup(x => x.GetVariableValueOrDefault("AZP_75787_ENABLE_NEW_PH_LOGIC")).Returns("false");

        handler.ExecutionContext = executionContext.Object;

        await handler.RunAsync();

        var tempFiles = Directory.GetFiles(temp);
        Assert.True(tempFiles.Length == 2);

        var scriptFile = tempFiles.FirstOrDefault(f => f.Contains("processHandlerScript_", StringComparison.Ordinal));
        Assert.NotNull(scriptFile);
        Assert.True(File.ReadAllText(scriptFile).Contains("!AGENT_PH_ARGS_", StringComparison.Ordinal));

        executionContext.Verify(x => x.Write(It.IsAny<string>(), "hello", It.IsAny<bool>()), Times.Once);
        executionContext.Verify(x => x.Write(null, It.IsAny<string>(), It.IsAny<bool>()), Times.Exactly(3));
    }

    [Fact]
    [Trait("Level", "L0")]
    [Trait("Category", "Worker.Handlers")]
    [Trait("SkipOn", "linux")]
    [Trait("SkipOn", "freebsd")]
    [Trait("SkipOn", "darwin")]
    public async void ProcessHandlerV2_Validation_passes()
    {
        using var hostContext = CreateTestHostContext();

        using var processInvoker = new ProcessInvokerWrapper();
        hostContext.EnqueueInstance<IProcessInvoker>(processInvoker);

        using var targetScript = new TestScript(
            testTemp: hostContext.GetDirectory(WellKnownDirectory.Temp),
            scriptName: "hello.cmd"
        );
        targetScript.WriteContent(@"
@echo off
echo hello");

        var handler = new ProcessHandlerV2();
        handler.Initialize(hostContext);
        hostContext.EnqueueInstance<IProcessHandlerV2>(handler);

        handler.Data = new ProcessHandlerData()
        {
            Target = targetScript.ScriptPath,
            // This is a valid argument format, it should pass validation.
            ArgumentFormat = "123",
            DisableInlineExecution = true.ToString()
        };
        handler.Inputs = new();
        handler.TaskDirectory = "";
        handler.Environment = new();
        handler.RuntimeVariables = new(hostContext, new Dictionary<string, VariableValue>(), out _);

        var executionContext = CreateMockExecutionContext(hostContext);
        // Enable args protection.
        executionContext.Setup(x => x.GetVariableValueOrDefault("AZP_75787_ENABLE_NEW_LOGIC")).Returns("true");
        // Enable args validation instead of using a file.
        executionContext.Setup(x => x.GetVariableValueOrDefault("AZP_75787_ENABLE_NEW_PH_LOGIC")).Returns("true");

        handler.ExecutionContext = executionContext.Object;

        await handler.RunAsync();

        executionContext.Verify(x => x.Write(It.IsAny<string>(), "hello", It.IsAny<bool>()), Times.Once);
        executionContext.Verify(x => x.Write(null, It.IsAny<string>(), It.IsAny<bool>()), Times.Once);
    }

    [Fact]
    [Trait("Level", "L0")]
    [Trait("Category", "Worker.Handlers")]
    [Trait("SkipOn", "linux")]
    [Trait("SkipOn", "freebsd")]
    [Trait("SkipOn", "darwin")]
    public async void ProcessHandlerV2_Validation_fails()
    {
        using var hostContext = CreateTestHostContext();

        using var processInvoker = new ProcessInvokerWrapper();
        hostContext.EnqueueInstance<IProcessInvoker>(processInvoker);

        var handler = new ProcessHandlerV2();
        handler.Initialize(hostContext);
        hostContext.EnqueueInstance<IProcessHandlerV2>(handler);

        using var targetScript = new TestScript(
            testTemp: hostContext.GetDirectory(WellKnownDirectory.Temp),
            scriptName: "hello.cmd"
        );
        targetScript.WriteContent(@"
@echo off
echo hello");

        handler.Data = new ProcessHandlerData()
        {
            Target = targetScript.ScriptPath,
            // This is an invalid argument format, it should fail validation.
            ArgumentFormat = "123; echo hacked",
            DisableInlineExecution = true.ToString()
        };
        handler.Inputs = new();
        handler.TaskDirectory = "";
        handler.Environment = new();
        handler.RuntimeVariables = new(hostContext, new Dictionary<string, VariableValue>(), out _);

        var executionContext = CreateMockExecutionContext(hostContext);
        // Enable args protection.
        executionContext.Setup(x => x.GetVariableValueOrDefault("AZP_75787_ENABLE_NEW_LOGIC")).Returns("true");
        // Enable args validation instead of using a file.
        executionContext.Setup(x => x.GetVariableValueOrDefault("AZP_75787_ENABLE_NEW_PH_LOGIC")).Returns("true");

        handler.ExecutionContext = executionContext.Object;

        await Assert.ThrowsAsync<InvalidScriptArgsException>(async () => await handler.RunAsync());
    }

    private Mock<IExecutionContext> CreateMockExecutionContext(IHostContext host)
    {
        var mockContext = new Mock<IExecutionContext>();
        mockContext.Setup(x => x.PrependPath).Returns(new List<string>());
        mockContext.Setup(x => x.Variables).Returns(new Variables(host, new Dictionary<string, VariableValue>(), out _));
        mockContext.Setup(x => x.GetScopedEnvironment()).Returns(new LocalEnvironment());

        return mockContext;
    }

    private TestHostContext CreateTestHostContext()
    {
        var hostContext = new TestHostContext(this);
        hostContext.SetSingleton(new WorkerCommandManager() as IWorkerCommandManager);
        hostContext.SetSingleton(new ExtensionManager() as IExtensionManager);

        return hostContext;
    }

    private class TestScript : IDisposable
    {
        private readonly string _scriptName;
        private readonly string _testTemp;

        public string ScriptPath => Path.Combine(_testTemp, _scriptName);

        public TestScript(string testTemp, string scriptName)
        {
            _testTemp = testTemp;
            _scriptName = scriptName;
        }

        public void WriteContent(string content)
        {
            Directory.CreateDirectory(_testTemp);

            File.WriteAllText(ScriptPath, content);
        }

        public void Dispose()
        {
            if (File.Exists(ScriptPath))
            {
                File.Delete(ScriptPath);
            }

            if (Directory.Exists(_testTemp))
            {
                try
                {
                    Directory.Delete(_testTemp);
                }
                catch (Exception ex)
                {
                    Trace.Write($"Failed to delete temp directory: {_testTemp}. {ex}", "Dispose");
                }
            }
        }
    }
}
