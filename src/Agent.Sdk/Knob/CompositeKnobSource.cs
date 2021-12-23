// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace Agent.Sdk.Knob
{

    public class CompositeKnobSource : ICompositeKnobSource
    {
        private IKnobSource[] _sources;

        public CompositeKnobSource(params IKnobSource[] sources)
        {
            _sources = sources;
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

        public string GetDisplayString()
        {
            var strings = new List<string>();
            foreach (var source in _sources)
            {
                strings.Add(source.GetDisplayString());
            }
            return string.Join(", ", strings);
        }

        public bool hasEnvironmentSourceWithName(string name)
        {
            foreach (var source in _sources)
            {
                var isEnvironmentSource = source is EnvironmentKnobSource;
                if (isEnvironmentSource)
                {
                    var envName = (source as IEnvironmentKnobSource).GetOriginalName();
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
