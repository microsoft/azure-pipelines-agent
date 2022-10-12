// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Agent.Sdk.Knob
{
    public class BuiltInDefaultKnobSource : IKnobSource
    {
        private string _value;

        public BuiltInDefaultKnobSource(string value)
        {
            _value = value;
        }

        public KnobValue GetValue(IKnobValueContext context)
        {
            return new KnobValue(_value, this);
        }

        public string GetDisplayString()
        {
            return "Default";
        }
    }
}
