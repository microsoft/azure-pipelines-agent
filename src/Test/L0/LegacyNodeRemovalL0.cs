// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using Agent.Sdk.Knob;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    public sealed class LegacyNodeRemovalL0
    {
        [Theory]
        [InlineData("UseNode10")]
        [InlineData("DisableNode6Tasks")]
        [InlineData("DisableNode6DeprecationWarning")]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void LegacyNodeKnobsAreRemoved(string name)
        {
            Assert.DoesNotContain(
                typeof(AgentKnobs).GetFields(BindingFlags.Public | BindingFlags.Static),
                field => field.Name == name);
        }

        [Theory]
        [InlineData("EnableEOLNodeVersionPolicy")]
        [InlineData("AgentDeprecatedNodeWarnings")]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void VersionIndependentNodeKnobsRemain(string name)
        {
            Assert.NotNull(typeof(AgentKnobs).GetField(name, BindingFlags.Public | BindingFlags.Static));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void TaskNodeRunnerDeprecationKnobRemains()
        {
            Assert.NotNull(typeof(AgentKnobs).GetField("CheckIfTaskNodeRunnerIsDeprecated246", BindingFlags.Public | BindingFlags.Static));
        }
    }
}
