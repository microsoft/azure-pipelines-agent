using System;
using Agent.Sdk;
using Microsoft.VisualStudio.Services.Agent.Worker;
using System.Collections.Generic;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Moq;
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
            public int ExecutionCount { get; private set; }

            public void Execute(IExecutionContext context, Command command)
            {
                ExecutionCount++;
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

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void PauseAndResumeCommandsControlProcessing()
        {
            using var fixture = new WorkerCommandManagerFixture();
            var context = fixture.CreateExecutionContext();
            const string token = "abcdef0123456789abcdef0123456789";

            Assert.True(fixture.Manager.TryProcessCommand(context.Object, "##vso[TestL0.foo]"));
            Assert.Equal(1, fixture.FooCommand.ExecutionCount);

            Assert.True(fixture.Manager.TryProcessCommand(context.Object, $"##vso[Agent.PauseCommands]{token}"));
            Assert.False(fixture.Manager.TryProcessCommand(context.Object, "##vso[TestL0.foo]"));
            Assert.Equal(1, fixture.FooCommand.ExecutionCount);

            Assert.False(fixture.Manager.TryProcessCommand(context.Object, $"##vso[agent.resumecommands]{token.ToUpperInvariant()}"));
            Assert.False(fixture.Manager.TryProcessCommand(context.Object, $"prefix ##vso[agent.resumecommands]{token}"));
            Assert.False(fixture.Manager.TryProcessCommand(context.Object, "##vso[TestL0.foo]"));

            Assert.True(fixture.Manager.TryProcessCommand(context.Object, $"##vso[AGENT.RESUMECOMMANDS]{token}"));
            Assert.True(fixture.Manager.TryProcessCommand(context.Object, "##vso[TestL0.foo]"));
            Assert.Equal(2, fixture.FooCommand.ExecutionCount);
            context.Verify(x => x.Write(null, StringUtil.Loc("LoggingCommandProcessingPaused"), true), Times.Once);
            context.Verify(x => x.Write(null, StringUtil.Loc("LoggingCommandProcessingResumed"), true), Times.Once);
            context.Verify(
                x => x.Write(null, It.Is<string>(message => message.Contains(token, StringComparison.Ordinal)), true),
                Times.Never);
        }

        public static IEnumerable<object[]> ValidCommandSuppressionTokens()
        {
            yield return new object[] { "abcdefghijklmnop" };
            yield return new object[] { "abcd_efgh-ijklmnop" };
            yield return new object[] { new string('a', 128) };
        }

        [Theory]
        [MemberData(nameof(ValidCommandSuppressionTokens))]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void ValidCommandSuppressionTokensAreAccepted(string token)
        {
            using var fixture = new WorkerCommandManagerFixture();
            var context = fixture.CreateExecutionContext();

            Assert.True(fixture.Manager.TryProcessCommand(context.Object, $"##vso[agent.pausecommands]{token}"));
            Assert.False(fixture.Manager.TryProcessCommand(context.Object, "##vso[TestL0.foo]"));
            Assert.True(fixture.Manager.TryProcessCommand(context.Object, $"##vso[agent.resumecommands]{token}"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void PauseCommandCannotBeReplacedWhileCommandsArePaused()
        {
            using var fixture = new WorkerCommandManagerFixture();
            var context = fixture.CreateExecutionContext();
            var originalToken = Guid.NewGuid().ToString("N");
            var replacementToken = Guid.NewGuid().ToString("N");

            Assert.True(fixture.Manager.TryProcessCommand(context.Object, $"##vso[agent.pausecommands]{originalToken}"));
            Assert.False(fixture.Manager.TryProcessCommand(context.Object, $"##vso[agent.pausecommands]{replacementToken}"));
            Assert.False(fixture.Manager.TryProcessCommand(context.Object, $"##vso[agent.resumecommands]{replacementToken}"));
            Assert.False(fixture.Manager.TryProcessCommand(context.Object, "##vso[TestL0.foo]"));

            Assert.True(fixture.Manager.TryProcessCommand(context.Object, $"##vso[agent.resumecommands]{originalToken}"));
            Assert.True(fixture.Manager.TryProcessCommand(context.Object, "##vso[TestL0.foo]"));
            Assert.Equal(1, fixture.FooCommand.ExecutionCount);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void CommandSuppressionIsScopedAndCanBeReset()
        {
            using var fixture = new WorkerCommandManagerFixture();
            var pausedContext = fixture.CreateExecutionContext();
            var enabledContext = fixture.CreateExecutionContext();
            var token = Guid.NewGuid().ToString("N");

            Assert.True(fixture.Manager.TryProcessCommand(pausedContext.Object, $"##vso[agent.pausecommands]{token}"));
            Assert.False(fixture.Manager.TryProcessCommand(pausedContext.Object, "##vso[TestL0.foo]"));

            Assert.True(fixture.Manager.TryProcessCommand(enabledContext.Object, "##vso[TestL0.foo]"));
            Assert.Equal(1, fixture.FooCommand.ExecutionCount);

            fixture.Manager.ResetCommandSuppression(pausedContext.Object);

            Assert.True(fixture.Manager.TryProcessCommand(pausedContext.Object, "##vso[TestL0.foo]"));
            Assert.Equal(2, fixture.FooCommand.ExecutionCount);
            pausedContext.Verify(
                x => x.Write(null, StringUtil.Loc("LoggingCommandProcessingResumedAtTaskCompletion"), true),
                Times.Once);
        }

        public static IEnumerable<object[]> InvalidPauseCommands()
        {
            yield return new object[] { "##vso[agent.pausecommands]" };
            yield return new object[] { "##vso[agent.pausecommands]short" };
            yield return new object[] { $"##vso[agent.pausecommands]{new string('a', 129)}" };
            yield return new object[] { "##vso[agent.pausecommands]invalid$token-value" };
            yield return new object[] { $"prefix ##vso[agent.pausecommands]{Guid.NewGuid():N}" };
        }

        [Theory]
        [MemberData(nameof(InvalidPauseCommands))]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void InvalidPauseCommandsDoNotChangeCommandProcessing(string pauseCommand)
        {
            using var fixture = new WorkerCommandManagerFixture();
            var context = fixture.CreateExecutionContext();

            Assert.False(fixture.Manager.TryProcessCommand(context.Object, pauseCommand));
            Assert.True(fixture.Manager.TryProcessCommand(context.Object, "##vso[TestL0.foo]"));
            Assert.Equal(1, fixture.FooCommand.ExecutionCount);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void ResumeCommandWithoutPauseDoesNotChangeCommandProcessing()
        {
            using var fixture = new WorkerCommandManagerFixture();
            var context = fixture.CreateExecutionContext();
            var token = Guid.NewGuid().ToString("N");

            Assert.False(fixture.Manager.TryProcessCommand(context.Object, $"##vso[agent.resumecommands]{token}"));
            Assert.True(fixture.Manager.TryProcessCommand(context.Object, "##vso[TestL0.foo]"));
            Assert.Equal(1, fixture.FooCommand.ExecutionCount);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void SuppressedMalformedCommandsDoNotProduceWarnings()
        {
            using var fixture = new WorkerCommandManagerFixture();
            var context = fixture.CreateExecutionContext();
            var token = Guid.NewGuid().ToString("N");

            Assert.True(fixture.Manager.TryProcessCommand(context.Object, $"##vso[agent.pausecommands]{token}"));
            Assert.False(fixture.Manager.TryProcessCommand(context.Object, "literal ##vso[not-a-command"));

            context.Verify(x => x.AddIssue(It.IsAny<Issue>()), Times.Never);
        }

        private sealed class WorkerCommandManagerFixture : IDisposable
        {
            private readonly TestHostContext _hostContext;
            private readonly Variables _variables;

            public WorkerCommandManagerFixture()
            {
                _hostContext = new TestHostContext(this);
                var extension = new TestWorkerCommandExtensionL0();
                extension.Initialize(_hostContext);
                FooCommand = (FooCommand)extension.GetWorkerCommand("foo");

                var extensionManager = new Mock<IExtensionManager>();
                extensionManager
                    .Setup(x => x.GetExtensions<IWorkerCommandExtension>())
                    .Returns(new List<IWorkerCommandExtension> { extension });
                _hostContext.SetSingleton(extensionManager.Object);

                var restrictions = new Mock<ITaskRestrictionsChecker>();
                restrictions
                    .Setup(x => x.CheckCommand(It.IsAny<IExecutionContext>(), It.IsAny<IWorkerCommand>(), It.IsAny<Command>()))
                    .Returns(true);
                _hostContext.SetSingleton(restrictions.Object);

                _variables = new Variables(
                    _hostContext,
                    new Dictionary<string, VariableValue> { ["system.hostType"] = "build" },
                    out _);

                Manager = new WorkerCommandManager();
                Manager.Initialize(_hostContext);
            }

            public FooCommand FooCommand { get; }
            public WorkerCommandManager Manager { get; }

            public Mock<IExecutionContext> CreateExecutionContext()
            {
                var context = new Mock<IExecutionContext>();
                context.SetupGet(x => x.Id).Returns(Guid.NewGuid());
                context.SetupGet(x => x.Variables).Returns(_variables);
                context.Setup(x => x.GetHostContext()).Returns(_hostContext);
                context.Setup(x => x.GetScopedEnvironment()).Returns(new SystemEnvironment());
                context
                    .Setup(x => x.GetVariableValueOrDefault(It.IsAny<string>()))
                    .Returns((string name) => _variables.Get(name));
                return context;
            }

            public void Dispose()
            {
                _hostContext.Dispose();
            }
        }
    }
}
