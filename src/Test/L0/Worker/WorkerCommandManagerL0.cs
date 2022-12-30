using System;
using Microsoft.VisualStudio.Services.Agent.Worker;
using System.Collections.Generic;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker
{
    public sealed class WorkerCommandManagerL0
    {

        public sealed class TestWorkerCommandExtensionL0 : BaseWorkerCommandExtension
        {
            public TestWorkerCommandExtensionL0()
            {
                CommandArea = "TestL0";
                SupportedHostTypes = HostTypes.All;
                InstallWorkerCommand(new FooCommand());
                InstallWorkerCommand(new BarCommand());
            }

            public void InstallFoo2Command()
            {
                InstallWorkerCommand(new Foo2Command());
            }
        }

        public class FooCommand : IWorkerCommand
        {
            public string Name => "foo";
            public List<string> Aliases => null;

            public void Execute(IExecutionContext context, Command command)
            {
            }
        }

        public class Foo2Command : IWorkerCommand
        {
            public string Name => "foo";
            public List<string> Aliases => null;

            public void Execute(IExecutionContext context, Command command)
            {
            }
        }

        public class BarCommand : IWorkerCommand
        {
            public string Name => "bar";
            public List<string> Aliases => new List<string>() { "cat" };

            public void Execute(IExecutionContext context, Command command)
            {
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void SimpleTests()
        {
            var commandExt = new TestWorkerCommandExtensionL0();
            Assert.Throws<Exception>(() => commandExt.InstallFoo2Command());

            IWorkerCommand command = commandExt.GetWorkerCommand("foo");
            Assert.Equal("foo", command.Name);
            Assert.IsType<FooCommand>(command);

            IWorkerCommand command2 = commandExt.GetWorkerCommand("bar");
            Assert.Equal("bar", command2.Name);
            IWorkerCommand command3 = commandExt.GetWorkerCommand("cat");
            Assert.Equal("bar", command3.Name);
            Assert.Equal(command2, command3);
        }
    }
}
