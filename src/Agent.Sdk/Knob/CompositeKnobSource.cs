// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace Agent.Sdk.Knob
{

    public class CompositeKnobSource : IKnobSource
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
    }

}
