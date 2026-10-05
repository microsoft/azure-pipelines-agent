// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Xunit;
using Microsoft.VisualStudio.Services.Agent;
using Microsoft.VisualStudio.Services.Agent.Tests;
using Microsoft.VisualStudio.Services.Agent.Worker.Handlers;
using Microsoft.VisualStudio.Services.Agent.Worker;
using System.Collections.Generic;
using Moq;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using System;

namespace Test.L0.Worker.Handlers;

public class LegacyPowerShellHandlerL0
{
    // Concrete subclass so the protected AddLegacyHostEnvironmentVariables can be exercised directly.
    private sealed class TestableLegacyPowerShellHandler : LegacyPowerShellHandler
    {
        protected override string GetArgumentFormat() => string.Empty;
        protected override string GetTarget() => "script.ps1";
        protected override string GetWorkingDirectory() => string.Empty;

        public void InvokeAddLegacyHostEnvironmentVariables(string scriptFile, string workingDirectory)
            => AddLegacyHostEnvironmentVariables(scriptFile, workingDirectory);
    }

    private static ServiceEndpoint CreateEndpoint(string name, string url)
    {
        return new ServiceEndpoint
        {
            Id = Guid.NewGuid(),
            Name = name,
            Url = new Uri(url),
            Type = "generic",
            Authorization = new EndpointAuthorization { Scheme = "UsernamePassword" }
        };
    }

    [Fact]
    [Trait("Level", "L0")]
    [Trait("Category", "Worker.Handlers")]
    public void OnlyExportsServiceConnectionsDeclaredByTask()
    {
        using var hostContext = new TestHostContext(this);
        hostContext.SetSingleton(new WorkerCommandManager() as IWorkerCommandManager);
        hostContext.SetSingleton(new ExtensionManager() as IExtensionManager);

        ServiceEndpoint declared = CreateEndpoint("declaredConnection", "https://declared.example");
        ServiceEndpoint undeclared = CreateEndpoint("undeclaredConnection", "https://undeclared.example");

        var executionContext = new Mock<IExecutionContext>();
        executionContext.Setup(x => x.Variables)
            .Returns(new Variables(hostContext, new Dictionary<string, VariableValue>(), out _));
        // Job-wide set: every connection used anywhere in the job.
        executionContext.Setup(x => x.Endpoints)
            .Returns(new List<ServiceEndpoint> { declared, undeclared });

        var handler = new TestableLegacyPowerShellHandler();
        handler.Initialize(hostContext);
        handler.Inputs = new Dictionary<string, string>();
        handler.TaskDirectory = string.Empty;
        handler.Environment = new Dictionary<string, string>();
        handler.ExecutionContext = executionContext.Object;
        // Scoped set: only the connection this task declared.
        handler.Endpoints = new List<ServiceEndpoint> { declared };

        handler.InvokeAddLegacyHostEnvironmentVariables("script.ps1", string.Empty);

        string declaredKey = declared.Id.ToString("D").ToUpperInvariant();
        string undeclaredKey = undeclared.Id.ToString("D").ToUpperInvariant();

        // The declared connection (and its auth) must be available to the task.
        Assert.True(handler.Environment.ContainsKey("VSTSPSHOSTENDPOINT_AUTH_" + declaredKey));
        Assert.True(handler.Environment.ContainsKey("VSTSPSHOSTENDPOINT_URL_" + declaredKey));

        // The connection the task never declared must NOT be exported, even though it is in the job-wide set.
        Assert.False(handler.Environment.ContainsKey("VSTSPSHOSTENDPOINT_AUTH_" + undeclaredKey));
        Assert.False(handler.Environment.ContainsKey("VSTSPSHOSTENDPOINT_URL_" + undeclaredKey));
    }

    [Fact]
    [Trait("Level", "L0")]
    [Trait("Category", "Worker.Handlers")]
    public void ExportsAllDeclaredServiceConnections()
    {
        using var hostContext = new TestHostContext(this);
        hostContext.SetSingleton(new WorkerCommandManager() as IWorkerCommandManager);
        hostContext.SetSingleton(new ExtensionManager() as IExtensionManager);

        ServiceEndpoint first = CreateEndpoint("firstConnection", "https://first.example");
        ServiceEndpoint second = CreateEndpoint("secondConnection", "https://second.example");

        var executionContext = new Mock<IExecutionContext>();
        executionContext.Setup(x => x.Variables)
            .Returns(new Variables(hostContext, new Dictionary<string, VariableValue>(), out _));
        executionContext.Setup(x => x.Endpoints)
            .Returns(new List<ServiceEndpoint> { first, second });

        var handler = new TestableLegacyPowerShellHandler();
        handler.Initialize(hostContext);
        handler.Inputs = new Dictionary<string, string>();
        handler.TaskDirectory = string.Empty;
        handler.Environment = new Dictionary<string, string>();
        handler.ExecutionContext = executionContext.Object;
        // Both connections declared by this task.
        handler.Endpoints = new List<ServiceEndpoint> { first, second };

        handler.InvokeAddLegacyHostEnvironmentVariables("script.ps1", string.Empty);

        Assert.True(handler.Environment.ContainsKey("VSTSPSHOSTENDPOINT_AUTH_" + first.Id.ToString("D").ToUpperInvariant()));
        Assert.True(handler.Environment.ContainsKey("VSTSPSHOSTENDPOINT_AUTH_" + second.Id.ToString("D").ToUpperInvariant()));
    }

    [Fact]
    [Trait("Level", "L0")]
    [Trait("Category", "Worker.Handlers")]
    public void ExportsSystemVssConnection()
    {
        // TaskRunner always adds SystemVssConnection to the scoped Endpoints. In-box legacy tasks
        // (QuickPerfTest, RunJMeterLoadTest, PowerShellOnTargetMachines, WindowsMachineFileCopy)
        // rely on it, so it must still be exported after scoping.
        using var hostContext = new TestHostContext(this);
        hostContext.SetSingleton(new WorkerCommandManager() as IWorkerCommandManager);
        hostContext.SetSingleton(new ExtensionManager() as IExtensionManager);

        ServiceEndpoint system = CreateEndpoint("SystemVssConnection", "https://dev.azure.com/org");
        ServiceEndpoint other = CreateEndpoint("someOtherConnection", "https://other.example");

        var executionContext = new Mock<IExecutionContext>();
        executionContext.Setup(x => x.Variables)
            .Returns(new Variables(hostContext, new Dictionary<string, VariableValue>(), out _));
        executionContext.Setup(x => x.Endpoints)
            .Returns(new List<ServiceEndpoint> { system, other });

        var handler = new TestableLegacyPowerShellHandler();
        handler.Initialize(hostContext);
        handler.Inputs = new Dictionary<string, string>();
        handler.TaskDirectory = string.Empty;
        handler.Environment = new Dictionary<string, string>();
        handler.ExecutionContext = executionContext.Object;
        // Scoped set contains only SystemVssConnection (the task declared no connectedService input).
        handler.Endpoints = new List<ServiceEndpoint> { system };

        handler.InvokeAddLegacyHostEnvironmentVariables("script.ps1", string.Empty);

        // SystemVssConnection is exported under its dedicated keys (no id suffix).
        Assert.True(handler.Environment.ContainsKey("VSTSPSHOSTSYSTEMENDPOINT_URL"));
        Assert.True(handler.Environment.ContainsKey("VSTSPSHOSTSYSTEMENDPOINT_AUTH"));
        // The undeclared connection, not in the scoped set, is not exported.
        Assert.False(handler.Environment.ContainsKey("VSTSPSHOSTENDPOINT_URL_" + other.Id.ToString("D").ToUpperInvariant()));
    }
}
