// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
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
            KnobValue value = null;
            foreach (var source in _sources)
            {
                try {
                    value = source.GetValue(context);
                }
                catch (NotSupportedException ex)
                {
                    throw new NotSupportedException($"{source.GetType()} not supported for context type {context.GetType()}");
                }
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
