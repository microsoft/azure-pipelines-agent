// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker;
using System.Collections.Generic;
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
        public void ApplyChangesPersistsOnlyOrdinalDelta()
        {
            var state = new TaskEnvironmentState();
            state.Set("PRIOR_REMOVED", "prior");
            var initial = new Dictionary<string, string>(VarUtil.EnvironmentVariableKeyComparer)
            {
                ["UNCHANGED"] = "same",
                ["UPDATED"] = "before",
                ["REMOVED"] = "before",
                ["PRIOR_REMOVED"] = "prior",
                ["EMPTY"] = "before",
            };
            var final = new Dictionary<string, string>(VarUtil.EnvironmentVariableKeyComparer)
            {
                ["UNCHANGED"] = "same",
                ["UPDATED"] = "after",
                ["ADDED"] = "after",
                ["EMPTY"] = string.Empty,
            };

            state.ApplyChanges(initial, final, excludedNames: new[] { "EXCLUDED" });

            TaskEnvironmentSnapshot snapshot = state.GetSnapshot();
            Assert.False(snapshot.Values.ContainsKey("UNCHANGED"));
            Assert.Equal("after", snapshot.Values["UPDATED"]);
            Assert.Equal("after", snapshot.Values["ADDED"]);
            Assert.Equal(string.Empty, snapshot.Values["EMPTY"]);
            Assert.Contains("REMOVED", snapshot.Removed);
            Assert.Contains("PRIOR_REMOVED", snapshot.Removed);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void ApplyChangesHonorsExclusionsForValuesAndTombstones()
        {
            var state = new TaskEnvironmentState();
            var initial = new Dictionary<string, string>(VarUtil.EnvironmentVariableKeyComparer)
            {
                ["EXCLUDED_REMOVAL"] = "before",
            };
            var final = new Dictionary<string, string>(VarUtil.EnvironmentVariableKeyComparer)
            {
                ["EXCLUDED_ADDITION"] = "after",
            };

            state.ApplyChanges(
                initial,
                final,
                excludedNames: new[] { "EXCLUDED_REMOVAL", "EXCLUDED_ADDITION" });

            TaskEnvironmentSnapshot snapshot = state.GetSnapshot();
            Assert.Empty(snapshot.Values);
            Assert.Empty(snapshot.Removed);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void ApplyChangesReAddCancelsTombstone()
        {
            var state = new TaskEnvironmentState();
            state.Remove("RESTORED");

            state.ApplyChanges(
                new Dictionary<string, string>(VarUtil.EnvironmentVariableKeyComparer),
                new Dictionary<string, string>(VarUtil.EnvironmentVariableKeyComparer)
                {
                    ["RESTORED"] = "new",
                },
                excludedNames: System.Array.Empty<string>());

            TaskEnvironmentSnapshot snapshot = state.GetSnapshot();
            Assert.Equal("new", snapshot.Values["RESTORED"]);
            Assert.DoesNotContain("RESTORED", snapshot.Removed);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void ApplyChangesUnchangedOverlayDoesNotCorruptTombstone()
        {
            var state = new TaskEnvironmentState();
            state.Remove("RESTORED_FOR_ATTEMPT");
            var initial = new Dictionary<string, string>(VarUtil.EnvironmentVariableKeyComparer)
            {
                ["RESTORED_FOR_ATTEMPT"] = "explicit",
            };
            var final = new Dictionary<string, string>(initial, VarUtil.EnvironmentVariableKeyComparer);

            state.ApplyChanges(initial, final, excludedNames: System.Array.Empty<string>());

            TaskEnvironmentSnapshot snapshot = state.GetSnapshot();
            Assert.Empty(snapshot.Values);
            Assert.Contains("RESTORED_FOR_ATTEMPT", snapshot.Removed);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void ApplyChangesUsesEnvironmentVariableNameComparer()
        {
            var state = new TaskEnvironmentState();
            var initial = new Dictionary<string, string>(VarUtil.EnvironmentVariableKeyComparer)
            {
                ["MixedCase"] = "same",
            };
            var final = new Dictionary<string, string>(VarUtil.EnvironmentVariableKeyComparer)
            {
                ["MIXEDCASE"] = "same",
            };

            state.ApplyChanges(initial, final, excludedNames: System.Array.Empty<string>());

            TaskEnvironmentSnapshot snapshot = state.GetSnapshot();
            if (VarUtil.EnvironmentVariableKeyComparer.Equals("MixedCase", "MIXEDCASE"))
            {
                Assert.Empty(snapshot.Values);
                Assert.Empty(snapshot.Removed);
            }
            else
            {
                Assert.Equal("same", snapshot.Values["MIXEDCASE"]);
                Assert.Contains("MixedCase", snapshot.Removed);
            }
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

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void TaskEnvironmentResetReconstructsJobStateThenExplicitMappings()
        {
            var explicitMappings = new Dictionary<string, string>(VarUtil.EnvironmentVariableKeyComparer)
            {
                ["EXPLICIT"] = "task",
                ["RESTORED"] = "task",
            };
            var environment = new TaskEnvironment(explicitMappings);
            var firstState = new TaskEnvironmentState();
            firstState.Set("JOB", "first");
            firstState.Remove("RESTORED");

            environment.Reset(firstState.GetSnapshot());
            environment.Set("GENERATED", "first-attempt");

            var currentState = new TaskEnvironmentState();
            currentState.Set("JOB", "current");
            currentState.Set("NEW_JOB", "new");
            currentState.Remove("REMOVED");
            currentState.Remove("RESTORED");
            environment.Reset(currentState.GetSnapshot());

            Assert.Equal("current", environment["JOB"]);
            Assert.Equal("new", environment["NEW_JOB"]);
            Assert.Equal("task", environment["EXPLICIT"]);
            Assert.Equal("task", environment["RESTORED"]);
            Assert.False(environment.ContainsKey("GENERATED"));
            Assert.Contains("REMOVED", environment.RemovedEnvironmentVariables);
            Assert.DoesNotContain("RESTORED", environment.RemovedEnvironmentVariables);
        }
    }
}
