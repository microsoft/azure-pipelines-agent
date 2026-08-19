// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk;
using System.Collections;
using System.Collections.Generic;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    internal sealed class TaskEnvironment : IDictionary<string, string>, IEnvironmentVariableRemovals
    {
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(VarUtil.EnvironmentVariableKeyComparer);
        private readonly HashSet<string> _removed = new HashSet<string>(VarUtil.EnvironmentVariableKeyComparer);

        public string this[string key]
        {
            get => _values[key];
            set => Set(key, value);
        }

        public ICollection<string> Keys => _values.Keys;

        public ICollection<string> Values => _values.Values;

        public int Count => _values.Count;

        public bool IsReadOnly => false;

        public IReadOnlyCollection<string> RemovedEnvironmentVariables => _removed;

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

        public void Add(string key, string value)
        {
            ArgUtil.NotNullOrEmpty(key, nameof(key));
            _values.Add(key, value ?? string.Empty);
            _removed.Remove(key);
        }

        public void Add(KeyValuePair<string, string> item)
        {
            Add(item.Key, item.Value);
        }

        public void Clear()
        {
            _removed.UnionWith(_values.Keys);
            _values.Clear();
        }

        public bool Contains(KeyValuePair<string, string> item)
        {
            return ((ICollection<KeyValuePair<string, string>>)_values).Contains(item);
        }

        public bool ContainsKey(string key)
        {
            return _values.ContainsKey(key);
        }

        public void CopyTo(KeyValuePair<string, string>[] array, int arrayIndex)
        {
            ((ICollection<KeyValuePair<string, string>>)_values).CopyTo(array, arrayIndex);
        }

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
        {
            return _values.GetEnumerator();
        }

        public bool Remove(string key)
        {
            ArgUtil.NotNullOrEmpty(key, nameof(key));
            bool removed = _values.Remove(key);
            _removed.Add(key);
            return removed;
        }

        public bool Remove(KeyValuePair<string, string> item)
        {
            ArgUtil.NotNullOrEmpty(item.Key, nameof(item.Key));
            bool removed = ((ICollection<KeyValuePair<string, string>>)_values).Remove(item);
            if (removed)
            {
                _removed.Add(item.Key);
            }

            return removed;
        }

        public void Set(string key, string value)
        {
            ArgUtil.NotNullOrEmpty(key, nameof(key));
            _removed.Remove(key);
            _values[key] = value ?? string.Empty;
        }

        public bool TryGetValue(string key, out string value)
        {
            return _values.TryGetValue(key, out value);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
