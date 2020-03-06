// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Agent.Sdk.Knob
{

    public class RuntimeKnobSource : IKnobSource
    {
        private string _runTimeVar;
        public RuntimeKnobSource(string runTimeVar)
        {
            _runTimeVar = runTimeVar;
        }

        public KnobValue GetValue(IKnobValueContext context)
        {
            var value = context.GetVariableValueOrDefault(_runTimeVar);
            if (!string.IsNullOrEmpty(value))
            {
                return new KnobValue(value, this);
            }
            return null;
        }

        public string GetDisplayString()
        {
            return $"$({_runTimeVar})";
        }
    }

}
