// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
    }
}
