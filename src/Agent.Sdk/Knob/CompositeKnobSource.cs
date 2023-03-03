// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace Agent.Sdk.Knob
{

    public class CompositeKnobSource : ICompositeKnobSource
    {
        private readonly string _defaultValue = null;
        private IKnobSource[] _sources;

        public string DefaultValue
        {
            get => _defaultValue;
            set { }
        }

        public CompositeKnobSource(params IKnobSource[] sources)
        {
            _sources = sources;
        }

        public CompositeKnobSource(string defaultValue, params IKnobSource[] sources)
        {
            _sources = sources;
            _defaultValue = defaultValue;

            foreach (var s in _sources)
            {
                s.DefaultValue = _defaultValue;
            }
        }

        public KnobValue GetValue(IKnobValueContext context)
        {
            foreach (var source in _sources)
            {
                var value = source.GetValue(context);
                if (!(value is null))
                {
                    return value;
                }
            }
            return null;
        }

        /// <summary>
        /// Returns knob value by specific source type
        /// </summary>
        /// <returns>Returns knob value if it's found in knob sources, otherwise returns null</returns>
        public KnobValue GetValue<T>(IKnobValueContext context)
        {
            foreach (var source in _sources)
            {
                if (source.GetType() == typeof(T))
                {
                    return source.GetValue(context);
                }
            }

            return null;
        }

        public string GetDisplayString()
        {
            var strings = new List<string>();
            foreach (var source in _sources)
            {
                strings.Add(source.GetDisplayString());
            }
            return string.Join(", ", strings);
        }

        /// <summary>
        /// Returns true if object has source with type EnvironmentKnobSource and provided name
        /// </summary>
        /// <param name="name">Name to check</param>
        /// <returns>Returns true if source exists with this type and name</returns>
        public bool HasSourceWithTypeEnvironmentByName(string name)
        {
            foreach (var source in _sources)
            {
                if (source is EnvironmentKnobSource)
                {
                    var envName = (source as IEnvironmentKnobSource).GetEnvironmentVariableName();
                    if (String.Equals(envName, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
