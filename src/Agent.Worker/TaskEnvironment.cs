// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using System.Collections.Generic;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    internal sealed class TaskEnvironment : Dictionary<string, string>, IEnvironmentVariableRemovals
    {
        private readonly Dictionary<string, string> _explicitMappings;
        private readonly HashSet<string> _removed = new HashSet<string>(VarUtil.EnvironmentVariableKeyComparer);

        public TaskEnvironment()
            : this(null)
        {
        }

        public TaskEnvironment(IDictionary<string, string> explicitMappings)
            : base(VarUtil.EnvironmentVariableKeyComparer)
        {
            _explicitMappings = explicitMappings == null
                ? new Dictionary<string, string>(VarUtil.EnvironmentVariableKeyComparer)
                : new Dictionary<string, string>(explicitMappings, VarUtil.EnvironmentVariableKeyComparer);

            ApplyExplicitMappings();
        }

        public new string this[string key]
        {
            get => base[key];
            set => Set(key, value);
        }

        public IReadOnlyCollection<string> RemovedEnvironmentVariables => _removed;

        public new void Add(string key, string value)
        {
            ArgUtil.NotNullOrEmpty(key, nameof(key));
            base.Add(key, value ?? string.Empty);
            _removed.Remove(key);
        }

        public new void Clear()
        {
            _removed.UnionWith(Keys);
            base.Clear();
        }

        public new bool Remove(string key)
        {
            ArgUtil.NotNullOrEmpty(key, nameof(key));
            bool removed = base.Remove(key);
            _removed.Add(key);
            return removed;
        }

        public void Set(string key, string value)
        {
            ArgUtil.NotNullOrEmpty(key, nameof(key));
            _removed.Remove(key);
            base[key] = value ?? string.Empty;
        }

        public void Apply(TaskEnvironmentSnapshot snapshot)
        {
            ArgUtil.NotNull(snapshot, nameof(snapshot));

            foreach (string name in snapshot.Removed)
            {
                Remove(name);
            }

            foreach (KeyValuePair<string, string> pair in snapshot.Values)
            {
                Set(pair.Key, pair.Value);
            }
        }

        public void Reset(TaskEnvironmentSnapshot snapshot)
        {
            ArgUtil.NotNull(snapshot, nameof(snapshot));

            base.Clear();
            _removed.Clear();
            Apply(snapshot);
            ApplyExplicitMappings();
        }

        private void ApplyExplicitMappings()
        {
            foreach (KeyValuePair<string, string> pair in _explicitMappings)
            {
                Set(pair.Key, pair.Value);
            }
        }
    }
}
