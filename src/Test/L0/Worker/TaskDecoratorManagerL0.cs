// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Moq;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker
{
    public sealed class TaskDecoratorManagerL0
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void IsInjectedTaskForTarget_TaskWithTargetPrefix()
        {
            var executionContext = new Mock<IExecutionContext>();

            const String PostTargetTask = "__system_posttargettask_";
            const String PreTargetTask = "__system_pretargettask_";
            var taskWithPreTarget = $"{PreTargetTask}TestTask";
            var taskWithPostTarget = $"{PostTargetTask}TestTask";

            TaskDecoratorManager decoratorManager = new TaskDecoratorManager();

            Assert.True(decoratorManager.IsInjectedTaskForTarget(taskWithPostTarget, executionContext.Object));
            Assert.True(decoratorManager.IsInjectedTaskForTarget(taskWithPreTarget, executionContext.Object));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void IsInjectedTaskForTarget_TaskWithoutTargetPrefix()
        {
            var executionContext = new Mock<IExecutionContext>();

            var taskWithoutTarget = "TestTask";

            TaskDecoratorManager decoratorManager = new TaskDecoratorManager();

            Assert.False(decoratorManager.IsInjectedTaskForTarget(taskWithoutTarget, executionContext.Object));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void IsInjectedTaskForTarget_NullValueInTaskName()
        {
            var executionContext = new Mock<IExecutionContext>();

            TaskDecoratorManager decoratorManager = new TaskDecoratorManager();

            Assert.False(decoratorManager.IsInjectedTaskForTarget(null, executionContext.Object));
        }
    }
}
