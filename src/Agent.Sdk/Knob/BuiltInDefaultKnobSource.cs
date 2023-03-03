// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Agent.Sdk.Knob
{
    public class BuiltInDefaultKnobSource : IKnobSource
    {
        private readonly string _defaultValue;

        public string DefaultValue { get => _defaultValue; set { } }

        public BuiltInDefaultKnobSource(string value)
        {
            _defaultValue = value;
        }

        public KnobValue GetValue(IKnobValueContext context)
        {
            return new KnobValue(_defaultValue, this);
        }

        public string GetDisplayString()
        {
            return "Default";
        }
    }
}
