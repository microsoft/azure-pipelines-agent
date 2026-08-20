// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    public sealed class TaskEnvironmentState
    {
        private readonly object _lock = new object();
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(VarUtil.EnvironmentVariableKeyComparer);
        private readonly HashSet<string> _removed = new HashSet<string>(VarUtil.EnvironmentVariableKeyComparer);

        public void Set(string name, string value)
        {
            ArgUtil.NotNullOrEmpty(name, nameof(name));
            ArgUtil.NotNull(value, nameof(value));

            lock (_lock)
            {
                _removed.Remove(name);
                _values[name] = value;
            }
        }

        internal void ApplyChanges(
            IReadOnlyDictionary<string, string> initial,
            IReadOnlyDictionary<string, string> final,
            IEnumerable<string> excludedNames)
        {
            Dictionary<string, string> initialCopy = CopyAndValidate(initial, nameof(initial));
            Dictionary<string, string> finalCopy = CopyAndValidate(final, nameof(final));
            ArgUtil.NotNull(excludedNames, nameof(excludedNames));
            var excluded = new HashSet<string>(VarUtil.EnvironmentVariableKeyComparer);
            foreach (string name in excludedNames)
            {
                ArgUtil.NotNullOrEmpty(name, nameof(excludedNames));
                excluded.Add(name);
            }

            lock (_lock)
            {
                foreach (KeyValuePair<string, string> pair in initialCopy)
                {
                    if (!excluded.Contains(pair.Key) && !finalCopy.ContainsKey(pair.Key))
                    {
                        _values.Remove(pair.Key);
                        _removed.Add(pair.Key);
                    }
                }

                foreach (KeyValuePair<string, string> pair in finalCopy)
                {
                    if (excluded.Contains(pair.Key))
                    {
                        continue;
                    }

                    if (!initialCopy.TryGetValue(pair.Key, out string initialValue)
                        || !string.Equals(initialValue, pair.Value, StringComparison.Ordinal))
                    {
                        _removed.Remove(pair.Key);
                        _values[pair.Key] = pair.Value;
                    }
                }
            }
        }

        public void Remove(string name)
        {
            ArgUtil.NotNullOrEmpty(name, nameof(name));

            lock (_lock)
            {
                _values.Remove(name);
                _removed.Add(name);
            }
        }

        public TaskEnvironmentSnapshot GetSnapshot()
        {
            lock (_lock)
            {
                return new TaskEnvironmentSnapshot(
                    new Dictionary<string, string>(_values, VarUtil.EnvironmentVariableKeyComparer),
                    _removed.ToList());
            }
        }

        private static Dictionary<string, string> CopyAndValidate(
            IReadOnlyDictionary<string, string> values,
            string parameterName)
        {
            ArgUtil.NotNull(values, parameterName);
            var copy = new Dictionary<string, string>(VarUtil.EnvironmentVariableKeyComparer);
            foreach (KeyValuePair<string, string> pair in values)
            {
                ArgUtil.NotNullOrEmpty(pair.Key, parameterName);
                ArgUtil.NotNull(pair.Value, parameterName);
                copy[pair.Key] = pair.Value;
            }

            return copy;
        }
    }

    public sealed class TaskEnvironmentSnapshot
    {
        internal TaskEnvironmentSnapshot(Dictionary<string, string> values, List<string> removed)
        {
            Values = new ReadOnlyDictionary<string, string>(values);
            Removed = new ReadOnlyCollection<string>(removed);
        }

        public IReadOnlyDictionary<string, string> Values { get; }

        public IReadOnlyCollection<string> Removed { get; }
    }
}
