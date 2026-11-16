// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.Handlers;
using Moq;
using System.Collections.Generic;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker.Handlers
{
    public sealed class HandlerFactoryL0
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void CreatesPwshHandlerForPwshHandlerData()
        {
            using var hc = new TestHostContext(this);
            hc.SetSingleton(new WorkerCommandManager() as IWorkerCommandManager);
            hc.SetSingleton(new ExtensionManager() as IExtensionManager);
            hc.EnqueueInstance<IPwshHandler>(new PwshHandler());

            var executionContext = new Mock<IExecutionContext>();
            List<string> warnings;
            executionContext.Setup(x => x.Variables).Returns(new Variables(hc, new Dictionary<string, VariableValue>(), out warnings));
            executionContext.Setup(x => x.Endpoints).Returns(new List<ServiceEndpoint>());
            executionContext.Setup(x => x.TaskVariables).Returns(new Variables(hc, new Dictionary<string, VariableValue>(), out warnings));
            executionContext.Setup(x => x.PrependPath).Returns(new List<string>());
            executionContext.Setup(x => x.GetScopedEnvironment()).Returns(new SystemEnvironment());

            var factory = new HandlerFactory();
            factory.Initialize(hc);

            IHandler handler = factory.Create(
                executionContext: executionContext.Object,
                task: null,
                stepHost: new Mock<IStepHost>().Object,
                endpoints: new List<ServiceEndpoint>(),
                secureFiles: new List<SecureFile>(),
                data: new PwshHandlerData(),
                inputs: new Dictionary<string, string>(),
                environment: new Dictionary<string, string>(VarUtil.EnvironmentVariableKeyComparer),
                runtimeVariables: executionContext.Object.Variables,
                taskDirectory: hc.GetDirectory(WellKnownDirectory.Work));

            Assert.IsType<PwshHandler>(handler);
        }
    }
}
