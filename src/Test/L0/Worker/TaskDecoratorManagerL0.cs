// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Microsoft.VisualStudio.Services.Agent.Worker;
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
            const String PostTargetTask = "__system_posttargettask_";
            const String PreTargetTask = "__system_pretargettask_";
            var taskWithPreTarget = $"{PreTargetTask}TestTask";
            var taskWithPostTarget = $"{PostTargetTask}TestTask";
            
            TaskDecoratorManager decoratorManager = new TaskDecoratorManager();

            Assert.True(decoratorManager.IsInjectedTaskForTarget(taskWithPostTarget));
            Assert.True(decoratorManager.IsInjectedTaskForTarget(taskWithPreTarget));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void IsInjectedTaskForTarget_TaskWithoutTargetPrefix()
        {
            var taskWithoutTarget = "TestTask";

            TaskDecoratorManager decoratorManager = new TaskDecoratorManager();

            Assert.False(decoratorManager.IsInjectedTaskForTarget(taskWithoutTarget));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void IsInjectedTaskForTarget_NullValueInTaskName()
        {
            TaskDecoratorManager decoratorManager = new TaskDecoratorManager();

            Assert.False(decoratorManager.IsInjectedTaskForTarget(null));
        }
    }
}
