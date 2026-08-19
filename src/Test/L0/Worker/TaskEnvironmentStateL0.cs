// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker
{
    public sealed class TaskEnvironmentStateL0
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void SetRemoveAndReAddMaintainsExclusiveState()
        {
            var state = new TaskEnvironmentState();

            state.Set("EXAMPLE", "one");
            state.Remove("EXAMPLE");

            TaskEnvironmentSnapshot removedSnapshot = state.GetSnapshot();
            Assert.Empty(removedSnapshot.Values);
            Assert.Contains("EXAMPLE", removedSnapshot.Removed);

            state.Set("EXAMPLE", "two");

            TaskEnvironmentSnapshot reAddedSnapshot = state.GetSnapshot();
            Assert.Equal("two", reAddedSnapshot.Values["EXAMPLE"]);
            Assert.Empty(reAddedSnapshot.Removed);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void UsesEnvironmentVariableNameComparer()
        {
            var state = new TaskEnvironmentState();
            state.Set("MixedCase", "value");

            state.Remove("MIXEDCASE");

            TaskEnvironmentSnapshot snapshot = state.GetSnapshot();
            if (VarUtil.EnvironmentVariableKeyComparer.Equals("MixedCase", "MIXEDCASE"))
            {
                Assert.Empty(snapshot.Values);
                Assert.Contains("MIXEDCASE", snapshot.Removed);
            }
            else
            {
                Assert.Equal("value", snapshot.Values["MixedCase"]);
                Assert.Contains("MIXEDCASE", snapshot.Removed);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void SnapshotIsIsolatedFromLaterMutations()
        {
            var state = new TaskEnvironmentState();
            state.Set("FIRST", "one");
            TaskEnvironmentSnapshot snapshot = state.GetSnapshot();

            state.Remove("FIRST");
            state.Set("SECOND", "two");

            Assert.Equal("one", snapshot.Values["FIRST"]);
            Assert.False(snapshot.Values.ContainsKey("SECOND"));
            Assert.Empty(snapshot.Removed);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void TaskEnvironmentTracksSetRemoveAndReAdd()
        {
            var environment = new TaskEnvironment();

            environment["EXAMPLE"] = null;
            Assert.Equal(string.Empty, environment["EXAMPLE"]);

            environment.Remove("EXAMPLE");
            Assert.False(environment.ContainsKey("EXAMPLE"));
            Assert.Contains("EXAMPLE", ((IEnvironmentVariableRemovals)environment).RemovedEnvironmentVariables);

            environment["EXAMPLE"] = "restored";
            Assert.Equal("restored", environment["EXAMPLE"]);
            Assert.DoesNotContain("EXAMPLE", ((IEnvironmentVariableRemovals)environment).RemovedEnvironmentVariables);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void TaskEnvironmentAppliesStateSnapshot()
        {
            var state = new TaskEnvironmentState();
            state.Set("SET_BY_STATE", "value");
            state.Remove("REMOVED_BY_STATE");
            var environment = new TaskEnvironment
            {
                ["REMOVED_BY_STATE"] = "old",
                ["UNCHANGED"] = "unchanged",
            };

            environment.Apply(state.GetSnapshot());

            Assert.Equal("value", environment["SET_BY_STATE"]);
            Assert.Equal("unchanged", environment["UNCHANGED"]);
            Assert.False(environment.ContainsKey("REMOVED_BY_STATE"));
            Assert.Contains(
                "REMOVED_BY_STATE",
                ((IEnvironmentVariableRemovals)environment).RemovedEnvironmentVariables);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void TaskEnvironmentUsesEnvironmentVariableNameComparer()
        {
            var environment = new TaskEnvironment
            {
                ["MixedCase"] = "value",
            };

            environment.Remove("MIXEDCASE");

            if (VarUtil.EnvironmentVariableKeyComparer.Equals("MixedCase", "MIXEDCASE"))
            {
                Assert.False(environment.ContainsKey("MixedCase"));
            }
            else
            {
                Assert.Equal("value", environment["MixedCase"]);
            }

            Assert.Contains(
                "MIXEDCASE",
                ((IEnvironmentVariableRemovals)environment).RemovedEnvironmentVariables);
        }
    }
}
