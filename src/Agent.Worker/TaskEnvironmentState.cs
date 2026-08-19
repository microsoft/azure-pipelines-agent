// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
